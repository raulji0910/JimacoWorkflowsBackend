using System.Text.RegularExpressions;

namespace Jimaco.Aprobaciones.Negocio.Conciliacion;

// Factura de compra (prefijo "FC") que Contabilidad ya registró en World Office. Sirve como último
// recurso del cruce: en el campo Personalizado1 del encabezado Contabilidad escribe la(s) OC que
// cubre esa factura, y NúmDocumentoExterno es el número de la factura del proveedor (el del XML).
public sealed record FacturaCompraConciliable(
    int IdAsientoContable,
    int Numero,
    DateOnly Fecha,
    string NitProveedor,
    string NombreProveedor,
    string? DocumentoExterno,
    string? ReferenciaOrdenes)
{
    // Lo que escribe Contabilidad a mano: "1187", "1120-1187" (el guion separa varias OC, no es un
    // rango: la 732 y la 733 aparecen solas en otras FC aunque exista "731-734"), "-" = sin OC.
    public IReadOnlyList<int> OrdenesCitadas => ReferenciasOrdenCompra.Extraer(ReferenciaOrdenes);

    // El número de la factura del proveedor se compara solo por dígitos: en WO a veces se digita
    // sin el prefijo ("317027" para el XML "FE317027") y a veces con él.
    public bool CorrespondeA(FacturaElectronica factura)
    {
        var externo = SoloDigitos(DocumentoExterno);
        var xml = SoloDigitos(factura.Numero);
        if (externo.Length == 0 || xml.Length == 0) return false;
        if (externo == xml) return true;
        // Por la cola solo con números largos: con 3-4 dígitos dos facturas del mismo proveedor
        // podrían coincidir de casualidad.
        var corto = externo.Length < xml.Length ? externo : xml;
        var largo = externo.Length < xml.Length ? xml : externo;
        return corto.Length >= 5 && largo.EndsWith(corto, StringComparison.Ordinal);
    }

    private static string SoloDigitos(string? texto) => new string((texto ?? "").Where(char.IsDigit).ToArray()).TrimStart('0');
}

// Una OC citada en una FC que no se pudo usar (otro proveedor o no está entre las OC sincronizadas):
// casi siempre un error de digitación de Contabilidad, para que lo corrija en WO.
public sealed record ReferenciaOrdenInvalida(FacturaCompraConciliable FacturaCompra, int NumeroOrdenCitada, string Motivo);

// OC que una FC de WO dice cubrir, pero cuyo XML no llegó (no está en el correo ni se cargó): no es
// una OC sin facturar, es un XML que falta bajar de la DIAN.
public sealed record OrdenFacturadaSinXml(OrdenCompraConciliable OrdenCompra, FacturaCompraConciliable FacturaCompra);

public static partial class ReferenciasOrdenCompra
{
    public static IReadOnlyList<int> Extraer(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];
        return Numeros().Matches(texto)
            .Select(m => int.TryParse(m.Value, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();
    }

    [GeneratedRegex(@"\d{1,9}")]
    private static partial Regex Numeros();
}
