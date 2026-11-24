using Jimaco.Aprobaciones.Negocio.Conciliacion;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

public class MotorConciliacionTests
{
    private static FacturaElectronica Factura(string numero, string nit, DateOnly fecha, decimal subtotal, decimal iva, string? referencia = null)
        => new($"cufe-{numero}", numero, "01", fecha, nit, "PROVEEDOR", "900632353", subtotal, iva, subtotal + iva, referencia, []);

    private static OrdenCompraConciliable Oc(int numero, string nit, DateOnly fecha, decimal subtotal, decimal iva)
        => new(numero + 10000, numero, fecha, nit, "PROVEEDOR", subtotal, iva);

    private static readonly DateOnly Hoy = new(2026, 10, 20);

    [Fact]
    public void MismoNitYValores_Cuadra()
    {
        var resultado = MotorConciliacion.Conciliar(
            [Factura("FE5611847", "890900652", Hoy, 75000m, 14250m)],
            [Oc(1332, "890900652", Hoy.AddDays(-1), 75000m, 14250m)]);

        var r = Assert.Single(resultado.Facturas);
        Assert.Equal(EstadoConciliacion.Cuadra, r.Estado);
        Assert.Equal(1332, r.OrdenCompra!.Numero);
        Assert.Empty(resultado.OrdenesPendientes);
    }

    [Fact]
    public void RedondeoDeUnPeso_Cuadra()
    {
        // Caso real Grupo Silver FB2142: XML 406.305 vs OC 406.303,35 (precio con IVA / 1,19).
        var resultado = MotorConciliacion.Conciliar(
            [Factura("FB2142", "900420715", Hoy, 406305m, 77198m)],
            [Oc(1312, "900420715", Hoy.AddDays(-1), 406303.35m, 77197.64m)]);

        Assert.Equal(EstadoConciliacion.Cuadra, resultado.Facturas[0].Estado);
    }

    [Fact]
    public void ProveedorCobraDistinto_Diferencia()
    {
        // Caso real Imporinox: XML 72.674 vs OC 1307 por 73.109,24.
        var resultado = MotorConciliacion.Conciliar(
            [Factura("FBTA222825", "805012368", Hoy, 72674m, 13808.06m)],
            [Oc(1307, "805012368", Hoy.AddDays(-4), 73109.24m, 13890.76m)]);

        var r = resultado.Facturas[0];
        Assert.Equal(EstadoConciliacion.Diferencia, r.Estado);
        Assert.Equal(1307, r.OrdenCompra!.Numero);
        Assert.Equal(-435.24m, r.DiferenciaSubtotal);
        Assert.Empty(resultado.OrdenesPendientes);
    }

    [Fact]
    public void ValorMuyDistintoDeLaOcMasCercana_DiferenciaSinConsumirLaOc()
    {
        var resultado = MotorConciliacion.Conciliar(
            [Factura("FE27531", "900457218", Hoy, 2_000_000m, 380_000m)],
            [Oc(1038, "900457218", Hoy.AddDays(-30), 1_243_372.72m, 236_240.82m)]);

        Assert.Equal(EstadoConciliacion.Diferencia, resultado.Facturas[0].Estado);
        Assert.Single(resultado.OrdenesPendientes);
    }

    [Fact]
    public void OrdenReferenciadaEnElXml_TienePrioridadSobreOtraOcDelMismoValor()
    {
        var resultado = MotorConciliacion.Conciliar(
            [Factura("BOG1336084", "860030723", Hoy, 55856m, 10612.64m, referencia: "OC_1275")],
            [
                Oc(1270, "860030723", Hoy.AddDays(-2), 55856m, 10612.64m),
                Oc(1275, "860030723", Hoy.AddDays(-9), 55856m, 10612.64m),
            ]);

        Assert.Equal(1275, resultado.Facturas[0].OrdenCompra!.Numero);
        Assert.Equal(1270, Assert.Single(resultado.OrdenesPendientes).OrdenCompra.Numero);
    }

    [Fact]
    public void DosFacturasContraUnaOc_ParcialYLuegoCompletaElSaldo()
    {
        var resultado = MotorConciliacion.Conciliar(
            [
                Factura("A1", "890900943", Hoy, 100000m, 19000m),
                Factura("A2", "890900943", Hoy.AddDays(3), 25000m, 4750m),
            ],
            [Oc(811, "890900943", Hoy.AddDays(-30), 125000m, 23750m)]);

        Assert.Equal(EstadoConciliacion.Parcial, resultado.Facturas[0].Estado);
        Assert.Equal(EstadoConciliacion.Cuadra, resultado.Facturas[1].Estado);
        Assert.All(resultado.Facturas, r => Assert.Equal(811, r.OrdenCompra!.Numero));
        Assert.Empty(resultado.OrdenesPendientes);
    }

    [Fact]
    public void ProveedorSinOc_SinOrdenCompra()
    {
        var resultado = MotorConciliacion.Conciliar(
            [Factura("A9311125715", "830513729", Hoy, 176935.55m, 0m)],
            [Oc(1332, "890900652", Hoy, 75000m, 14250m)]);

        Assert.Equal(EstadoConciliacion.SinOrdenCompra, resultado.Facturas[0].Estado);
        Assert.Single(resultado.OrdenesPendientes);
    }

    [Fact]
    public void OcFueraDeLaVentanaDeFechas_NoSeUsa()
    {
        var resultado = MotorConciliacion.Conciliar(
            [Factura("X1", "890900652", Hoy, 75000m, 14250m)],
            [Oc(500, "890900652", Hoy.AddDays(-120), 75000m, 14250m)]);

        Assert.Equal(EstadoConciliacion.SinOrdenCompra, resultado.Facturas[0].Estado);
    }

    [Fact]
    public void NotasCredito_NoEntranAlCruce()
    {
        var nota = Factura("NC1", "890900652", Hoy, 75000m, 14250m) with { TipoDocumento = "91" };

        var resultado = MotorConciliacion.Conciliar([nota], [Oc(1332, "890900652", Hoy, 75000m, 14250m)]);

        Assert.Empty(resultado.Facturas);
    }

    [Fact]
    public void DosOcIgualesDelMismoProveedor_CadaFacturaTomaUnaDistinta()
    {
        var resultado = MotorConciliacion.Conciliar(
            [
                Factura("F1", "830016209", Hoy, 50000m, 9500m),
                Factura("F2", "830016209", Hoy.AddDays(1), 50000m, 9500m),
            ],
            [
                Oc(1, "830016209", Hoy.AddDays(-5), 50000m, 9500m),
                Oc(2, "830016209", Hoy.AddDays(-1), 50000m, 9500m),
            ]);

        Assert.All(resultado.Facturas, r => Assert.Equal(EstadoConciliacion.Cuadra, r.Estado));
        Assert.NotEqual(resultado.Facturas[0].OrdenCompra!.Numero, resultado.Facturas[1].OrdenCompra!.Numero);
    }
}
