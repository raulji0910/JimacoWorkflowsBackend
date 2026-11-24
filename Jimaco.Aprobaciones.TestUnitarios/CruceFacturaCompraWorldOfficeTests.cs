using Jimaco.Aprobaciones.Negocio.Conciliacion;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

// Último recurso del cruce: la FC que Contabilidad registra en World Office con la(s) OC en
// Personalizado1. Casos sacados de FERRALIADOS2026 (PASO 14-15 del script de exploración).
public class CruceFacturaCompraWorldOfficeTests
{
    private const string Sodimac = "800242106";
    private const string OtroProveedor = "900419912";
    private static readonly DateOnly Hoy = new(2026, 10, 5);

    private static FacturaElectronica Factura(string numero, string nit, decimal subtotal, decimal iva)
        => new($"cufe-{numero}", numero, "01", Hoy, nit, "PROVEEDOR", "900632353", subtotal, iva, subtotal + iva, null, []);

    private static OrdenCompraConciliable Oc(int numero, string nit, DateOnly fecha, decimal subtotal, decimal iva)
        => new(numero + 10000, numero, fecha, nit, $"PROVEEDOR {nit}", subtotal, iva);

    private static FacturaCompraConciliable Fc(int numero, string nit, string? documentoExterno, string? referencia)
        => new(numero + 50000, numero, Hoy, nit, "PROVEEDOR", documentoExterno, referencia);

    [Theory]
    [InlineData(null, new int[0])]
    [InlineData("-", new int[0])]
    [InlineData("1187", new[] { 1187 })]
    [InlineData("1120-1187", new[] { 1120, 1187 })]
    [InlineData("731-734", new[] { 731, 734 })]
    [InlineData("OC 1187, 1190", new[] { 1187, 1190 })]
    public void Extraer_LeeLasOcQueEscribeContabilidad(string? texto, int[] esperadas)
        => Assert.Equal(esperadas, ReferenciasOrdenCompra.Extraer(texto));

    [Theory]
    [InlineData("1511100482996", "1511100482996", true)]
    [InlineData("317027", "FE317027", true)]
    [InlineData("FE317027", "317027", true)]
    [InlineData("0317027", "FE317027", true)]
    [InlineData("27", "FE317027", false)]   // cola demasiado corta: podría ser otra factura
    [InlineData("317028", "FE317027", false)]
    [InlineData(null, "FE317027", false)]
    public void CorrespondeA_ComparaElNumeroDeLaFacturaPorDigitos(string? documentoExterno, string numeroXml, bool esperado)
        => Assert.Equal(esperado, Fc(1, Sodimac, documentoExterno, "1").CorrespondeA(Factura(numeroXml, Sodimac, 1, 0)));

    [Fact]
    public void SinOcPorValor_UsaLaOcDeLaFcDeWorldOffice()
    {
        // El XML no cuadra por valor con nada (la OC es de hace 4 meses, fuera de la ventana), pero
        // Contabilidad registró la FC con esa OC.
        var resultado = MotorConciliacion.Conciliar(
            [Factura("1511100482996", Sodimac, 125_882m, 23_918m)],
            [Oc(1187, Sodimac, Hoy.AddDays(-120), 125_882m, 23_918m)],
            facturasCompra: [Fc(1069, Sodimac, "1511100482996", "1187")]);

        var r = Assert.Single(resultado.Facturas);
        Assert.Equal(EstadoConciliacion.Cuadra, r.Estado);
        Assert.Equal(1187, r.OrdenCompra!.Numero);
        Assert.Equal(1069, r.FacturaCompra!.Numero);
        Assert.Empty(resultado.OrdenesPendientes);
    }

    [Fact]
    public void FcCitaOcDeOtroProveedor_SeDescartaYSeReporta()
    {
        // Caso real FC 1069: "1120-1187"; la 1120 es de otro NIT (error de digitación).
        var resultado = MotorConciliacion.Conciliar(
            [Factura("1511100482996", Sodimac, 125_882m, 23_918m)],
            [Oc(1187, Sodimac, Hoy, 125_882m, 23_918m), Oc(1120, OtroProveedor, Hoy.AddDays(-11), 500_000m, 95_000m)],
            facturasCompra: [Fc(1069, Sodimac, "1511100482996", "1120-1187")]);

        Assert.Equal(1187, resultado.Facturas[0].OrdenCompra!.Numero);
        var invalida = Assert.Single(resultado.ReferenciasInvalidas);
        Assert.Equal(1120, invalida.NumeroOrdenCitada);
        Assert.Contains("otro proveedor", invalida.Motivo);
        // La 1120 sigue pendiente de su propio proveedor: la FC de Sodimac no la cubre.
        Assert.Equal(1120, Assert.Single(resultado.OrdenesPendientes).OrdenCompra.Numero);
    }

