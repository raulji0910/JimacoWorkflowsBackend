using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>
/// 2026-11-19: las OC a crédito de FERREALIADOS se aprueban solas al llegar (sin aprobación
/// comercial) pero ya no se completan: pasan a "Recogida Transporte" (Logística), y solo la
/// Asistente de Gerencia las puede devolver — eso las deja "en espera de aprobación" en el WO de
/// Ferrealiados. Ver DefinicionFlujo.AprobacionAutomaticaAlLlegar.
/// </summary>
public class FerrealiadosCreditoLogisticaTests
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<INotificacionService> notificaciones = new();

    private InstanciaDocumentoService Documentos() =>
        new(db, Mock.Of<IAlmacenamientoArchivos>(), notificaciones.Object, TimeProvider.System);

    private HojaAlistamientoService Hojas() =>
        new(db, Mock.Of<IConfiguration>(), TimeProvider.System, notificaciones.Object);

    private record Semilla(int TipoFerrealiados, int TipoJimaco, int Sincronizador, int Logistica, int Asistente, int PasoRecogida);

    private async Task<Semilla> SembrarAsync()
    {
        var transporte = new Rol { Nombre = "Area de Transporte", Activo = true, PuedeGestionarAlistamientoFerrealiados = true };
        var asistente = new Rol { Nombre = "Asistente de Gerencia", Activo = true, PuedeDevolverCualquierDocumento = true };
        var comercial = new Rol { Nombre = "Gerente Comercial", Activo = true };
        var uSinc = new Usuario { Nombre = "Sincronizador Ferrealiados", Email = "sinc@test", PasswordHash = "x" };
        var uLog = new Usuario { Nombre = "Logistica", Email = "log@test", PasswordHash = "x" };
        var uAsis = new Usuario { Nombre = "Asistente de Gerencia", Email = "asis@test", PasswordHash = "x" };
        var tipoF = new TipoDocumento { Nombre = "Orden de Compra Ferrealiados", PrefijoWorldOffice = "OC", Empresa = Empresa.Ferrealiados };
        var tipoJ = new TipoDocumento { Nombre = "Orden de Compra", PrefijoWorldOffice = "OC" };
        db.AddRange(transporte, asistente, comercial, uSinc, uLog, uAsis, tipoF, tipoJ);
        await db.SaveChangesAsync();
        db.UsuarioRoles.AddRange(
            new UsuarioRol { UsuarioId = uLog.Id, RolId = transporte.Id },
            new UsuarioRol { UsuarioId = uAsis.Id, RolId = asistente.Id });

        // Igual que quedará en producción: crédito con aprobación automática + un paso de
        // Logística que NO permite devolver ni rechazar.
        var recogida = new PasoFlujo { Nombre = "Recogida Transporte", Orden = 1, PermiteDevolver = false, PermiteRechazar = false, PermiteDevolverAlEmisor = false };
        var comercialJ = new PasoFlujo { Nombre = "Aprobacion comercial", Orden = 1, PermiteDevolver = true, PermiteDevolverAlEmisor = true };
        db.DefinicionesFlujo.AddRange(
            new DefinicionFlujo { Nombre = "OC Ferrealiados Crédito", TipoDocumentoId = tipoF.Id, Activo = true, FormaDePagoRequerida = "Credito", AprobacionAutomaticaAlLlegar = true, Pasos = [recogida] },
            new DefinicionFlujo { Nombre = "OC Jimaco Crédito", TipoDocumentoId = tipoJ.Id, Activo = true, FormaDePagoRequerida = "Credito", Pasos = [comercialJ] });
        await db.SaveChangesAsync();
        db.PasoFlujoRoles.AddRange(
            new PasoFlujoRol { PasoFlujoId = recogida.Id, RolId = transporte.Id },
            new PasoFlujoRol { PasoFlujoId = comercialJ.Id, RolId = comercial.Id });
        await db.SaveChangesAsync();
        return new(tipoF.Id, tipoJ.Id, uSinc.Id, uLog.Id, uAsis.Id, recogida.Id);
    }

    private static CrearInstanciaDocumentoDto Oc(int tipoId, int asiento, string cotizacion = "897") => new(
        tipoId, asiento.ToString(), "FERRETERIA SERGUS S.A.S.", 195_591m, null,
        new Dictionary<string, string> { ["formaDePago"] = "Credito" },
        IdAsientoContableOrigen: asiento, PrefijoOrigen: "OC",
        DocumentosVinculados: [new DocumentoVinculadoInputDto(
            "COT", cotizacion, new DateTime(2026, 11, 12), new DateTime(2026, 12, 27), "CABLEMOVIL S.A.S.", "900", null, null, null,
            "KIMBERLY CAROLINA OCHOA BONILLA", "Credito", "BODEGA",
            [new RenglonDocumentoVinculadoInputDto("COD-4239", "PIEDRA CALIZA", 550, "Kg", 100, 0.19m)])],
        SolicitanteWO: "CAROLINA OCHOA");

    [Fact]
    public async Task Al_llegar_queda_aprobada_en_WO_con_su_hoja_liberada_pero_en_proceso_en_Recogida_Transporte()
    {
        var s = await SembrarAsync();

        var oc = await Documentos().CrearAsync(Oc(s.TipoFerrealiados, 1503), s.Sincronizador);

        Assert.Equal(EstadoInstanciaDocumento.EnProceso, oc.Estado);
        var instancia = await db.InstanciasDocumento.SingleAsync();
        Assert.Equal(s.PasoRecogida, instancia.PasoActualId);
        Assert.True(instancia.PendienteEscrituraWO);
        Assert.Contains(await db.HistorialAcciones.ToListAsync(), h => h.Accion == TipoAccion.AprobadoAutomatico);
        // Logística recibe el aviso de su paso, y su Hoja ya está disponible.
        notificaciones.Verify(n => n.NotificarPasoAsync(instancia.Id, s.PasoRecogida, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(await Hojas().ListarAsync(s.Logistica, empresa: Empresa.Ferrealiados));

        var pendiente = Assert.Single(await Documentos().ListarPendientesEscrituraWOAsync(Empresa.Ferrealiados));
        Assert.Equal(ResultadoPrimerPaso.Aprobado, pendiente.Resultado);
    }

    [Fact]
    public async Task Cuando_Logistica_aprueba_se_completa_sin_volver_a_escribir_en_WO()
    {
        var s = await SembrarAsync();
        var docs = Documentos();
        var oc = await docs.CrearAsync(Oc(s.TipoFerrealiados, 1503), s.Sincronizador);
        await docs.ConfirmarEscrituraWOAsync(oc.Id); // el Sincronizador ya escribió la aprobación automática

        var final = await docs.EjecutarAccionAsync(oc.Id, s.Logistica, new EjecutarAccionDto(TipoAccion.Aprobado, null));

        Assert.Equal(EstadoInstanciaDocumento.Completado, final.Estado);
        Assert.False((await db.InstanciasDocumento.SingleAsync()).PendienteEscrituraWO);
        Assert.Empty(await docs.ListarPendientesEscrituraWOAsync(Empresa.Ferrealiados));
    }

    [Fact]
    public async Task Logistica_no_puede_devolver_desde_su_paso()
    {
        var s = await SembrarAsync();
        var docs = Documentos();
        var oc = await docs.CrearAsync(Oc(s.TipoFerrealiados, 1503), s.Sincronizador);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            docs.EjecutarAccionAsync(oc.Id, s.Logistica, new EjecutarAccionDto(TipoAccion.Devuelto, "falta algo")));
    }

    [Fact]
    public async Task La_Asistente_de_Gerencia_la_devuelve_y_queda_en_espera_solo_en_el_WO_de_Ferrealiados()
    {
        var s = await SembrarAsync();
        var docs = Documentos();
        var oc = await docs.CrearAsync(Oc(s.TipoFerrealiados, 1503), s.Sincronizador);
        await docs.ConfirmarEscrituraWOAsync(oc.Id);
        // Una OC de Jimaco con el mismo IdAsientoContable no se toca.
        await docs.CrearAsync(Oc(s.TipoJimaco, 1503), s.Sincronizador);

        var encontradas = await docs.BuscarPorNumeroReferenciaAsync("1503", s.Asistente);
        Assert.Equal(2, encontradas.Count);

        var devuelta = await docs.DevolverForzadoAsync(oc.Id, s.Asistente, new DevolverForzadoDto("Proveedor equivocado"));

        Assert.Equal(EstadoInstanciaDocumento.Devuelto, devuelta.Estado);
        var pendiente = Assert.Single(await docs.ListarPendientesEscrituraWOAsync(Empresa.Ferrealiados));
        Assert.Equal(oc.Id, pendiente.InstanciaDocumentoId);
        Assert.Equal(ResultadoPrimerPaso.Devuelto, pendiente.Resultado);
        Assert.DoesNotContain(await docs.ListarPendientesEscrituraWOAsync(Empresa.Jimaco), p => p.InstanciaDocumentoId == oc.Id);
    }

    [Fact]
    public async Task Al_volver_a_solicitar_aprobacion_desde_WO_se_aprueba_sola_otra_vez_y_vuelve_a_Logistica()
    {
        var s = await SembrarAsync();
        var docs = Documentos();
        var oc = await docs.CrearAsync(Oc(s.TipoFerrealiados, 1503), s.Sincronizador);
        await docs.ConfirmarEscrituraWOAsync(oc.Id);
        await docs.DevolverForzadoAsync(oc.Id, s.Asistente, new DevolverForzadoDto("Proveedor equivocado"));
        await docs.ConfirmarEscrituraWOAsync(oc.Id); // WO ya quedó en espera

        var reenviada = await docs.CrearAsync(Oc(s.TipoFerrealiados, 1503), s.Sincronizador);

        Assert.Equal(oc.Id, reenviada.Id);
        Assert.Equal(EstadoInstanciaDocumento.EnProceso, reenviada.Estado);
        var instancia = await db.InstanciasDocumento.SingleAsync(i => i.Id == oc.Id);
        Assert.Equal(s.PasoRecogida, instancia.PasoActualId);
        Assert.Equal(2, await db.HistorialAcciones.CountAsync(h => h.InstanciaDocumentoId == oc.Id && h.Accion == TipoAccion.AprobadoAutomatico));
        var pendiente = Assert.Single(await docs.ListarPendientesEscrituraWOAsync(Empresa.Ferrealiados));
        Assert.Equal(ResultadoPrimerPaso.Aprobado, pendiente.Resultado);
    }
}
