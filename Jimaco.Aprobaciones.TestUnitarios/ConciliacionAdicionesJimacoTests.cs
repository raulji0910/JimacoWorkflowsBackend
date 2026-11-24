using System.IO.Compression;
using System.Text;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Negocio.Conciliacion;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>
/// Lo que la conciliación de Jimaco agrega sobre la de Ferrealiados (2026-11-24): descarga de XML a
/// demanda, observaciones con marca "Revisada" en facturas Parciales / Con diferencia, emisor de cada
/// OC y el permiso del rol Conciliador.
/// </summary>
public class ConciliacionAdicionesJimacoTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 30);
    private const int Ana = ConciliacionTestsDatos.Ana;

    private static ConciliacionService CrearServicio(AppDbContext db) => new(db, new TiempoFijo(Hoy.ToDateTime(TimeOnly.MinValue)));

    private static FacturaXmlEntradaDto Xml(string numero, decimal subtotal = 75000m, decimal iva = 14250m)
        => new(LectorFacturaElectronicaTests.AttachedDocument(LectorFacturaElectronicaTests.Invoice(
            numero: numero, taxExclusive: subtotal, iva: iva, payable: subtotal + iva)), $"{numero}.xml", "asunto", null);

    private static OrdenCompraEntradaDto Oc(int idAsiento, int numero, decimal valorUnitario, string? emisor = "LAURA SOFIA HERNANDEZ CARO")
        => new(idAsiento, numero, new DateOnly(2026, 10, 20), "890.900.652", "PROVEEDOR S.A", false,
            [new OrdenCompraRenglonEntradaDto("COD-1", "PRODUCTO", 10m, valorUnitario, 19m)], emisor);

    [Fact]
    public async Task XmlSeGuardaComprimidoYSeDescargaIgual()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var servicio = CrearServicio(db);
        var original = Xml("FE100");
        await servicio.RegistrarFacturasAsync([original]);
        var factura = await db.FacturasProveedor.SingleAsync();

        Assert.Equal(original.Xml, XmlComprimido.Descomprimir(factura.XmlGzip));

        var archivo = await servicio.DescargarXmlAsync([factura.Id], Ana);
        Assert.Equal("PROVEEDOR S.A_FE100.xml", archivo.NombreArchivo);
        Assert.Equal("application/xml", archivo.ContentType);
        Assert.Equal(original.Xml, Encoding.UTF8.GetString(archivo.Contenido));
    }

    [Fact]
    public async Task VariasFacturas_SeDescarganEnUnZipConUnXmlPorFactura()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var servicio = CrearServicio(db);
        await servicio.RegistrarFacturasAsync([Xml("FE1"), Xml("FE2"), Xml("FE3")]);
        var ids = await db.FacturasProveedor.Select(f => f.Id).ToListAsync();

        var archivo = await servicio.DescargarXmlAsync(ids, Ana);

        Assert.Equal("application/zip", archivo.ContentType);
        Assert.EndsWith(".zip", archivo.NombreArchivo);
        using var zip = new ZipArchive(new MemoryStream(archivo.Contenido));
        Assert.Equal(["PROVEEDOR S.A_FE1.xml", "PROVEEDOR S.A_FE2.xml", "PROVEEDOR S.A_FE3.xml"],
            zip.Entries.Select(e => e.Name).Order().ToArray());
        using var lector = new StreamReader(zip.Entries.First(e => e.Name.EndsWith("FE2.xml")).Open());
        Assert.Contains("<cbc:ID>FE2</cbc:ID>", lector.ReadToEnd());
    }

    [Fact]
    public async Task DescargarXml_SinFacturasOSinPermiso_Rechaza()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var servicio = CrearServicio(db);
        await servicio.RegistrarFacturasAsync([Xml("FE1")]);
        var id = (await db.FacturasProveedor.SingleAsync()).Id;

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.DescargarXmlAsync([], Ana));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => servicio.DescargarXmlAsync([9999], Ana));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.DescargarXmlAsync([id], ConciliacionTestsDatos.SinPermiso));
    }

    [Theory]
    [InlineData("FERRETERIA RHINO SA", "FE 123", "FERRETERIA RHINO SA_FE 123.xml")]
    [InlineData("A/B: C*D?", "1/2", "A B  C D_1 2.xml")]
    [InlineData("   ", "9", "PROVEEDOR_9.xml")]
    public void NombreXml_QuitaCaracteresInvalidos(string proveedor, string numero, string esperado)
        => Assert.Equal(esperado, ConciliacionService.NombreXml(proveedor, numero));

    [Fact]
    public async Task ObservacionesFactura_HistorialYMarcaRevisada()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var servicio = CrearServicio(db);
        // OC de 80.000 y factura de 75.000: deja 6,25 % de saldo → entrega parcial.
        await servicio.RegistrarFacturasAsync([Xml("FE7", subtotal: 75000m)]);
        await servicio.SincronizarOrdenesAsync([Oc(500, 20058, 8000m)]);
        var id = (await db.FacturasProveedor.SingleAsync()).Id;

        await servicio.AgregarObservacionFacturaAsync(id, new NuevaObservacionFacturaDto("Falta una entrega"), Ana);
        var revisada = await servicio.AgregarObservacionFacturaAsync(id,
            new NuevaObservacionFacturaDto("Proveedor subió precio, autorizado por gerencia", AccionObservacionFactura.MarcarRevisada),
            ConciliacionTestsDatos.Luis);

        Assert.True(revisada!.Revisada);
        Assert.Equal(["MarcaRevisada", "Observacion"], revisada.Historial.Select(h => h.Tipo));
        Assert.Equal("Luis", revisada.Historial[0].NombreUsuario);

        var factura = Assert.Single((await servicio.ConciliarAsync(Hoy.AddDays(-30), Hoy, Ana)).Facturas);
        Assert.Equal(EstadoConciliacion.Parcial, factura.Estado);
        Assert.Equal(2, factura.Observaciones);
        Assert.True(factura.Revisada);
        Assert.Equal("Proveedor subió precio, autorizado por gerencia", factura.UltimaObservacion);

        var quitada = await servicio.AgregarObservacionFacturaAsync(id,
            new NuevaObservacionFacturaDto("Se reabre: llegó otra factura", AccionObservacionFactura.QuitarMarcaRevisada), Ana);
        Assert.False(quitada!.Revisada);
    }

    [Fact]
    public async Task ObservacionesFactura_SinTextoMarcaRepetidaOFacturaInexistente_Rechaza()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var servicio = CrearServicio(db);
        await servicio.RegistrarFacturasAsync([Xml("FE7")]);
        var id = (await db.FacturasProveedor.SingleAsync()).Id;

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.AgregarObservacionFacturaAsync(id, new NuevaObservacionFacturaDto("  "), Ana));
        await Assert.ThrowsAsync<ArgumentException>(() => servicio.AgregarObservacionFacturaAsync(id,
            new NuevaObservacionFacturaDto("x", AccionObservacionFactura.QuitarMarcaRevisada), Ana));
        Assert.Null(await servicio.AgregarObservacionFacturaAsync(9999, new NuevaObservacionFacturaDto("x"), Ana));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.AgregarObservacionFacturaAsync(id,
            new NuevaObservacionFacturaDto("x"), ConciliacionTestsDatos.SinPermiso));
    }

    [Fact]
    public async Task Emisor_SeGuardaYSeMuestraEnFacturasYOcSinFactura()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var servicio = CrearServicio(db);
        await servicio.RegistrarFacturasAsync([Xml("FE1")]);
        await servicio.SincronizarOrdenesAsync([
            Oc(500, 20058, 7500m, "LAURA SOFIA HERNANDEZ CARO"),
            Oc(501, 20059, 1000m, "YAZMIN RENDON RODRIGUEZ")]);

        var conciliacion = await servicio.ConciliarAsync(Hoy.AddDays(-30), Hoy, Ana);

        var factura = Assert.Single(conciliacion.Facturas);
        Assert.Equal(EstadoConciliacion.Cuadra, factura.Estado);
        Assert.Equal(["LAURA SOFIA HERNANDEZ CARO"], factura.EmisoresOrdenCompra!);
        var pendiente = Assert.Single(conciliacion.OrdenesPendientes);
        Assert.Equal("YAZMIN RENDON RODRIGUEZ", pendiente.Emisor);
        Assert.Equal("YAZMIN RENDON RODRIGUEZ", (await servicio.ObtenerObservacionesOrdenAsync(501, Ana))!.Emisor);
    }

    [Fact]
    public async Task Emisor_CambiaEnWO_SeActualizaSinTocarRenglones()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var servicio = CrearServicio(db);
        await servicio.SincronizarOrdenesAsync([Oc(500, 20058, 7500m, "LAURA")]);
        var idRenglon = (await db.OrdenCompraWorldOfficeRenglones.SingleAsync()).Id;

        var r = await servicio.SincronizarOrdenesAsync([Oc(500, 20058, 7500m, "YAZMIN")]);

        Assert.Equal(new SincronizacionOrdenesResultadoDto(0, 1), r);
        Assert.Equal("YAZMIN", (await db.OrdenesCompraWorldOffice.SingleAsync()).Emisor);
        Assert.Equal(idRenglon, (await db.OrdenCompraWorldOfficeRenglones.SingleAsync()).Id);
    }

    [Fact]
    public async Task UsuarioSinRolConciliador_NoVeLaConciliacion()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CrearServicio(db).ConciliarAsync(Hoy.AddDays(-30), Hoy, ConciliacionTestsDatos.SinPermiso));
    }

    [Fact]
    public async Task CargaDian_FacturaDeOtroReceptor_NoEntra()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var xml = LectorFacturaElectronicaTests.Invoice(numero: "FE9").Replace(">900632353<", ">901342748<");

        var r = await CrearServicio(db).CargarArchivosAsync([new ArchivoCargado("FE9.xml", new MemoryStream(Encoding.UTF8.GetBytes(xml)))], Ana);

        Assert.Equal(1, r.OtroReceptor);
        Assert.Equal(0, await db.FacturasProveedor.CountAsync());
    }

    private sealed class TiempoFijo(DateTime ahora) : TimeProvider
    {
        private readonly DateTimeOffset _ahora = new(ahora, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
