using System.IO.Compression;
using System.Text;
using Jimaco.Aprobaciones.Negocio.Conciliacion;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

public class LectorFacturaElectronicaTests
{
    // Factura mínima con la misma forma que las reales de los proveedores (UBL 2.1 DIAN).
    internal static string Invoice(
        string numero = "FE5611847", string nitEmisor = "890900652", string fecha = "2026-10-21",
        decimal lineExtension = 100000m, decimal taxExclusive = 75000m, decimal iva = 14250m,
        decimal payable = 89250m, string? referenciaOrden = null, decimal? descuentoGlobal = 25000m)
        => $"""
           <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                    xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                    xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
             <cbc:ID>{numero}</cbc:ID>
             <cbc:UUID schemeName="CUFE-SHA384">cufe-{numero}</cbc:UUID>
             <cbc:IssueDate>{fecha}</cbc:IssueDate>
             <cbc:InvoiceTypeCode>01</cbc:InvoiceTypeCode>
             {(referenciaOrden is null ? "" : $"<cac:OrderReference><cbc:ID>{referenciaOrden}</cbc:ID></cac:OrderReference>")}
             <cac:AccountingSupplierParty><cac:Party><cac:PartyTaxScheme>
               <cbc:RegistrationName>PROVEEDOR S.A</cbc:RegistrationName>
               <cbc:CompanyID schemeID="6" schemeName="31">{nitEmisor}</cbc:CompanyID>
             </cac:PartyTaxScheme></cac:Party></cac:AccountingSupplierParty>
             <cac:AccountingCustomerParty><cac:Party><cac:PartyTaxScheme>
               <cbc:RegistrationName>JIMACO S.A.S</cbc:RegistrationName>
               <cbc:CompanyID schemeID="1" schemeName="31">900632353</cbc:CompanyID>
             </cac:PartyTaxScheme></cac:Party></cac:AccountingCustomerParty>
             <cac:TaxTotal>
               <cbc:TaxAmount currencyID="COP">{iva.ToString(System.Globalization.CultureInfo.InvariantCulture)}</cbc:TaxAmount>
               <cac:TaxSubtotal><cac:TaxCategory><cbc:Percent>19.00</cbc:Percent>
                 <cac:TaxScheme><cbc:ID>01</cbc:ID><cbc:Name>IVA</cbc:Name></cac:TaxScheme>
               </cac:TaxCategory></cac:TaxSubtotal>
             </cac:TaxTotal>
             <cac:LegalMonetaryTotal>
               <cbc:LineExtensionAmount currencyID="COP">{lineExtension.ToString(System.Globalization.CultureInfo.InvariantCulture)}</cbc:LineExtensionAmount>
               <cbc:TaxExclusiveAmount currencyID="COP">{taxExclusive.ToString(System.Globalization.CultureInfo.InvariantCulture)}</cbc:TaxExclusiveAmount>
               {(descuentoGlobal is null ? "" : $"<cbc:AllowanceTotalAmount currencyID=\"COP\">{descuentoGlobal.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}</cbc:AllowanceTotalAmount>")}
               <cbc:PayableAmount currencyID="COP">{payable.ToString(System.Globalization.CultureInfo.InvariantCulture)}</cbc:PayableAmount>
             </cac:LegalMonetaryTotal>
             <cac:InvoiceLine>
               <cbc:ID>1</cbc:ID>
               <cbc:InvoicedQuantity unitCode="94">10</cbc:InvoicedQuantity>
               <cbc:LineExtensionAmount currencyID="COP">{lineExtension.ToString(System.Globalization.CultureInfo.InvariantCulture)}</cbc:LineExtensionAmount>
               <cac:TaxTotal><cbc:TaxAmount currencyID="COP">{iva.ToString(System.Globalization.CultureInfo.InvariantCulture)}</cbc:TaxAmount>
                 <cac:TaxSubtotal><cac:TaxCategory><cbc:Percent>19.00</cbc:Percent>
                   <cac:TaxScheme><cbc:ID>01</cbc:ID><cbc:Name>IVA</cbc:Name></cac:TaxScheme>
                 </cac:TaxCategory></cac:TaxSubtotal></cac:TaxTotal>
               <cac:Item><cbc:Description>INVECRYL 500 750 G</cbc:Description></cac:Item>
             </cac:InvoiceLine>
           </Invoice>
           """;

    internal static string AttachedDocument(string invoiceXml)
        => $"""
           <?xml version="1.0" encoding="UTF-8"?>
           <AttachedDocument xmlns="urn:oasis:names:specification:ubl:schema:xsd:AttachedDocument-2"
                             xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                             xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
             <cbc:ID>ad-1</cbc:ID>
             <cac:Attachment><cac:ExternalReference>
               <cbc:MimeCode>text/xml</cbc:MimeCode>
               <cbc:Description><![CDATA[{invoiceXml}]]></cbc:Description>
             </cac:ExternalReference></cac:Attachment>
           </AttachedDocument>
           """;

