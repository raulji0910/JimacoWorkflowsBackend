using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Jimaco.Aprobaciones.Negocio.Conciliacion;

// Lee los adjuntos que llegan al correo de facturación: un ZIP con el XML (y el PDF) de la DIAN.
// El XML puede ser la Invoice directa o un AttachedDocument que la trae embebida como texto
// (CDATA) en cac:Attachment — la mayoría de proveedores usan el segundo formato.
public static class LectorFacturaElectronica
{
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    public static IReadOnlyList<FacturaElectronica> LeerZip(Stream zip)
    {
        using var archivo = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        var facturas = new List<FacturaElectronica>();
        foreach (var entrada in archivo.Entries.Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using var lector = new StreamReader(entrada.Open());
            var factura = LeerXml(lector.ReadToEnd());
            if (factura is not null) facturas.Add(factura);
        }
        return facturas;
    }

    // Devuelve null si el XML no es un documento electrónico reconocible (p. ej. un
    // ApplicationResponse de acuse, que algunos proveedores también adjuntan).
    public static FacturaElectronica? LeerXml(string xml)
    {
        var documento = BuscarDocumento(XDocument.Parse(xml).Root!);
        return documento is null ? null : Mapear(documento);
    }

    private static XElement? BuscarDocumento(XElement raiz)
    {
        if (raiz.Name.LocalName is "Invoice" or "CreditNote" or "DebitNote") return raiz;
        if (raiz.Name.LocalName != "AttachedDocument") return null;

        foreach (var descripcion in raiz.Descendants(Cac + "ExternalReference").Elements(Cbc + "Description"))
        {
            var texto = descripcion.Value.Trim();
            if (!texto.StartsWith('<')) continue;
            try
            {
                var embebido = XDocument.Parse(texto).Root!;
                if (embebido.Name.LocalName is "Invoice" or "CreditNote" or "DebitNote") return embebido;
            }
            catch (System.Xml.XmlException)
            {
                // Descripción con texto que parece XML pero no lo es: se ignora y se sigue buscando.
            }
        }
        return null;
    }

    private static FacturaElectronica Mapear(XElement doc)
    {
        var tipo = doc.Name.LocalName switch
        {
            "CreditNote" => "91",
            "DebitNote" => "92",
            _ => Texto(doc.Element(Cbc + "InvoiceTypeCode")) ?? "01",
        };

        // Las notas débito usan RequestedMonetaryTotal en vez de LegalMonetaryTotal.
        var totales = doc.Element(Cac + "LegalMonetaryTotal") ?? doc.Element(Cac + "RequestedMonetaryTotal");

        return new FacturaElectronica(
            Cufe: Texto(doc.Element(Cbc + "UUID")) ?? "",
            Numero: Texto(doc.Element(Cbc + "ID")) ?? "",
            TipoDocumento: tipo,
            FechaEmision: DateOnly.Parse(Texto(doc.Element(Cbc + "IssueDate"))!, CultureInfo.InvariantCulture),
            NitEmisor: Nit(doc.Element(Cac + "AccountingSupplierParty")),
            NombreEmisor: Texto(doc.Element(Cac + "AccountingSupplierParty")?.Descendants(Cbc + "RegistrationName").FirstOrDefault()) ?? "",
            NitReceptor: Nit(doc.Element(Cac + "AccountingCustomerParty")),
            Subtotal: Subtotal(totales),
            Iva: doc.Elements(Cac + "TaxTotal").Where(EsIva).Sum(t => Numero(t.Element(Cbc + "TaxAmount"))),
            Total: Numero(totales?.Element(Cbc + "PayableAmount")),
            ReferenciaOrden: Texto(doc.Element(Cac + "OrderReference")?.Element(Cbc + "ID")),
            Renglones: doc.Elements().Where(e => e.Name.LocalName is "InvoiceLine" or "CreditNoteLine" or "DebitNoteLine")
                .Select(MapearRenglon)
                .ToList());
    }

    /// <summary>
    /// Subtotal de los productos antes de IVA (2026-11-24). En la DIAN, <c>TaxExclusiveAmount</c> es la
    /// BASE GRAVABLE, no el subtotal: vale 0 en una factura sin IVA (agua, gasolina), deja por fuera los
    /// renglones sin IVA en una mixta y en los servicios con AIU es solo el 10 % (el IVA va sobre la
    /// utilidad). Por eso el subtotal es <c>LineExtensionAmount</c> (suma de los renglones), salvo
    /// cuando hay un descuento global ANTES del IVA (caso real INVESA: renglones 100.000, descuento
    /// 25.000, base 75.000): ahí la base es exactamente renglones − descuento y la OC dice 75.000. Un
    /// descuento que no cuadra así es de pronto pago (después del IVA) y no cambia el costo.
    /// Medido sobre las 5.291 facturas de producción: 4.480 quedan igual, 809 se corrigen.
    /// </summary>
    private static decimal Subtotal(XElement? totales)
    {
        var renglones = Numero(totales?.Element(Cbc + "LineExtensionAmount"));
        var baseGravable = Numero(totales?.Element(Cbc + "TaxExclusiveAmount"));
        var descuento = Numero(totales?.Element(Cbc + "AllowanceTotalAmount"));

        return descuento > 0 && Math.Abs(baseGravable - (renglones - descuento)) <= 1m
            ? renglones - descuento
            : renglones;
    }

    private static RenglonFacturaElectronica MapearRenglon(XElement renglon)
    {
        var cantidad = renglon.Elements().FirstOrDefault(e => e.Name.LocalName is "InvoicedQuantity" or "CreditedQuantity" or "DebitedQuantity");
        var porcentaje = renglon.Elements(Cac + "TaxTotal").Where(EsIva)
            .Select(t => t.Descendants(Cbc + "Percent").FirstOrDefault())
            .FirstOrDefault(p => p is not null);

        return new RenglonFacturaElectronica(
            Descripcion: Texto(renglon.Element(Cac + "Item")?.Element(Cbc + "Description")) ?? "",
            Cantidad: Numero(cantidad),
            Subtotal: Numero(renglon.Element(Cbc + "LineExtensionAmount")),
            PorcentajeIva: porcentaje is null ? null : Numero(porcentaje));
    }

    // IVA es el esquema tributario "01" de la DIAN (INC es "04", ICA "03", etc.).
    private static bool EsIva(XElement taxTotal)
        => taxTotal.Descendants(Cac + "TaxScheme").Elements(Cbc + "ID").Any(id => id.Value.Trim() == "01");

    // El CompanyID trae el NIT sin dígito de verificación (el DV va en el atributo schemeID).
    private static string Nit(XElement? parte)
    {
        var companyId = parte?.Descendants(Cac + "PartyTaxScheme").Elements(Cbc + "CompanyID").FirstOrDefault()
                        ?? parte?.Descendants(Cbc + "CompanyID").FirstOrDefault();
        return NormalizarNit(companyId?.Value);
    }

    public static string NormalizarNit(string? nit)
        => new((nit ?? "").Where(char.IsDigit).ToArray());

    private static string? Texto(XElement? e) => string.IsNullOrWhiteSpace(e?.Value) ? null : e.Value.Trim();

    private static decimal Numero(XElement? e)
        => decimal.TryParse(e?.Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) ? valor : 0m;
}
