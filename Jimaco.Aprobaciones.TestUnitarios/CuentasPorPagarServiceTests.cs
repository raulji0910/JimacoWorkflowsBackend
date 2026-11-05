using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>
/// Módulo de Cuentas por Pagar: la foto de WO (signo invertido, neto con anticipos), y que una
/// factura no se pueda mandar a pagar dos veces mientras su pago siga vivo.
/// </summary>
public class CuentasPorPagarServiceTests
{
    // 2026-11-05 15:00 UTC = 10:00 en Colombia — "hoy" para los vencimientos es el 5 de noviembre.
    private sealed class RelojFijo() : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime Hoy = new(2026, 11, 5);

    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private CuentasPorPagarService CrearServicio()
    {
        var reloj = new RelojFijo();
        var instancias = new InstanciaDocumentoService(db, Mock.Of<IAlmacenamientoArchivos>(), Mock.Of<INotificacionService>(), reloj);
        return new CuentasPorPagarService(db, instancias, reloj);
    }

    /// <summary>Gerente comercial con el permiso + tipo "Pago a Proveedor" con flujo Contabilidad → Gerencia. Devuelve el Id del gerente.</summary>
    private async Task<int> SembrarAsync()
    {
        var rolGerente = new Rol { Nombre = "Gerencia Comercial", PuedeVerCuentasPorPagar = true };
        var rolContable = new Rol { Nombre = "Area Contable" };
        var gerente = new Usuario { Nombre = "Gerente", Email = "gerente@test", PasswordHash = "x" };
        db.AddRange(rolGerente, rolContable, gerente);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = gerente.Id, RolId = rolGerente.Id });

        var tipo = new TipoDocumento { Nombre = "Pago a Proveedor", PrefijoWorldOffice = CuentasPorPagarService.PrefijoPagoProveedor };
        db.TiposDocumento.Add(tipo);
        await db.SaveChangesAsync();

        var paso1 = new PasoFlujo { Nombre = "Area Contable", Orden = 1, PermiteRechazar = true };
        var paso2 = new PasoFlujo { Nombre = "Gerencia General", Orden = 2 };
        db.DefinicionesFlujo.Add(new DefinicionFlujo { Nombre = "Pago a proveedor", TipoDocumentoId = tipo.Id, Activo = true, Pasos = [paso1, paso2] });
        await db.SaveChangesAsync();
        db.PasoFlujoRoles.Add(new PasoFlujoRol { PasoFlujoId = paso1.Id, RolId = rolContable.Id });
        await db.SaveChangesAsync();

        return gerente.Id;
    }

    private static PartidaCuentaPorPagarInputDto Factura(int id, string nit, decimal saldoWO, int diasVencida, string numeroExterno = "FE1") =>
        new(id, nit, $"PROVEEDOR {nit}", "Factura de Compra", "FCJ", id.ToString(), numeroExterno,
            Hoy.AddDays(-diasVencida - 30), Hoy.AddDays(-diasVencida), saldoWO, "220501");

    [Fact]
    public async Task GuardarFoto_InvierteElSignoDeWO_YReemplazaLaFotoAnterior()
    {
        var servicio = CrearServicio();
        await servicio.GuardarFotoAsync(new([Factura(1, "900", -1000, 0), Factura(2, "900", -500, 0)], []));
        await servicio.GuardarFotoAsync(new([Factura(3, "800", -700, 0), Factura(4, "800", 0, 0)], [new("800", "PROVEEDOR 800", 2, 100)]));

        var partidas = await db.PartidasCuentaPorPagar.ToListAsync();
        var partida = Assert.Single(partidas);
        Assert.Equal(3, partida.IdDocumentoDetalleWO);
        Assert.Equal(700, partida.Pendiente);
        Assert.Single(await db.AnticiposProveedor.ToListAsync());

        var corte = Assert.Single(await db.SincronizacionesCuentasPorPagar.ToListAsync());
        Assert.Equal(new DateTime(2026, 11, 5, 15, 0, 0), corte.FechaCorte);
        Assert.Equal(1, corte.Partidas);
    }

    [Fact]
    public async Task Resumen_NetoDescuentaSaldoAFavorYAnticipos_YClasificaPorAntiguedad()
    {
        var gerenteId = await SembrarAsync();
        var servicio = CrearServicio();
        await servicio.GuardarFotoAsync(new(
            [
                Factura(1, "900", -1000, diasVencida: 10),
                Factura(2, "900", -2000, diasVencida: 45),
                Factura(3, "900", -3000, diasVencida: -5),   // vence en 5 días
                Factura(4, "900", 400, diasVencida: 0),      // pago sin aplicar (saldo a favor)
                Factura(5, "800", -500, diasVencida: 90)
            ],
            [new("900", "PROVEEDOR 900", 3, 1500), new("700", "SOLO ANTICIPO", 1, 9000)]));

        var resumen = await servicio.ObtenerResumenAsync(gerenteId);

        Assert.Equal(6500, resumen.TotalPendiente);
        Assert.Equal(3500, resumen.TotalVencido);
        Assert.Equal(3000, resumen.VenceProximos7Dias);
        Assert.Equal(3000, resumen.AlDia);
        Assert.Equal(1000, resumen.Vencido1a30);
        Assert.Equal(2000, resumen.Vencido31a60);
        Assert.Equal(500, resumen.VencidoMas60);
        Assert.Equal(10500, resumen.TotalAnticipos);

        var p900 = resumen.Proveedores.Single(p => p.Identificacion == "900");
        Assert.Equal(3, p900.Facturas);
        Assert.Equal(6000, p900.Pendiente);
        Assert.Equal(400, p900.SaldoAFavor);
        Assert.Equal(1500, p900.Anticipos);
        Assert.Equal(4100, p900.Neto);
        Assert.Equal(45, p900.DiasMaximoVencido);

        var soloAnticipo = resumen.Proveedores.Single(p => p.Identificacion == "700");
        Assert.Equal("SOLO ANTICIPO", soloAnticipo.Nombre);
        Assert.Equal(-9000, soloAnticipo.Neto);
    }

    [Fact]
    public async Task CrearPago_CreaElDocumentoConUnRenglonPorFactura_YLasDejaEnTramite()
    {
        var gerenteId = await SembrarAsync();
        var servicio = CrearServicio();
        await servicio.GuardarFotoAsync(new([Factura(1, "900", -1000, 10, "FE10"), Factura(2, "900", -2500, 0, "FE11"), Factura(3, "900", -300, 0)], []));

        var id = await servicio.CrearPagoAsync(new("900", [1, 2], "Pagar esta semana"), gerenteId);

        var documento = await db.InstanciasDocumento.Include(i => i.Renglones).Include(i => i.FacturasPagoProveedor).SingleAsync(i => i.Id == id);
        Assert.Equal(3500, documento.Valor);
        Assert.Equal("1", documento.NumeroReferencia);
        Assert.Equal("PROVEEDOR 900", documento.Proveedor);
        Assert.Equal(gerenteId, documento.CreadoPorUsuarioId);
        Assert.Equal(["FCJ 1", "FCJ 2"], documento.Renglones.OrderBy(r => r.Orden).Select(r => r.Codigo));
        Assert.Contains("FE10", documento.Renglones.OrderBy(r => r.Orden).First().Descripcion);
        Assert.Equal(2, documento.FacturasPagoProveedor.Count);

        var detalle = await servicio.ObtenerProveedorAsync("900", gerenteId);
        Assert.Equal(EstadoPartidaCuentaPorPagar.EnTramite, detalle.Partidas.Single(p => p.IdDocumentoDetalleWO == 1).Estado);
        Assert.Equal(EstadoPartidaCuentaPorPagar.Disponible, detalle.Partidas.Single(p => p.IdDocumentoDetalleWO == 3).Estado);
        Assert.Equal(3500, detalle.Proveedor.EnTramite);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CrearPagoAsync(new("900", [2, 3], null), gerenteId));
        Assert.Contains("FCJ 2", error.Message);
    }

    [Fact]
    public async Task PagoRechazado_LiberaLasFacturas_YCompletado_LasBloqueaHastaQueWOCambieElSaldo()
    {
        var gerenteId = await SembrarAsync();
        var servicio = CrearServicio();
        await servicio.GuardarFotoAsync(new([Factura(1, "900", -1000, 0), Factura(2, "900", -2000, 0)], []));

        var rechazado = await servicio.CrearPagoAsync(new("900", [1], null), gerenteId);
        var completado = await servicio.CrearPagoAsync(new("900", [2], null), gerenteId);
        (await db.InstanciasDocumento.FindAsync(rechazado))!.Estado = EstadoInstanciaDocumento.Rechazado;
        (await db.InstanciasDocumento.FindAsync(completado))!.Estado = EstadoInstanciaDocumento.Completado;
        await db.SaveChangesAsync();

        var detalle = await servicio.ObtenerProveedorAsync("900", gerenteId);
        Assert.Equal(EstadoPartidaCuentaPorPagar.Disponible, detalle.Partidas.Single(p => p.IdDocumentoDetalleWO == 1).Estado);
        Assert.Equal(EstadoPartidaCuentaPorPagar.PagadaPendienteCruce, detalle.Partidas.Single(p => p.IdDocumentoDetalleWO == 2).Estado);

        // WO cruzó un abono parcial: queda un saldo distinto, que se puede volver a pagar.
        await servicio.GuardarFotoAsync(new([Factura(1, "900", -1000, 0), Factura(2, "900", -800, 0)], []));
        detalle = await servicio.ObtenerProveedorAsync("900", gerenteId);
        Assert.Equal(EstadoPartidaCuentaPorPagar.Disponible, detalle.Partidas.Single(p => p.IdDocumentoDetalleWO == 2).Estado);
    }

    [Fact]
    public async Task CrearPago_FacturaQueYaNoEstaEnLaFoto_NoSePaga()
    {
        var gerenteId = await SembrarAsync();
        var servicio = CrearServicio();
        await servicio.GuardarFotoAsync(new([Factura(1, "900", -1000, 0)], []));

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CrearPagoAsync(new("900", [1, 99], null), gerenteId));
        Assert.Empty(await db.InstanciasDocumento.ToListAsync());
    }

    [Fact]
    public async Task SinElPermiso_NoVeNadaNiPuedePagar()
    {
        await SembrarAsync();
        var otro = new Usuario { Nombre = "Otro", Email = "otro@test", PasswordHash = "x" };
        db.Usuarios.Add(otro);
        await db.SaveChangesAsync();
        var servicio = CrearServicio();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.ObtenerResumenAsync(otro.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.CrearPagoAsync(new("900", [1], null), otro.Id));
    }
}