    [Fact]
    public void FcConVariasOc_SumaLosSaldosYLasConsumeTodas()
    {
        var resultado = MotorConciliacion.Conciliar(
            [Factura("FE900", Sodimac, 300_000m, 57_000m)],
            [Oc(731, Sodimac, Hoy.AddDays(-200), 100_000m, 19_000m), Oc(734, Sodimac, Hoy.AddDays(-200), 200_000m, 38_000m)],
            facturasCompra: [Fc(500, Sodimac, "FE900", "731-734")]);

        var r = Assert.Single(resultado.Facturas);
        Assert.Equal(EstadoConciliacion.Cuadra, r.Estado);
        Assert.Equal([731, 734], r.TodasLasOrdenes.Select(o => o.Numero));
        Assert.Empty(resultado.OrdenesPendientes);
    }

    [Fact]
    public void FcConValorDistinto_DiferenciaPeroLaOcNoQuedaComoSinFactura()
    {
        var resultado = MotorConciliacion.Conciliar(
            [Factura("FE901", Sodimac, 90_000m, 17_100m)],
            [Oc(1200, Sodimac, Hoy.AddDays(-200), 150_000m, 28_500m)],
            facturasCompra: [Fc(501, Sodimac, "FE901", "1200")]);

        var r = Assert.Single(resultado.Facturas);
        Assert.Equal(EstadoConciliacion.Diferencia, r.Estado);
        Assert.Equal(-60_000m, r.DiferenciaSubtotal);
        Assert.Empty(resultado.OrdenesPendientes);
    }

    [Fact]
    public void CruceNormalPorValor_TienePrioridadSobreLaFc()
    {
        // La FC de WO es solo el último recurso: si el valor cuadra con una OC, se queda esa.
        var resultado = MotorConciliacion.Conciliar(
            [Factura("FE902", Sodimac, 80_000m, 15_200m)],
            [Oc(1300, Sodimac, Hoy.AddDays(-2), 80_000m, 15_200m), Oc(1301, Sodimac, Hoy.AddDays(-200), 80_000m, 15_200m)],
            facturasCompra: [Fc(502, Sodimac, "FE902", "1301")]);

        var r = Assert.Single(resultado.Facturas);
        Assert.Equal(1300, r.OrdenCompra!.Numero);
        Assert.Null(r.FacturaCompra);
    }

    [Fact]
    public void FcSinXml_SusOcPasanAFacturadasSinXml()
    {
        // Contabilidad registró la FC pero el XML nunca llegó al correo (caso Sodimac).
        var resultado = MotorConciliacion.Conciliar(
            [],
            [Oc(1187, Sodimac, Hoy, 125_882m, 23_918m), Oc(1188, Sodimac, Hoy, 40_000m, 7_600m)],
            facturasCompra: [Fc(1069, Sodimac, "1511100482996", "1187")]);

        var facturada = Assert.Single(resultado.OrdenesFacturadasSinXml);
        Assert.Equal(1187, facturada.OrdenCompra.Numero);
        Assert.Equal(1069, facturada.FacturaCompra.Numero);
        Assert.Equal(1188, Assert.Single(resultado.OrdenesPendientes).OrdenCompra.Numero);
    }

    [Fact]
    public void FcCitaOcQueNoExiste_SeReporta()
    {
        var resultado = MotorConciliacion.Conciliar(
            [],
            [Oc(1187, Sodimac, Hoy, 125_882m, 23_918m)],
            facturasCompra: [Fc(1070, Sodimac, "FE1", "99999")]);

        Assert.Contains("no está entre las OC", Assert.Single(resultado.ReferenciasInvalidas).Motivo);
        Assert.Single(resultado.OrdenesPendientes);
    }
}
