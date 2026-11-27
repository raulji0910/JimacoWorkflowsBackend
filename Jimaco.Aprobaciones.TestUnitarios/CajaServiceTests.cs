using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>Cajas menores de efectivo (2026-11-27).</summary>
public class CajaServiceTests
{
    private const int Admin = 901, Girador = 902, Otro = 903, GiradorFerrealiados = 904;
    private const int CajaJimaco = 1, CajaFerrealiados = 2;

    private sealed class Reloj : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private sealed class Contexto(AppDbContext db, Reloj reloj, Mock<INotificacionService> avisos)
    {
        public AppDbContext Db { get; } = db;
        public Reloj Reloj { get; } = reloj;
        public Mock<INotificacionService> Avisos { get; } = avisos;
        public CajaService Servicio => new(Db, Reloj, Avisos.Object, Mock.Of<IAlmacenamientoArchivos>());
    }

    private static Contexto Crear()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated(); // siembra las dos cajas (HasData)
        foreach (var (id, nombre) in new[] { (Admin, "Gerente General"), (Girador, "Asistente Gerencia"), (Otro, "Vendedora"), (GiradorFerrealiados, "Asistente Ferrealiados") })
            db.Usuarios.Add(new Usuario { Id = id, Nombre = nombre, Email = $"u{id}@x.com", PasswordHash = "x" });
        db.Roles.AddRange(
            new Rol { Id = 901, Nombre = "Administrador de caja", PuedeAdministrarCajaJimaco = true, PuedeAdministrarCajaFerrealiados = true },
            new Rol { Id = 902, Nombre = "Girador", PuedeGirarCajaJimaco = true },
            new Rol { Id = 903, Nombre = "Vendedora" },
            new Rol { Id = 904, Nombre = "Girador Ferrealiados", PuedeGirarCajaFerrealiados = true });
        db.UsuarioRoles.AddRange(
            new UsuarioRol { UsuarioId = Admin, RolId = 901 }, new UsuarioRol { UsuarioId = Girador, RolId = 902 },
            new UsuarioRol { UsuarioId = Otro, RolId = 903 }, new UsuarioRol { UsuarioId = GiradorFerrealiados, RolId = 904 });
        db.ConceptosCaja.AddRange(new ConceptoCaja { Id = 1, CajaId = CajaJimaco, Nombre = "TRANSPORTE" }, new ConceptoCaja { Id = 2, CajaId = CajaFerrealiados, Nombre = "PAGO OC" });
        db.BeneficiariosCaja.Add(new BeneficiarioCaja { Id = 1, Nombre = "PEDRO LOGISTICA", Cedula = "1010", CreadoPorUsuarioId = Admin });
        db.SaveChanges();
        return new Contexto(db, new Reloj(), new Mock<INotificacionService>());
    }

    /// <summary>Flujo "OC Contado hasta $300.000" con el paso del asistente marcado y una OC parada ahí.</summary>
    private static (PasoFlujo PasoAsistente, InstanciaDocumento Oc) CrearOc(AppDbContext db, decimal valor, Empresa empresa = Empresa.Jimaco, string numero = "20100")
    {
        var tipo = db.TiposDocumento.FirstOrDefault(t => t.Empresa == empresa && t.PrefijoWorldOffice == "OC")
                   ?? db.TiposDocumento.Add(new TipoDocumento { Nombre = $"Orden de Compra {empresa}", PrefijoWorldOffice = "OC", Empresa = empresa }).Entity;
        var flujo = new DefinicionFlujo { Nombre = "OC Contado hasta $300.000", TipoDocumento = tipo, Activo = true, FormaDePagoRequerida = "Contado" };
        var comercial = new PasoFlujo { Nombre = "Aprobacion comercial", Orden = 1, DefinicionFlujo = flujo };
        var asistente = new PasoFlujo { Nombre = "Entrega Asistente Gerencia", Orden = 2, DefinicionFlujo = flujo, RequiereGiroCajaAlAprobar = true };
        var transporte = new PasoFlujo { Nombre = "Recogida Transporte", Orden = 3, DefinicionFlujo = flujo };
        asistente.PasoFlujoRoles.Add(new PasoFlujoRol { RolId = 902 });
        flujo.Pasos = [comercial, asistente, transporte];
        var oc = new InstanciaDocumento
        {
            TipoDocumento = tipo, DefinicionFlujo = flujo, PasoActual = asistente, Empresa = empresa,
            NumeroReferencia = numero, Proveedor = "FERRETERIA X", Valor = valor, CreadoPorUsuarioId = Admin
        };
        db.InstanciasDocumento.Add(oc);
        db.SaveChanges();
        return (asistente, oc);
    }

    private static async Task Recargar(Contexto c, decimal valor, int caja = CajaJimaco) =>
        await c.Servicio.RegistrarRecargaAsync(caja, new RegistrarRecargaDto(valor, "Fondo inicial"), Admin);

    // ---------------- Permisos ----------------

    [Fact]
    public async Task CadaUsuarioVeSoloLasCajasDondeTieneRol()
    {
        var c = Crear();

        Assert.Equal(2, (await c.Servicio.ListarAsync(Admin)).Count);
        var delGirador = Assert.Single(await c.Servicio.ListarAsync(Girador));
        Assert.Equal(Empresa.Jimaco, delGirador.Empresa);
        Assert.False(delGirador.EsAdministrador);
        Assert.True(delGirador.EsGirador);
        Assert.Empty(await c.Servicio.ListarAsync(Otro));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Servicio.ObtenerAsync(CajaFerrealiados, Girador));
    }

    [Fact]
    public async Task ElGiradorNoRecargaNiCreaConceptosNiCambiaElTope()
    {
        var c = Crear();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Servicio.RegistrarRecargaAsync(CajaJimaco, new(100_000, null), Girador));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Servicio.CrearConceptoAsync(CajaJimaco, new("PAPELERIA"), Girador));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Servicio.ActualizarAsync(CajaJimaco, new(3_000_000, 0), Girador));
    }

    // ---------------- Saldo ----------------

    [Fact]
    public async Task Recarga_SubeElSaldoYMuestraCuantoFaltaParaElTope()
    {
        var c = Crear();

        await Recargar(c, 1_500_000);

        var caja = await c.Servicio.ObtenerAsync(CajaJimaco, Girador);
        Assert.Equal(1_500_000, caja.SaldoActual);
        Assert.Equal(500_000, caja.FaltaParaTope);
    }

    [Fact]
    public async Task NoSePuedeGirarMasDelEfectivoQueHay()
    {
        var c = Crear();
        await Recargar(c, 100_000);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 150_000, null, null), Girador));

        Assert.Contains("No hay suficiente efectivo", ex.Message);
        Assert.Equal(100_000, (await c.Servicio.ObtenerAsync(CajaJimaco, Admin)).SaldoActual);
    }

    [Fact]
    public async Task CicloCompleto_GiroLegalizacionConVuelta()
    {
        var c = Crear();
        await Recargar(c, 1_000_000);

        var giro = await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 200_000, "Domicilios", null), Girador);
        Assert.Equal(1, giro.Numero);
        Assert.Equal(EstadoGiroCaja.Abierto, giro.Estado);
        var caja = await c.Servicio.ObtenerAsync(CajaJimaco, Admin);
        Assert.Equal(800_000, caja.SaldoActual);
        Assert.Equal(1, caja.GirosAbiertos);
        Assert.Equal(200_000, caja.ValorGirosAbiertos);

        giro = await c.Servicio.LegalizarGiroAsync(CajaJimaco, giro.Id, new(170_000, "Facturas"), Girador);
        Assert.Equal(EstadoGiroCaja.VueltaPendiente, giro.Estado);
        Assert.Equal(30_000, giro.VueltaPendiente);
        Assert.Equal(30_000, (await c.Servicio.ObtenerAsync(CajaJimaco, Admin)).ValorVueltasPendientes);

        // Puede devolver en partes.
        giro = await c.Servicio.RecibirVueltaAsync(CajaJimaco, giro.Id, new(10_000, null), Girador);
        Assert.Equal(EstadoGiroCaja.VueltaPendiente, giro.Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Servicio.RecibirVueltaAsync(CajaJimaco, giro.Id, new(25_000, null), Girador));
        giro = await c.Servicio.RecibirVueltaAsync(CajaJimaco, giro.Id, new(20_000, null), Girador);

        Assert.Equal(EstadoGiroCaja.Cerrado, giro.Estado);
        Assert.Equal(830_000, (await c.Servicio.ObtenerAsync(CajaJimaco, Admin)).SaldoActual);
        var historial = await c.Servicio.HistorialAsync(CajaJimaco, new FiltroHistorialCajaDto(), Admin);
        Assert.Equal(4, historial.Total); // recarga, giro, 2 vueltas
        Assert.Equal(830_000, historial.Movimientos[0].SaldoDespues);
    }

    [Fact]
    public async Task Legalizacion_GastoDeMas_PagaElReintegroYCierra()
    {
        var c = Crear();
        await Recargar(c, 500_000);
        var giro = await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 100_000, null, null), Girador);

        giro = await c.Servicio.LegalizarGiroAsync(CajaJimaco, giro.Id, new(120_000, null), Girador);

        Assert.Equal(EstadoGiroCaja.Cerrado, giro.Estado);
        Assert.Contains(giro.Movimientos, m => m.Tipo == TipoMovimientoCaja.Reintegro && m.Valor == -20_000);
        Assert.Equal(380_000, (await c.Servicio.ObtenerAsync(CajaJimaco, Admin)).SaldoActual);
    }

    [Fact]
    public async Task Arqueo_SinDiferenciaNoAjusta_ConDiferenciaExigeMotivoYAjusta()
    {
        var c = Crear();
        await Recargar(c, 500_000);

        var cuadrado = await c.Servicio.RegistrarArqueoAsync(CajaJimaco, new(500_000, null), Admin);
        Assert.Null(cuadrado.Ajuste);

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Servicio.RegistrarArqueoAsync(CajaJimaco, new(480_000, null), Admin));
        var ajustado = await c.Servicio.RegistrarArqueoAsync(CajaJimaco, new(480_000, "Faltante en el conteo"), Admin);

        Assert.Equal(-20_000, ajustado.Diferencia);
        Assert.Equal(TipoMovimientoCaja.Ajuste, ajustado.Ajuste!.Tipo);
        Assert.Equal(480_000, (await c.Servicio.ObtenerAsync(CajaJimaco, Admin)).SaldoActual);
    }

    [Fact]
    public async Task AnularRecarga_DevuelveElSaldoYQuedaEnElHistorial()
    {
        var c = Crear();
        var recarga = await c.Servicio.RegistrarRecargaAsync(CajaJimaco, new(300_000, null), Admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Servicio.AnularMovimientoAsync(CajaJimaco, recarga.Id, new(""), Admin));
        var anulada = await c.Servicio.AnularMovimientoAsync(CajaJimaco, recarga.Id, new("Valor equivocado"), Admin);

        Assert.True(anulada.Anulado);
        Assert.Equal(0, (await c.Servicio.ObtenerAsync(CajaJimaco, Admin)).SaldoActual);
        Assert.Equal(1, (await c.Servicio.HistorialAsync(CajaJimaco, new FiltroHistorialCajaDto(), Admin)).Total);
    }

    [Fact]
    public async Task AnularGiroAbierto_LaPlataVuelveALaCaja()
    {
        var c = Crear();
        await Recargar(c, 300_000);
        var giro = await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 100_000, null, null), Girador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Servicio.AnularGiroAsync(CajaJimaco, giro.Id, new("error"), Girador));
        giro = await c.Servicio.AnularGiroAsync(CajaJimaco, giro.Id, new("Se giró a la persona equivocada"), Admin);

        Assert.Equal(EstadoGiroCaja.Anulado, giro.Estado);
        Assert.Equal(300_000, (await c.Servicio.ObtenerAsync(CajaJimaco, Admin)).SaldoActual);
    }

    [Fact]
    public async Task SaldoBajo_AvisaUnaSolaVezAlCruzarElMinimo()
    {
        var c = Crear();
        await Recargar(c, 500_000);
        await c.Servicio.ActualizarAsync(CajaJimaco, new ActualizarCajaDto(2_000_000, 300_000), Admin);

        await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 250_000, null, null), Girador); // queda 250.000
        await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 50_000, null, null), Girador);  // queda 200.000

        // Solo el administrador (Admin), una sola vez.
        c.Avisos.Verify(a => a.NotificarHtmlAsync(null, Admin, It.Is<string>(s => s.Contains("hay que recargar")), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        c.Avisos.Verify(a => a.NotificarHtmlAsync(null, Girador, It.Is<string>(s => s.Contains("hay que recargar")), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True((await c.Servicio.ObtenerAsync(CajaJimaco, Admin)).SaldoBajo);
    }

    // ---------------- OC de contado ----------------

    [Fact]
    public async Task GiroConOc_SeLegalizaSoloYAvisaLaVueltaPendiente()
    {
        var c = Crear();
        await Recargar(c, 1_000_000);
        var (_, oc1) = CrearOc(c.Db, 300_000, numero: "20101");
        var (_, oc2) = CrearOc(c.Db, 150_000, numero: "20102");
        Assert.Equal(2, (await c.Servicio.OrdenesCompraPendientesAsync(CajaJimaco, Girador)).Count);

        var giro = await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 500_000, null, [oc1.Id, oc2.Id]), Girador);

        Assert.Equal(EstadoGiroCaja.VueltaPendiente, giro.Estado);
        Assert.Equal(450_000, giro.ValorLegalizado);
        Assert.Equal(50_000, giro.VueltaPendiente);
        Assert.Equal(2, giro.OrdenesCompra.Count);
        Assert.Empty(await c.Servicio.OrdenesCompraPendientesAsync(CajaJimaco, Girador));
        c.Avisos.Verify(a => a.NotificarHtmlAsync(null, Admin, It.Is<string>(s => s.Contains("esperando 50.000 de vuelta")), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        c.Avisos.Verify(a => a.NotificarHtmlAsync(null, Girador, It.Is<string>(s => s.Contains("esperando 50.000 de vuelta")), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GiroConOc_ElGiroTieneQueCubrirLasOcYSerDeLaMismaEmpresa()
    {
        var c = Crear();
        await Recargar(c, 1_000_000);
        await Recargar(c, 1_000_000, CajaFerrealiados);
        var (_, ocJimaco) = CrearOc(c.Db, 300_000);
        var (_, ocFerrealiados) = CrearOc(c.Db, 100_000, Empresa.Ferrealiados, "1503");

        var menor = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 250_000, null, [ocJimaco.Id]), Girador));
        Assert.Contains("tiene que cubrir las OC", menor.Message);

        var otraEmpresa = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 200_000, null, [ocFerrealiados.Id]), Girador));
        Assert.Contains("Ferrealiados", otraEmpresa.Message);

        var exacto = await c.Servicio.CrearGiroAsync(CajaFerrealiados, new CrearGiroCajaDto(1, 2, 100_000, null, [ocFerrealiados.Id]), GiradorFerrealiados);
        Assert.Equal(EstadoGiroCaja.Cerrado, exacto.Estado);

        // Una OC no se paga dos veces.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Servicio.CrearGiroAsync(CajaFerrealiados, new CrearGiroCajaDto(1, 2, 100_000, null, [ocFerrealiados.Id]), GiradorFerrealiados));
    }

    [Fact]
    public async Task AprobarElPasoDelAsistente_ExigeElGiroDeCaja()
    {
        var c = Crear();
        await Recargar(c, 1_000_000);
        var (_, oc) = CrearOc(c.Db, 200_000);
        var instancias = new InstanciaDocumentoService(c.Db, Mock.Of<IAlmacenamientoArchivos>(), Mock.Of<INotificacionService>(), c.Reloj);

        var sinGiro = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            instancias.EjecutarAccionAsync(oc.Id, Girador, new EjecutarAccionDto(TipoAccion.Aprobado, null)));
        Assert.Contains("caja menor", sinGiro.Message);

        await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 200_000, null, [oc.Id]), Girador);
        var aprobada = await instancias.EjecutarAccionAsync(oc.Id, Girador, new EjecutarAccionDto(TipoAccion.Aprobado, null));

        Assert.Equal("Recogida Transporte", aprobada.PasoActualNombre);
    }

    [Fact]
    public async Task GiroConOcYaAprobada_NoSePuedeAnular()
    {
        var c = Crear();
        await Recargar(c, 1_000_000);
        var (_, oc) = CrearOc(c.Db, 200_000);
        var giro = await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 200_000, null, [oc.Id]), Girador);
        var instancias = new InstanciaDocumentoService(c.Db, Mock.Of<IAlmacenamientoArchivos>(), Mock.Of<INotificacionService>(), c.Reloj);
        await instancias.EjecutarAccionAsync(oc.Id, Girador, new EjecutarAccionDto(TipoAccion.Aprobado, null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => c.Servicio.AnularGiroAsync(CajaJimaco, giro.Id, new("error"), Admin));
        Assert.Contains("ya se aprobó", ex.Message);
    }

    [Fact]
    public async Task MarcarPasoDePago_SoloPasosDeOcDeLaEmpresaDeLaCaja()
    {
        var c = Crear();
        var (paso, _) = CrearOc(c.Db, 100_000);
        paso.RequiereGiroCajaAlAprobar = false;
        c.Db.SaveChanges();

        var pasos = await c.Servicio.ListarPasosPagoAsync(CajaJimaco, Admin);
        Assert.Equal(3, pasos.Count);
        var marcado = await c.Servicio.MarcarPasoPagoAsync(CajaJimaco, paso.Id, new(true), Admin);
        Assert.True(marcado.RequiereGiroCaja);
        Assert.Equal(1, marcado.OrdenesEnElPaso);

        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Servicio.MarcarPasoPagoAsync(CajaFerrealiados, paso.Id, new(true), Admin));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Servicio.MarcarPasoPagoAsync(CajaJimaco, paso.Id, new(false), Girador));
    }

    // ---------------- Personas, conceptos, recordatorios ----------------

    [Fact]
    public async Task Beneficiario_CedulaUnicaYBusquedaPorNombreOCedula()
    {
        var c = Crear();

        var creado = await c.Servicio.CrearBeneficiarioAsync(new GuardarBeneficiarioCajaDto(" juan  pérez ", "1.020.304", "300 123", "Juan@X.com", true), Girador);
        Assert.Equal("JUAN PÉREZ", creado.Nombre);
        Assert.Equal("1020304", creado.Cedula);
        Assert.Equal("juan@x.com", creado.Correo);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Servicio.CrearBeneficiarioAsync(new GuardarBeneficiarioCajaDto("Otro", "1020304", null, null, null), Girador));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            c.Servicio.CrearBeneficiarioAsync(new GuardarBeneficiarioCajaDto("Sin cédula", "", null, null, null), Girador));
        Assert.Single(await c.Servicio.BuscarBeneficiariosAsync("pérez", Girador));
        Assert.Single(await c.Servicio.BuscarBeneficiariosAsync("10203", Girador));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => c.Servicio.BuscarBeneficiariosAsync(null, Otro));
    }

    [Fact]
    public async Task Concepto_NoSeRepiteEnLaMismaCaja()
    {
        var c = Crear();

        await c.Servicio.CrearConceptoAsync(CajaJimaco, new("papelería"), Admin);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.Servicio.CrearConceptoAsync(CajaJimaco, new("PAPELERÍA"), Admin));
        await c.Servicio.CrearConceptoAsync(CajaFerrealiados, new("papelería"), Admin);
    }

    [Fact]
    public async Task Recordatorio_CadaVeinticuatroHorasUnCorreoPorCaja()
    {
        var c = Crear();
        await Recargar(c, 1_000_000);
        await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 100_000, null, null), Girador);
        await c.Servicio.CrearGiroAsync(CajaJimaco, new CrearGiroCajaDto(1, 1, 50_000, null, null), Girador);

        c.Reloj.Ahora = c.Reloj.Ahora.AddHours(23);
        Assert.Equal(0, await c.Servicio.EnviarRecordatoriosAsync());

        c.Reloj.Ahora = c.Reloj.Ahora.AddHours(2);
        Assert.Equal(2, await c.Servicio.EnviarRecordatoriosAsync());
        c.Avisos.Verify(a => a.NotificarHtmlAsync(null, Girador, It.Is<string>(s => s.Contains("2 giro(s) pendiente(s)")), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        c.Reloj.Ahora = c.Reloj.Ahora.AddHours(5);
        Assert.Equal(0, await c.Servicio.EnviarRecordatoriosAsync()); // no repite antes de 24 h
        c.Reloj.Ahora = c.Reloj.Ahora.AddHours(20);
        Assert.Equal(2, await c.Servicio.EnviarRecordatoriosAsync());
    }

    [Fact]
    public void PdfDelGiro_SeGenera()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var giro = new GiroCajaDto(1, 1, Empresa.Jimaco, 7, EstadoGiroCaja.VueltaPendiente, new DateTime(2026, 11, 27, 15, 0, 0),
            new BeneficiarioCajaDto(1, "PEDRO LOGISTICA", "1010", null, null, true, true), new ConceptoCajaDto(1, "PAGO OC", true),
            500_000, "Pago proveedor", "Asistente", 450_000, DateTime.UtcNow, "Asistente", null, 0, 50_000, false, null, null, null, null,
            [new OrdenCompraGiroDto(10, "20101", "FERRETERIA X", 450_000, "Entrega Asistente Gerencia")], []);

        var pdf = GiroCajaPdfGenerador.Generar(giro);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
