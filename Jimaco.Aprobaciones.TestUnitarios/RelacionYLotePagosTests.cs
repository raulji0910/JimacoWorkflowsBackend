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
/// 2026-11-09 — relación de pagos (Gerencia Comercial ratifica varios proveedores de una vez, un
/// solo correo), menú "Pagos" (el pagador paga como lote con UN soporte, rechaza lo que no) y pago
/// extraordinario (giro a un tercero sin factura).
/// </summary>
public class RelacionYLotePagosTests
{
    private sealed class RelojFijo() : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);
    }

    private static readonly DateTime Hoy = new(2026, 11, 9);

    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly Mock<INotificacionService> notificaciones = new();
    private readonly Mock<IAlmacenamientoArchivos> almacenamiento = new();
    private readonly RelojFijo reloj = new();

    private InstanciaDocumentoService Instancias() => new(db, almacenamiento.Object, notificaciones.Object, reloj);
    private CuentasPorPagarService CuentasPorPagar() => new(db, Instancias(), notificaciones.Object, reloj);
    private PagosService Pagos() => new(db, Instancias(), almacenamiento.Object, notificaciones.Object, reloj);

    private int comercialId;
    private int pagadorId;

    public RelacionYLotePagosTests()
    {
        almacenamiento.Setup(a => a.GuardarAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("soportes/lote.pdf");
    }

    private async Task SembrarAsync()
    {
        var rolComercial = new Rol { Nombre = "Gerencia Comercial", PuedeVerCuentasPorPagar = true };
        var rolPagador = new Rol { Nombre = "Gerencia General", PuedePagarCuentas = true };
        var comercial = new Usuario { Nombre = "Comercial", Email = "comercial@test", PasswordHash = "x" };
        var pagador = new Usuario { Nombre = "Gerente General", Email = "gerencia@test", PasswordHash = "x" };
        db.AddRange(rolComercial, rolPagador, comercial, pagador);
        await db.SaveChangesAsync();
        db.UsuarioRoles.AddRange(
            new UsuarioRol { UsuarioId = comercial.Id, RolId = rolComercial.Id },
            new UsuarioRol { UsuarioId = pagador.Id, RolId = rolPagador.Id });
        comercialId = comercial.Id;
        pagadorId = pagador.Id;

        var tipo = new TipoDocumento { Nombre = "Pago a Proveedor", PrefijoWorldOffice = CuentasPorPagarService.PrefijoPagoProveedor };
        db.TiposDocumento.Add(tipo);
        await db.SaveChangesAsync();
        var paso = new PasoFlujo { Nombre = "Gerencia General", Orden = 1, PermiteRechazar = true, RequiereSoportePagoAlAprobar = true };
        db.DefinicionesFlujo.Add(new DefinicionFlujo { Nombre = "Pago a proveedor", TipoDocumentoId = tipo.Id, Activo = true, Pasos = [paso] });
        await db.SaveChangesAsync();
        db.PasoFlujoRoles.Add(new PasoFlujoRol { PasoFlujoId = paso.Id, RolId = rolPagador.Id });
        db.TercerosWO.Add(new TerceroWO { IdTerceroWO = 555, Identificacion = "1020", Nombre = "JUAN PEREZ" });
        db.TiposGiro.Add(new TipoGiro { Id = 1, Nombre = "Anticipo a proveedores", CodigoCuenta = "133005", EsAnticipo = true });
        await db.SaveChangesAsync();

        await CuentasPorPagar().GuardarFotoAsync(new FotoCuentasPorPagarDto(
            [Factura(101, "900", -1000), Factura(102, "900", -500), Factura(201, "800", -700)],
            [new AnticipoProveedorInputDto("900", "PROVEEDOR 900", 1, 300, 501, "CEJ", "15000", Hoy.AddDays(-40), "ANTICIPO OC 1")],
            [new CuentaBancoInputDto(13650, "11100502", "BANCOLOMBIA", true)]));
    }

    private static PartidaCuentaPorPagarInputDto Factura(int id, string nit, decimal saldoWO) =>
        new(id, nit, $"PROVEEDOR {nit}", "Factura de Compra", "FCJ", id.ToString(), $"FE{id}", Hoy.AddDays(-40), Hoy.AddDays(-10), saldoWO, "220501");

    private async Task<RelacionCreadaDto> RatificarDosProveedoresAsync()
    {
        var cxp = CuentasPorPagar();
        await cxp.GuardarBorradorProveedorAsync("900", new GuardarBorradorProveedorDto([101, 102], [501]), comercialId);
        await cxp.GuardarBorradorProveedorAsync("800", new GuardarBorradorProveedorDto([201], []), comercialId);
        return await cxp.RatificarRelacionAsync(new RatificarRelacionDto("Pagos de la semana"), comercialId);
    }

    private int CorreosConAsunto(string fragmento) =>
        notificaciones.Invocations.Count(i => i.Method.Name == nameof(INotificacionService.NotificarHtmlAsync)
                                              && ((string)i.Arguments[2]).Contains(fragmento));

    [Fact]
    public async Task Borrador_MuestraNetoConAnticipo_YSeQuitaElProveedorSinFacturas()
    {
        await SembrarAsync();
        var cxp = CuentasPorPagar();

        var borrador = await cxp.GuardarBorradorProveedorAsync("900", new GuardarBorradorProveedorDto([101, 102], [501]), comercialId);
        var prov = Assert.Single(borrador.Proveedores);
        Assert.Equal(1500, prov.TotalFacturas);
        Assert.Equal(300, prov.TotalAnticipos);
        Assert.Equal(1200, prov.Neto);
        Assert.Null(prov.Problema);

        borrador = await cxp.GuardarBorradorProveedorAsync("900", new GuardarBorradorProveedorDto([], []), comercialId);
        Assert.Empty(borrador.Proveedores);
    }

    [Fact]
    public async Task Ratificar_UnaOrdenPorProveedor_YUnSoloCorreoAlPagador()
    {
        await SembrarAsync();

        var relacion = await RatificarDosProveedoresAsync();

        Assert.Equal(1, relacion.Numero);
        Assert.Equal(2, relacion.Ordenes);
        Assert.Equal(1200 + 700, relacion.Total);

        var pagos = await db.PagosProveedor.ToListAsync();
        Assert.Equal(2, pagos.Count);
        Assert.All(pagos, p => Assert.NotNull(p.RelacionPagoId));
        Assert.Empty(await db.BorradoresPagoProveedor.ToListAsync());

        // Un solo correo (al único pagador) y ningún aviso de "paso pendiente" por orden.
        Assert.Equal(1, CorreosConAsunto("Relación de pagos 1"));
        notificaciones.Verify(n => n.NotificarPasoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        // Las facturas quedaron en trámite: no se pueden volver a meter en otra relación.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CuentasPorPagar().GuardarBorradorProveedorAsync("800", new GuardarBorradorProveedorDto([201], []), comercialId));
    }

    [Fact]
    public async Task OrdenesDePago_NoAparecenEnPendientesNiMisDocumentos()
    {
        await SembrarAsync();
        await RatificarDosProveedoresAsync();

        Assert.Empty(await Instancias().ListarPendientesAsync(pagadorId));
        Assert.Empty(await Instancias().ListarMisDocumentosAsync(comercialId));
        Assert.Equal(2, (await Pagos().ListarPendientesAsync(pagadorId)).Count);
    }

    [Fact]
    public async Task PagarLote_UnSoloSoporte_CadaOrdenQuedaParaSuCE()
    {
        await SembrarAsync();
        await RatificarDosProveedoresAsync();
        var ids = (await Pagos().ListarPendientesAsync(pagadorId)).Select(o => o.InstanciaDocumentoId).ToList();

        var lote = await Pagos().PagarLoteAsync(ids, 13650, Hoy, "pago.pdf", "application/pdf", 10, new MemoryStream([1, 2, 3]), pagadorId);

        Assert.Equal(1, lote.Numero);
        Assert.Equal(1900, lote.Total);
        almacenamiento.Verify(a => a.GuardarAsync("pago.pdf", It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);

        var pagos = await db.PagosProveedor.ToListAsync();
        Assert.All(pagos, p =>
        {
            Assert.True(p.PendienteCreacionCE);
            Assert.Equal(13650, p.IdCuentaBancoWO);
            Assert.Equal(Hoy, p.FechaPago);
            Assert.NotNull(p.LotePagoId);
        });
        var adjuntos = await db.Adjuntos.ToListAsync();
        Assert.Equal(2, adjuntos.Count);
        Assert.All(adjuntos, a => Assert.Equal("soportes/lote.pdf", a.RutaArchivo));
        Assert.Empty(await Pagos().ListarPendientesAsync(pagadorId));

        // La cola del CE recibe las dos órdenes, cada una con su tercero.
        var cola = await CuentasPorPagar().ListarPendientesCreacionCEAsync();
        Assert.Equal(2, cola.Count);
        Assert.Contains(cola, c => c.Identificacion == "900" && c.Neto == 1200 && c.Anticipos.Count == 1);
    }

    [Fact]
    public async Task PagarLote_SinSoporteONiBanco_NoDejaPagar()
    {
        await SembrarAsync();
        await RatificarDosProveedoresAsync();
        var ids = (await Pagos().ListarPendientesAsync(pagadorId)).Select(o => o.InstanciaDocumentoId).ToList();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Pagos().PagarLoteAsync(ids, 13650, Hoy, null, null, 0, null, pagadorId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Pagos().PagarLoteAsync(ids, null, Hoy, "pago.pdf", "application/pdf", 3, new MemoryStream([1]), pagadorId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Pagos().PagarLoteAsync(ids, 13650, Hoy, "pago.pdf", "application/pdf", 3, new MemoryStream([1]), comercialId));
        Assert.Empty(await db.LotesPago.ToListAsync());
    }

    [Fact]
    public async Task LoteTerminado_AvisaUnaSolaVez_CuandoTodosLosCEQuedan()
    {
        await SembrarAsync();
        await RatificarDosProveedoresAsync();
        var ids = (await Pagos().ListarPendientesAsync(pagadorId)).Select(o => o.InstanciaDocumentoId).OrderBy(i => i).ToList();
        await Pagos().PagarLoteAsync(ids, 13650, Hoy, "pago.pdf", "application/pdf", 10, new MemoryStream([1]), pagadorId);

        async Task CrearCEAsync(int id, int numero)
        {
            await CuentasPorPagar().ConfirmarCECreadoAsync(id, new CECreadoDto("CEJ-2", numero, 9000 + numero));
            await Instancias().AdjuntarComprobanteEgresoAsync(id, new ComprobanteEgresoEncontradoDto(
                $"CEJ-2 {numero}", Hoy, 1, null, null, null, null, null, null, null, []));
        }

        await CrearCEAsync(ids[0], 30);
        Assert.Equal(0, CorreosConAsunto("Lote de pago 1"));

        await CrearCEAsync(ids[1], 31);
        // Un correo a la comercial (creó las órdenes) y uno al pagador — no uno por orden.
        Assert.Equal(2, CorreosConAsunto("Lote de pago 1"));
        Assert.True((await db.LotesPago.SingleAsync()).AvisoTerminadoEnviado);

        // Ni un aviso de "aprobado en todos los pasos" por orden.
        notificaciones.Verify(n => n.NotificarUsuarioAsync(It.IsAny<int>(), It.IsAny<int>(),
            It.Is<string>(s => s.Contains("todos los pasos")), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rechazar_LiberaLasFacturasParaGerenciaComercial()
    {
        await SembrarAsync();
        await RatificarDosProveedoresAsync();
        var orden = (await Pagos().ListarPendientesAsync(pagadorId)).Single(o => o.Identificacion == "800");

        await Pagos().RechazarAsync(orden.InstanciaDocumentoId, "Este mes no", pagadorId);

        var detalle = await CuentasPorPagar().ObtenerProveedorAsync("800", comercialId);
        Assert.Equal(EstadoPartidaCuentaPorPagar.Disponible, detalle.Partidas.Single().Estado);
        Assert.Single(await Pagos().ListarPendientesAsync(pagadorId));
    }

    [Fact]
    public async Task Rechazar_AvisaAGerenciaComercialConElMotivo_YSeVeEnElSeguimiento()
    {
        await SembrarAsync();
        await RatificarDosProveedoresAsync();
        var orden = (await Pagos().ListarPendientesAsync(pagadorId)).Single(o => o.Identificacion == "800");
        notificaciones.Invocations.Clear();

        await Pagos().RechazarAsync(orden.InstanciaDocumentoId, "Este mes no hay caja", pagadorId);

        // Un solo aviso, el detallado, a quien creó la relación (no el genérico "Tu documento fue rechazado")
        notificaciones.Verify(n => n.NotificarHtmlAsync(orden.InstanciaDocumentoId, comercialId,
            It.Is<string>(s => s.Contains("Pago rechazado")), It.Is<string>(c => c.Contains("Este mes no hay caja") && c.Contains("800")),
            "/cuentas-por-pagar/seguimiento", It.IsAny<CancellationToken>()), Times.Once);
        notificaciones.Verify(n => n.NotificarUsuarioAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        var pagina = await CuentasPorPagar().ObtenerSeguimientoAsync(new FiltroPagosDto(), comercialId);
        Assert.Equal(1, pagina.PorPagar.Cantidad);
        Assert.Equal(1, pagina.Rechazados.Cantidad);
        Assert.Equal(orden.Neto, pagina.Rechazados.Monto);
        var seguimiento = Assert.Single(pagina.Grupos).Ordenes; // la relación viene completa, en un solo grupo
        var rechazada = seguimiento.Single(s => s.InstanciaDocumentoId == orden.InstanciaDocumentoId);
        Assert.Equal("Rechazado", rechazada.Estado);
        Assert.Equal("Este mes no hay caja", rechazada.MotivoRechazo);
        Assert.Equal("Gerente General", rechazada.RechazadoPor);
        Assert.Equal("PorPagar", seguimiento.Single(s => s.InstanciaDocumentoId != orden.InstanciaDocumentoId).Estado);

        // Filtro por estado: el grupo trae solo las órdenes de ese estado
        var soloRechazadas = await CuentasPorPagar().ObtenerSeguimientoAsync(new FiltroPagosDto(Estado: "Rechazado"), comercialId);
        Assert.Equal(orden.InstanciaDocumentoId, Assert.Single(Assert.Single(soloRechazadas.Grupos).Ordenes).InstanciaDocumentoId);

        // El pagador ve lo que él rechazó en su propio menú
        var rechazadas = await Pagos().ListarRechazadosAsync(new FiltroPagosDto(), pagadorId);
        Assert.Equal(1, rechazadas.Total);
        Assert.Equal("Este mes no hay caja", Assert.Single(rechazadas.Items).MotivoRechazo);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Pagos().ListarRechazadosAsync(new FiltroPagosDto(), comercialId));
    }

    [Fact]
    public async Task Seguimiento_MuestraElLotePagado_YExigeElPermisoDeCuentasPorPagar()
    {
        await SembrarAsync();
        await RatificarDosProveedoresAsync();
        var ordenes = await Pagos().ListarPendientesAsync(pagadorId);
        await Pagos().PagarLoteAsync(ordenes.Select(o => o.InstanciaDocumentoId).ToList(), 13650, Hoy,
            "soporte.pdf", "application/pdf", 10, new MemoryStream([1]), pagadorId);

        var pagina = await CuentasPorPagar().ObtenerSeguimientoAsync(new FiltroPagosDto(Estado: "Pagado"), comercialId);
        var seguimiento = pagina.Grupos.SelectMany(g => g.Ordenes).ToList();
        Assert.Equal(2, seguimiento.Count);
        Assert.All(seguimiento, s => Assert.Equal("Pagado", s.Estado));
        Assert.All(seguimiento, s => Assert.Equal(1, s.NumeroLote));
        Assert.All(seguimiento, s => Assert.Empty(s.Lineas)); // las líneas se piden al abrir
        Assert.NotEmpty(await CuentasPorPagar().ObtenerLineasSeguimientoAsync(seguimiento[0].InstanciaDocumentoId, comercialId));

        // Búsqueda por NIT, página fuera de rango y lotes paginados
        var porNit = await CuentasPorPagar().ObtenerSeguimientoAsync(new FiltroPagosDto(Buscar: "800"), comercialId);
        Assert.Equal("800", Assert.Single(Assert.Single(porNit.Grupos).Ordenes).Identificacion);
        var vacia = await CuentasPorPagar().ObtenerSeguimientoAsync(new FiltroPagosDto(Pagina: 2), comercialId);
        Assert.Empty(vacia.Grupos);
        Assert.Equal(1, vacia.Total);
        var lotes = await Pagos().ListarLotesAsync(new FiltroPagosDto(), pagadorId);
        Assert.Equal(1, lotes.Total);
        Assert.Empty((await Pagos().ListarLotesAsync(new FiltroPagosDto(Desde: Hoy.AddDays(1)), pagadorId)).Items);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CuentasPorPagar().ObtenerSeguimientoAsync(new FiltroPagosDto(), pagadorId));
    }

    [Fact]
    public async Task PagoExtraordinario_UnCorreo_YVaALaColaDelCEComoGiro()
    {
        await SembrarAsync();

        var id = await CuentasPorPagar().CrearPagoExtraordinarioAsync(new CrearPagoExtraordinarioDto(555, 250_000, 1, "Viáticos obra Mesetas"), comercialId);

        Assert.Equal(1, CorreosConAsunto("Pago extraordinario a JUAN PEREZ"));
        var pendiente = Assert.Single(await Pagos().ListarPendientesAsync(pagadorId));
        Assert.True(pendiente.EsExtraordinario);
        Assert.Equal(250_000, pendiente.Neto);

        await Pagos().PagarLoteAsync([id], 13650, Hoy, "giro.pdf", "application/pdf", 5, new MemoryStream([1]), pagadorId);

        var ce = Assert.Single(await CuentasPorPagar().ListarPendientesCreacionCEAsync());
        Assert.Empty(ce.Facturas);
        Assert.Equal(250_000, ce.Neto);
        Assert.NotNull(ce.Giro);
        Assert.Equal(555, ce.Giro!.IdTerceroWO);
        Assert.Equal("133005", ce.Giro.CodigoCuenta);
        Assert.True(ce.Giro.EsAnticipo);
        Assert.Equal("Viáticos obra Mesetas", ce.Giro.Concepto);
    }
}
