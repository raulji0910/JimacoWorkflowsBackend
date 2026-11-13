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
/// 2026-11-13: FERREALIADOS JV en la misma aplicación (mismos aprobadores, logística desde Jimaco).
/// OC a crédito sin aprobación (flujo sin pasos) → Hoja directo a Logística; contado por el flujo
/// normal; dashboard de alistamiento aparte; la OC/COT 894 de una empresa nunca se mezcla con la de
/// la otra; cada Sincronizador solo escribe en el WO de su empresa.
/// </summary>
public class FerrealiadosEmpresaTests
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<INotificacionService> notificaciones = new();

    private InstanciaDocumentoService Documentos() =>
        new(db, Mock.Of<IAlmacenamientoArchivos>(), notificaciones.Object, TimeProvider.System);

    private HojaAlistamientoService Hojas() =>
        new(db, Mock.Of<IConfiguration>(), TimeProvider.System, notificaciones.Object);

    private record Semilla(int TipoJimaco, int TipoFerrealiados, int Emisor, int Comercial, int LogisticaJimaco, int LogisticaFerrealiados);

    private async Task<Semilla> SembrarAsync()
    {
        var comercial = new Rol { Nombre = "Gerencia Comercial", Activo = true };
        var logJimaco = new Rol { Nombre = "Logística Jimaco", Activo = true, PuedeGestionarAlistamiento = true };
        var logFerre = new Rol { Nombre = "Logística Ferrealiados", Activo = true, PuedeGestionarAlistamientoFerrealiados = true };
        var emisor = new Usuario { Nombre = "Sincronizador", Email = "sinc@test", PasswordHash = "x" };
        var uComercial = new Usuario { Nombre = "Comercial", Email = "comercial@test", PasswordHash = "x", UsuarioWO = "YAZMIN RENDON", UsuarioWOFerrealiados = "CAROLINA OCHOA" };
        var uLogJimaco = new Usuario { Nombre = "Transporte Jimaco", Email = "tj@test", PasswordHash = "x" };
        var uLogFerre = new Usuario { Nombre = "Transporte Ferrealiados", Email = "tf@test", PasswordHash = "x" };
        var tipoJ = new TipoDocumento { Nombre = "Orden de Compra", PrefijoWorldOffice = "OC" };
        var tipoF = new TipoDocumento { Nombre = "Orden de Compra Ferrealiados", PrefijoWorldOffice = "OC", Empresa = Empresa.Ferrealiados };
        db.AddRange(comercial, logJimaco, logFerre, emisor, uComercial, uLogJimaco, uLogFerre, tipoJ, tipoF);
        await db.SaveChangesAsync();
        db.UsuarioRoles.AddRange(
            new UsuarioRol { UsuarioId = uComercial.Id, RolId = comercial.Id },
            new UsuarioRol { UsuarioId = uLogJimaco.Id, RolId = logJimaco.Id },
            new UsuarioRol { UsuarioId = uLogFerre.Id, RolId = logFerre.Id });

        // Jimaco: un paso. Ferrealiados: crédito SIN pasos; contado con el paso comercial.
        var pasoJ = new PasoFlujo { Nombre = "Aprobación comercial", Orden = 1, PermiteDevolver = true, PermiteDevolverAlEmisor = true };
        var pasoF = new PasoFlujo { Nombre = "Aprobación comercial", Orden = 1, PermiteDevolver = true, PermiteDevolverAlEmisor = true };
        db.DefinicionesFlujo.AddRange(
            new DefinicionFlujo { Nombre = "OC Jimaco", TipoDocumentoId = tipoJ.Id, Activo = true, Pasos = [pasoJ] },
            new DefinicionFlujo { Nombre = "OC Ferrealiados crédito", TipoDocumentoId = tipoF.Id, Activo = true, FormaDePagoRequerida = "Credito" },
            new DefinicionFlujo { Nombre = "OC Ferrealiados contado", TipoDocumentoId = tipoF.Id, Activo = true, FormaDePagoRequerida = "Contado", Pasos = [pasoF] });
        await db.SaveChangesAsync();
        db.PasoFlujoRoles.AddRange(
            new PasoFlujoRol { PasoFlujoId = pasoJ.Id, RolId = comercial.Id },
            new PasoFlujoRol { PasoFlujoId = pasoF.Id, RolId = comercial.Id });
        await db.SaveChangesAsync();
        return new(tipoJ.Id, tipoF.Id, emisor.Id, uComercial.Id, uLogJimaco.Id, uLogFerre.Id);
    }

    private static DocumentoVinculadoInputDto Cotizacion(string numero) => new(
        "COT", numero, new DateTime(2026, 11, 11), new DateTime(2026, 12, 26), "CLIENTE", "900", null, null, null,
        "CAROLINA OCHOA", "Credito", "ENTREGAR EN SANTA LIBRADA",
        [new RenglonDocumentoVinculadoInputDto("PJ-1", "TEJA", 26, "Und", 1000, 0.19m)]);

    private static CrearInstanciaDocumentoDto Oc(int tipoId, int asiento, string formaDePago, string cotizacion, string solicitante = "CAROLINA OCHOA") => new(
        tipoId, asiento.ToString(), "Proveedor", 500_000m, null,
        new Dictionary<string, string> { ["formaDePago"] = formaDePago },
        IdAsientoContableOrigen: asiento, PrefijoOrigen: "OC",
        DocumentosVinculados: [Cotizacion(cotizacion)], SolicitanteWO: solicitante);

    [Fact]
    public async Task OC_a_credito_de_Ferrealiados_queda_aprobada_al_llegar_y_su_hoja_va_directo_a_logistica()
    {
        var s = await SembrarAsync();

        var oc = await Documentos().CrearAsync(Oc(s.TipoFerrealiados, 1503, "Credito", "894"), s.Emisor);

        Assert.Equal(EstadoInstanciaDocumento.Completado, oc.Estado);
        var instancia = await db.InstanciasDocumento.SingleAsync();
        Assert.Equal(Empresa.Ferrealiados, instancia.Empresa);
        Assert.True(instancia.PendienteEscrituraWO);
        Assert.Contains(await db.HistorialAcciones.ToListAsync(), h => h.Accion == TipoAccion.AprobadoAutomatico);
        // Nadie tiene que aprobar: no se avisa a ningún paso.
        notificaciones.Verify(n => n.NotificarPasoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        var hojas = await Hojas().ListarAsync(s.LogisticaFerrealiados, empresa: Empresa.Ferrealiados);
        var hoja = Assert.Single(hojas);
        Assert.Equal("894", hoja.NumeroReferenciaHoja);
    }

    [Fact]
    public async Task OC_de_contado_de_Ferrealiados_pasa_por_el_flujo_y_su_hoja_aparece_al_aprobar()
    {
        var s = await SembrarAsync();
        var oc = await Documentos().CrearAsync(Oc(s.TipoFerrealiados, 1484, "Contado", "888"), s.Emisor);

        Assert.Equal(EstadoInstanciaDocumento.EnProceso, oc.Estado);
        Assert.Empty(await Hojas().ListarAsync(s.LogisticaFerrealiados, empresa: Empresa.Ferrealiados));

        await Documentos().EjecutarAccionAsync(oc.Id, s.Comercial, new EjecutarAccionDto(TipoAccion.Aprobado, null));

        Assert.Single(await Hojas().ListarAsync(s.LogisticaFerrealiados, empresa: Empresa.Ferrealiados));
    }

    [Fact]
    public async Task Los_dashboards_no_se_mezclan_y_cada_logistica_solo_ve_el_suyo()
    {
        var s = await SembrarAsync();
        var docs = Documentos();
        // Misma OC y misma COT (números) en las dos empresas: son documentos distintos.
        var ocJ = await docs.CrearAsync(Oc(s.TipoJimaco, 894, "Credito", "894", "YAZMIN RENDON"), s.Emisor);
        await docs.EjecutarAccionAsync(ocJ.Id, s.Comercial, new EjecutarAccionDto(TipoAccion.Aprobado, null));
        await docs.CrearAsync(Oc(s.TipoFerrealiados, 894, "Credito", "894"), s.Emisor);

        Assert.Equal(2, await db.InstanciasDocumento.CountAsync());
        Assert.Equal(2, await db.DocumentosVinculados.CountAsync());

        var jimaco = Assert.Single(await Hojas().ListarAsync(s.LogisticaJimaco));
        var ferre = Assert.Single(await Hojas().ListarAsync(s.LogisticaFerrealiados, empresa: Empresa.Ferrealiados));
        Assert.NotEqual(jimaco.DocumentoVinculadoId, ferre.DocumentoVinculadoId);
        Assert.Equal(["894"], jimaco.Ocs.Select(o => o.NumeroReferencia));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Hojas().ListarAsync(s.LogisticaJimaco, empresa: Empresa.Ferrealiados));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Hojas().ListarAsync(s.LogisticaFerrealiados));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Hojas().ObtenerAsync(ferre.DocumentoVinculadoId, s.LogisticaJimaco));
    }

    [Fact]
    public async Task La_disponibilidad_marcada_en_una_empresa_no_toca_la_hoja_de_la_otra()
    {
        var s = await SembrarAsync();
        var docs = Documentos();
        var ocJ = await docs.CrearAsync(Oc(s.TipoJimaco, 10, "Credito", "894", "YAZMIN RENDON"), s.Emisor);
        await docs.EjecutarAccionAsync(ocJ.Id, s.Comercial, new EjecutarAccionDto(TipoAccion.Aprobado, null));
        await docs.CrearAsync(Oc(s.TipoFerrealiados, 20, "Credito", "894"), s.Emisor);

        var hojaF = await db.DocumentosVinculados.Include(d => d.Renglones).SingleAsync(d => d.Empresa == Empresa.Ferrealiados);
        await Hojas().ActualizarCantidadesAsync(hojaF.Id, s.LogisticaFerrealiados,
            new ActualizarCantidadesDisponiblesDto([new ActualizarCantidadRenglonDto(hojaF.Renglones.Single().Id, 26)]));

        var hojaJ = await db.DocumentosVinculados.Include(d => d.Renglones).SingleAsync(d => d.Empresa == Empresa.Jimaco);
        Assert.Equal(0, hojaJ.Renglones.Single().CantidadDisponible);
        Assert.Equal(26, (await db.DocumentosVinculados.Include(d => d.Renglones).SingleAsync(d => d.Id == hojaF.Id)).Renglones.Single().CantidadDisponible);
    }

    [Fact]
    public async Task Cada_sincronizador_solo_recibe_lo_de_su_World_Office_y_con_el_usuario_de_esa_empresa()
    {
        var s = await SembrarAsync();
        var docs = Documentos();
        var ocJ = await docs.CrearAsync(Oc(s.TipoJimaco, 30, "Credito", "100", "YAZMIN RENDON"), s.Emisor);
        await docs.EjecutarAccionAsync(ocJ.Id, s.Comercial, new EjecutarAccionDto(TipoAccion.Aprobado, null));
        var ocF = await docs.CrearAsync(Oc(s.TipoFerrealiados, 40, "Contado", "200"), s.Emisor);
        await docs.EjecutarAccionAsync(ocF.Id, s.Comercial, new EjecutarAccionDto(TipoAccion.Aprobado, null));

        var paraJimaco = Assert.Single(await docs.ListarPendientesEscrituraWOAsync(Empresa.Jimaco));
        var paraFerre = Assert.Single(await docs.ListarPendientesEscrituraWOAsync(Empresa.Ferrealiados));

        Assert.Equal(ocJ.Id, paraJimaco.InstanciaDocumentoId);
        Assert.Equal("YAZMIN RENDON", paraJimaco.UsuarioWO);
        Assert.Equal(ocF.Id, paraFerre.InstanciaDocumentoId);
        Assert.Equal("CAROLINA OCHOA", paraFerre.UsuarioWO); // su usuario en el WO de Ferrealiados
    }

    [Fact]
    public async Task La_misma_OC_de_Ferrealiados_que_vuelve_a_llegar_no_se_confunde_con_la_de_Jimaco()
    {
        var s = await SembrarAsync();
        var docs = Documentos();
        var ocJ = await docs.CrearAsync(Oc(s.TipoJimaco, 777, "Credito", "1", "YAZMIN RENDON"), s.Emisor);
        var ocF = await docs.CrearAsync(Oc(s.TipoFerrealiados, 777, "Credito", "2"), s.Emisor);
        var ocFOtraVez = await docs.CrearAsync(Oc(s.TipoFerrealiados, 777, "Credito", "2"), s.Emisor);

        Assert.NotEqual(ocJ.Id, ocF.Id);
        Assert.Equal(ocF.Id, ocFOtraVez.Id);
    }

    [Fact]
    public async Task Pendientes_de_remision_y_refresco_se_piden_por_empresa()
    {
        var s = await SembrarAsync();
        await Documentos().CrearAsync(Oc(s.TipoFerrealiados, 50, "Credito", "894"), s.Emisor);

        Assert.Empty(await Hojas().ListarPendientesRemisionAsync());
        Assert.Single(await Hojas().ListarPendientesRemisionAsync(empresa: Empresa.Ferrealiados));
        Assert.Single(await Hojas().ListarPendientesRefrescoAsync(empresa: Empresa.Ferrealiados));

        await Hojas().MarcarRemisionadaAsync(new MarcarRemisionadaDto("COT", "894", "783", new DateTime(2026, 11, 12),
            [new RenglonRemisionadoDto("PJ-1", 26)]), empresa: Empresa.Ferrealiados);
        Assert.Empty(await Hojas().ListarPendientesRemisionAsync(empresa: Empresa.Ferrealiados));
    }

    [Fact]
    public void El_encabezado_X_Empresa_vacio_o_desconocido_es_Jimaco()
    {
        Assert.Equal(Empresa.Jimaco, EmpresaExtensions.Parsear(null));
        Assert.Equal(Empresa.Jimaco, EmpresaExtensions.Parsear(""));
        Assert.Equal(Empresa.Jimaco, EmpresaExtensions.Parsear("otra"));
        Assert.Equal(Empresa.Ferrealiados, EmpresaExtensions.Parsear(" ferrealiados "));
    }
}