    [Fact]
    public void LeerXml_AttachedDocument_ExtraeLaFacturaEmbebida()
    {
        var factura = LectorFacturaElectronica.LeerXml(AttachedDocument(Invoice()));

        Assert.NotNull(factura);
        Assert.Equal("FE5611847", factura.Numero);
        Assert.Equal("cufe-FE5611847", factura.Cufe);
        Assert.Equal("01", factura.TipoDocumento);
        Assert.Equal(new DateOnly(2026, 10, 21), factura.FechaEmision);
        Assert.Equal("890900652", factura.NitEmisor);
        Assert.Equal("900632353", factura.NitReceptor);
        Assert.Equal(14250m, factura.Iva);
        Assert.Equal(89250m, factura.Total);
        Assert.Single(factura.Renglones);
        Assert.Equal("INVECRYL 500 750 G", factura.Renglones[0].Descripcion);
        Assert.Equal(19m, factura.Renglones[0].PorcentajeIva);
    }

    [Fact]
    public void LeerXml_DescuentoGlobalAntesDelIva_SubtotalYaDescontado()
    {
        // Caso real INVESA: 10 x 10.000 con 25% de descuento (AllowanceTotalAmount 25.000, base 75.000)
        // -> la OC dice 75.000, no 100.000.
        var factura = LectorFacturaElectronica.LeerXml(Invoice(lineExtension: 100000m, taxExclusive: 75000m, descuentoGlobal: 25000m));

        Assert.Equal(75000m, factura!.Subtotal);
    }

    // 2026-11-24: TaxExclusiveAmount es la BASE GRAVABLE de la DIAN, no el subtotal. Casos reales de Jimaco.
    [Theory]
    // Sin IVA (agua, FE2374): base 0 -> el subtotal es el valor de los renglones, no 0.
    [InlineData("119000", "0", null, "119000")]
    // Mixta con renglones sin IVA: la base solo trae lo gravado.
    [InlineData("1563298", "1342734", null, "1563298")]
    // Servicio de aseo con AIU (LIMPIO): el IVA va solo sobre el 10%.
    [InlineData("3000000", "272727.27", "0", "3000000")]
    // Descuento de pronto pago DESPUÉS del IVA: la base no lo descuenta, el costo tampoco.
    [InlineData("1264000", "1264000", "151250", "1264000")]
    // Descuento global ANTES del IVA (TUBOSA): base = renglones - descuento.
    [InlineData("58822296", "21192569", "37629727", "21192569")]
    // Bolsas de obsequio (valen 0 en el renglón pero suman a la base): manda el valor de los renglones.
    [InlineData("1134118", "1136074", "372", "1134118")]
    public void LeerXml_Subtotal_EsElDeLosRenglonesSalvoDescuentoAntesDelIva(
        string renglones, string baseGravable, string? descuento, string esperado)
    {
        static decimal D(string v) => decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
        var factura = LectorFacturaElectronica.LeerXml(Invoice(
            lineExtension: D(renglones), taxExclusive: D(baseGravable),
            descuentoGlobal: descuento is null ? null : D(descuento)));

        Assert.Equal(D(esperado), factura!.Subtotal);
    }

    [Theory]
    [InlineData("OC_1275", 1275)]
    [InlineData("OC 1312", 1312)]
    [InlineData("-", null)]
    public void NumeroOrdenReferenciada_ExtraeLosDigitos(string referencia, int? esperado)
    {
        var factura = LectorFacturaElectronica.LeerXml(Invoice(referenciaOrden: referencia));

        Assert.Equal(esperado, factura!.NumeroOrdenReferenciada);
    }

    [Fact]
    public void LeerXml_DocumentoQueNoEsFactura_DevuelveNull()
    {
        const string acuse = """<ApplicationResponse xmlns="urn:oasis:names:specification:ubl:schema:xsd:ApplicationResponse-2" />""";

        Assert.Null(LectorFacturaElectronica.LeerXml(acuse));
    }

    [Fact]
    public void LeerZip_IgnoraElPdfYLeeElXml()
    {
        using var zip = new MemoryStream();
        using (var archivo = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var xml = new StreamWriter(archivo.CreateEntry("ad0890900652.xml").Open(), Encoding.UTF8))
                xml.Write(AttachedDocument(Invoice()));
            using (var pdf = archivo.CreateEntry("fv0890900652.pdf").Open())
                pdf.Write([0x25, 0x50, 0x44, 0x46]);
        }
        zip.Position = 0;

        var facturas = LectorFacturaElectronica.LeerZip(zip);

        Assert.Single(facturas);
        Assert.Equal("FE5611847", facturas[0].Numero);
    }
}
