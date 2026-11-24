using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>
/// 2026-11-24: las facturas guardadas con la lectura vieja (subtotal = base gravable, 0 en las sin
/// IVA) se corrigen releyendo su XML al arrancar la Api, una sola vez.
/// </summary>
public class ConciliacionRecalculoTotalesTests
{
    private static ConciliacionService CrearServicio(Modelo.AppDbContext db) => new(db, new TiempoFijo(new DateTime(2026, 11, 24)));

    private static FacturaXmlEntradaDto SinIva(string numero)
        => new(LectorFacturaElectronicaTests.Invoice(numero: numero, lineExtension: 119000m, taxExclusive: 0m, iva: 0m,
            payable: 119000m, descuentoGlobal: null), $"{numero}.xml", "asunto", null);

    private sealed class TiempoFijo(DateTime ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(ahora, TimeSpan.Zero);
    }

    [Fact]
    public async Task FacturaGuardadaConLaLecturaVieja_SeCorrigeUnaSolaVez()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var servicio = CrearServicio(db);
        await servicio.RegistrarFacturasAsync([SinIva("FE2374")]);
        // Así quedó guardada antes del arreglo: subtotal = TaxExclusiveAmount = 0.
        var factura = await db.FacturasProveedor.SingleAsync();
        factura.Subtotal = 0m;
        db.SincronizacionConciliacion.Add(new SincronizacionConciliacion { Id = 1, UltimaSincronizacionOrdenes = new DateTime(2026, 11, 24) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var cambiadas = await servicio.RecalcularTotalesSiHaceFaltaAsync();

        Assert.Equal(1, cambiadas);
        var corregida = await db.FacturasProveedor.AsNoTracking().SingleAsync();
        Assert.Equal(119000m, corregida.Subtotal);
        Assert.Equal(119000m, corregida.Total);
        var marca = await db.SincronizacionConciliacion.AsNoTracking().SingleAsync();
        Assert.Equal(ConciliacionService.VersionLecturaXml, marca.VersionLecturaXml);
        Assert.NotNull(marca.UltimaSincronizacionOrdenes);

        // Segundo arranque: ya está al día, no relee nada.
        Assert.Null(await servicio.RecalcularTotalesSiHaceFaltaAsync());
    }

    [Fact]
    public async Task SinFilaDeSincronizacion_LaCreaConLaVersion()
    {
        using var db = ConciliacionTestsDatos.CrearContexto();
        var servicio = CrearServicio(db);
        await servicio.RegistrarFacturasAsync([SinIva("FE1")]);

        var cambiadas = await servicio.RecalcularTotalesSiHaceFaltaAsync();

        Assert.Equal(0, cambiadas);
        Assert.Equal(119000m, (await db.FacturasProveedor.SingleAsync()).Subtotal);
        Assert.Equal(ConciliacionService.VersionLecturaXml, (await db.SincronizacionConciliacion.SingleAsync()).VersionLecturaXml);
    }
}
