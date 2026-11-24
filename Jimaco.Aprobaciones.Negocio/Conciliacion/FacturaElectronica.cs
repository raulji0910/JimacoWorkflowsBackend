namespace Jimaco.Aprobaciones.Negocio.Conciliacion;

// Factura (o nota) electrónica DIAN de un proveedor, tal como viene en el XML UBL 2.1.
// TipoDocumento: "01" factura de venta, "91" nota crédito, "92" nota débito (códigos DIAN).
public sealed record FacturaElectronica(
    string Cufe,
    string Numero,
    string TipoDocumento,
    DateOnly FechaEmision,
    string NitEmisor,
    string NombreEmisor,
    string NitReceptor,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    string? ReferenciaOrden,
    IReadOnlyList<RenglonFacturaElectronica> Renglones)
{
    public bool EsFactura => TipoDocumento is "01" or "02" or "03" or "04";

    // Número de OC que el proveedor puso en el XML (cac:OrderReference), si trae dígitos.
    // Ej. "OC_1275" -> 1275. Muchos proveedores mandan "-" o nada.
    public int? NumeroOrdenReferenciada
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ReferenciaOrden)) return null;
            var digitos = new string(ReferenciaOrden.Where(char.IsDigit).ToArray());
            return digitos.Length is > 0 and <= 9 ? int.Parse(digitos) : null;
        }
    }
}

public sealed record RenglonFacturaElectronica(
    string Descripcion,
    decimal Cantidad,
    decimal Subtotal,
    decimal? PorcentajeIva);
