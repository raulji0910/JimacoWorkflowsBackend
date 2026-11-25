namespace Jimaco.Aprobaciones.Negocio.Utilidades;

/// <summary>
/// Totales de un documento de inventario como los calcula World Office (2026-11-25): base e IVA se
/// redondean A PESOS en cada renglón y después se suman. Verificado contra la COT 16915 (28 renglones):
/// subtotal 62.906.211, IVA 10.218.433, total 73.124.644 — sin redondear por renglón el IVA daba
/// 10.218.430,09.
/// </summary>
public static class TotalesWO
{
    public static (decimal Subtotal, decimal Iva, decimal Total) Renglon(decimal cantidad, decimal valorUnitario, decimal iva)
    {
        var subtotal = Math.Round(cantidad * valorUnitario, 0, MidpointRounding.AwayFromZero);
        var valorIva = Math.Round(cantidad * valorUnitario * iva, 0, MidpointRounding.AwayFromZero);
        return (subtotal, valorIva, subtotal + valorIva);
    }

    public static (decimal Subtotal, decimal Iva, decimal Total) Documento(IEnumerable<(decimal Cantidad, decimal ValorUnitario, decimal Iva)> renglones)
    {
        decimal subtotal = 0, iva = 0;
        foreach (var (cantidad, valorUnitario, porcentaje) in renglones)
        {
            var r = Renglon(cantidad, valorUnitario, porcentaje);
            subtotal += r.Subtotal;
            iva += r.Iva;
        }
        return (subtotal, iva, subtotal + iva);
    }
}
