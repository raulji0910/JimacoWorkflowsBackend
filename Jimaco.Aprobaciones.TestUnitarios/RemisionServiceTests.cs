using System.Text;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>2026-11-12: módulo de Entregas (Remisiones contabilizadas de WO).</summary>
public class RemisionServiceTests
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<IAlmacenamientoArchivos> almacenamiento = new();

    private static readonly DateTime Inicio = new(2026, 11, 12);

    public RemisionServiceTests()
    {
        almacenamiento.Setup(a => a.GuardarAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string nombre, Stream _, CancellationToken _) => "adjuntos/" + nombre);
        db.ConfiguracionesEntregas.Add(new ConfiguracionEntregas { Id = 1, FechaInicio = Inicio });
        db.SaveChanges();
    }

    private sealed class Reloj : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly Reloj reloj = new();

    // IA apagada por defecto (solo QR); cada prueba de IA la enciende y define qué "ve".
    private readonly Mock<IVerificadorSoporteEntrega> ia = new();
    private readonly MemoryCache cache = new(new MemoryCacheOptions());

    private RemisionService Servicio() => new(db, almacenamiento.Object, new RemisionPdfGenerador(),
        Mock.Of<IConfiguration>(c => c["Entregas:HorasUmbralAlerta"] == "48"),
        reloj, ia.Object, cache);

    private void IaVe(string? numero, bool firma, bool legible = true, bool sello = false)
    {
        ia.SetupGet(x => x.Activo).Returns(true);
        ia.Setup(x => x.VerificarAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoVerificacionIA(true, numero, true, firma, firma, legible, "alta", "obs", null, sello));
    }

    private static ArchivoSoporteEntrega Archivo(string nombre, string? qr, string contenido) =>
        new(nombre, "image/jpeg", new MemoryStream(Encoding.UTF8.GetBytes(contenido)), contenido.Length, qr);

    private async Task<int> UsuarioAsync(string nombre, bool gestiona, bool ve)
    {
        var rol = new Rol { Nombre = "Rol " + nombre, Activo = true, PuedeGestionarEntregas = gestiona, PuedeVerEntregas = ve };
        var u = new Usuario { Nombre = nombre, Email = nombre.Replace(" ", "") + "@jimaco.local", PasswordHash = "x" };
        db.AddRange(rol, u);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = u.Id, RolId = rol.Id });
        await db.SaveChangesAsync();
        return u.Id;
    }

    private static RemisionInputDto Rem(int idWO, string numero, DateTime? fecha = null, int renglones = 2) => new(
        idWO, "REM", numero, fecha ?? Inicio, "VENTUS COLOMBIA S.A.S.", "900123", "CRA 9 115 06 P 17", "Bogota D.C.", "6016398498",
        "YAZMIN RENDON RODRIGUEZ", "YAZMIN RENDON", "Credito", "VALLE DEL CAUCA", "CARMELO II", "2017383", "16374", "REMISIÓN",
        Enumerable.Range(1, renglones).Select(i => new RenglonRemisionInputDto($"PJ-{i}", $"Ítem {i}", i, "Und.")).ToList());

    private static ArchivoSoporteEntrega Archivo(string nombre, string? qr) => Archivo(nombre, qr, "foto-" + nombre);

    private const string QrOk = "JIMACO-REM-29635";

    [Fact]
    public async Task Registrar_es_idempotente_y_respeta_fecha_de_inicio_y_renglones()
    {
        var s = Servicio();
        var r1 = await s.RegistrarAsync([Rem(128636, "29635"), Rem(100, "28000", Inicio.AddDays(-1)), Rem(101, "29653", renglones: 0)]);
        Assert.Equal(1, r1.Creadas);

        var r2 = await s.RegistrarAsync([Rem(128636, "29635")]);
        Assert.Equal(0, r2.Creadas);
        Assert.Equal(1, await db.Remisiones.CountAsync());

        var estado = await s.ObtenerEstadoSincronizacionAsync();
        Assert.Equal(Inicio, estado.FechaInicio);
        Assert.Equal([128636], estado.IdsConocidos);

        var rem = await db.Remisiones.Include(r => r.Renglones).SingleAsync();
        Assert.Equal("VALLE DEL CAUCA", rem.DireccionEntrega);
        Assert.Equal("2017383", rem.OrdenCompraCliente);
        Assert.Equal(2, rem.Renglones.Count);
        Assert.Equal(EstadoRemision.PorEntregar, rem.Estado);
    }

    [Fact]
    public async Task Sin_permiso_no_ve_ni_confirma()
    {
        var nadie = await UsuarioAsync("Sin permiso", false, false);
        var soloVe = await UsuarioAsync("Comercial", false, true);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Servicio().ListarAsync(nadie));

        Assert.Single(await Servicio().ListarAsync(soloVe));
        Assert.False((await Servicio().ObtenerAsync(id, soloVe)).PuedeGestionar);
        Assert.True((await Servicio().GenerarPdfAsync([id], soloVe)).Length > 1000); // ver también imprime

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Servicio().ConfirmarEntregaAsync(id, soloVe, null, [Archivo("f.jpg", null)]));
    }

    // ---- Sin IA configurada: manda el QR ----

    [Theory]
    [InlineData("JIMACO-REM-29635")]
    [InlineData("  jimaco-rem-29635 ")]
    public async Task Sin_IA_con_el_QR_correcto_queda_entregada(string qr)
    {
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        var detalle = await Servicio().ConfirmarEntregaAsync(id, logistica, "  Recibió Carmelo  ", [Archivo("firma.jpg", qr)]);

        Assert.Equal("Entregada", detalle.Estado);
        Assert.Equal("Coincide", detalle.VerificacionQR);
        Assert.Equal("Recibió Carmelo", detalle.ObservacionEntrega);
        Assert.Equal("Logistica", detalle.EntregadaPor);
        Assert.True(Assert.Single(detalle.Soportes).Valido);
    }

    [Theory]
    [InlineData("JIMACO-REM-29640")]
    [InlineData(null)]
    public async Task Soporte_incorrecto_no_entrega_ni_guarda_nada(string? qr)
    {
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("f.jpg", qr)]));

        Assert.Contains("29635", error.Message);
        var rem = await db.Remisiones.Include(r => r.Soportes).SingleAsync();
        Assert.Equal(EstadoRemision.PorEntregar, rem.Estado);
        Assert.Empty(rem.Soportes);
        almacenamiento.Verify(a => a.GuardarAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Basta_un_soporte_valido_y_se_guardan_todos_marcados()
    {
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        var detalle = await Servicio().ConfirmarEntregaAsync(id, logistica, null,
            [Archivo("pagina1.jpg", null), Archivo("pagina2.jpg", QrOk)]);

        Assert.Equal("Coincide", detalle.VerificacionQR);
        Assert.Equal([false, true], detalle.Soportes.Select(x => x.Valido));
    }

    // ---- Con IA: además del documento correcto, la firma del cliente ----

    [Fact]
    public async Task Con_IA_numero_y_firma_correctos_entrega_aunque_no_haya_QR()
    {
        IaVe("29635", firma: true);
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        var detalle = await Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("impresa-desde-WO.jpg", null)]);

        Assert.Equal("Entregada", detalle.Estado);
        var soporte = Assert.Single(detalle.Soportes);
        Assert.True(soporte.IaHayFirma);
        Assert.Equal("29635", soporte.IaNumeroLeido);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Con_IA_el_sello_del_cliente_vale_con_o_sin_firma(bool firma, bool sello)
    {
        IaVe("29635", firma, sello: sello);
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        var detalle = await Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("sellada.jpg", QrOk)]);

        Assert.Equal("Entregada", detalle.Estado);
        var soporte = Assert.Single(detalle.Soportes);
        Assert.True(soporte.IaHaySello);
        Assert.Equal(firma, soporte.IaHayFirma);
    }

    [Fact]
    public async Task Con_IA_sin_firma_no_entrega_aunque_el_QR_sea_correcto()
    {
        IaVe("29635", firma: false);
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("sin-firmar.jpg", QrOk)]));

        Assert.Contains("firma", error.Message);
        Assert.Equal(EstadoRemision.PorEntregar, (await db.Remisiones.SingleAsync()).Estado);
    }

    [Theory]
    [InlineData("29640", null, "remisión 29640")]
    [InlineData("29635", "JIMACO-REM-29640", "otra remisión")]
    [InlineData(null, null, "No se reconoce")]
    public async Task Con_IA_documento_de_otra_remision_no_entrega(string? numeroLeido, string? qr, string motivo)
    {
        IaVe(numeroLeido, firma: true);
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        var verif = await Servicio().VerificarSoporteAsync(id, logistica, Archivo("x.jpg", qr));
        Assert.False(verif.Valido);
        Assert.Contains(motivo, verif.Motivo);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("x.jpg", qr)]));
    }

    [Fact]
    public async Task Con_IA_que_falla_no_entrega_y_lo_explica()
    {
        ia.SetupGet(x => x.Activo).Returns(true);
        ia.Setup(x => x.VerificarAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoVerificacionIA.NoAnalizado("No se pudo revisar con IA en este momento."));
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("f.jpg", QrOk)]));
        Assert.Contains("No se pudo revisar con IA", error.Message);
    }

    [Fact]
    public async Task Verificar_y_luego_confirmar_el_mismo_archivo_llama_a_la_IA_una_sola_vez()
    {
        IaVe("29635", firma: true);
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        Assert.True((await Servicio().VerificarSoporteAsync(id, logistica, Archivo("f.jpg", QrOk))).Valido);
        await Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("f.jpg", QrOk)]);

        ia.Verify(x => x.VerificarAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task No_se_confirma_dos_veces_ni_sin_archivos()
    {
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().ConfirmarEntregaAsync(id, logistica, null, []));
        await Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("f.jpg", QrOk)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("g.jpg", QrOk)]));
    }

    [Fact]
    public async Task Tablero_trae_por_entregar_y_entregadas_y_marca_atrasadas()
    {
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635"), Rem(2, "29636"), Rem(3, "29637"), Rem(4, "29638")]);
        var ids = await db.Remisiones.OrderBy(r => r.Numero).Select(r => r.Id).ToListAsync();
        await Servicio().ConfirmarEntregaAsync(ids[0], logistica, null, [Archivo("a.jpg", "JIMACO-REM-29635")]);
        await Servicio().MarcarAnuladasAsync([4]);

        var recien = await Servicio().ListarAsync(logistica);
        Assert.Equal(["29635", "29636", "29637"], recien.Select(r => r.Numero).Order());
        Assert.All(recien, r => Assert.False(r.EnAlerta));
        Assert.Equal("Coincide", recien.Single(r => r.Numero == "29635").VerificacionQR);

        // 49 h después: la por entregar pasa a atrasada; las entregadas nunca.
        reloj.Ahora = reloj.Ahora.AddHours(49);
        var despues = await Servicio().ListarAsync(logistica);
        Assert.True(despues.Single(r => r.Numero == "29637").EnAlerta);
        Assert.False(despues.Single(r => r.Numero == "29635").EnAlerta);

        // Pasados 60 días, las entregadas ya no se traen (la por entregar sí).
        reloj.Ahora = reloj.Ahora.AddDays(61);
        Assert.Equal(["29636", "29637"], (await Servicio().ListarAsync(logistica)).Select(r => r.Numero).Order());
    }

    [Fact]
    public async Task Anular_no_toca_las_ya_entregadas()
    {
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635")]);
        var id = (await db.Remisiones.SingleAsync()).Id;
        await Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("a.jpg", QrOk)]);

        await Servicio().MarcarAnuladasAsync([1]);

        Assert.Equal(EstadoRemision.Entregada, (await db.Remisiones.SingleAsync()).Estado);
    }

    [Fact]
    public async Task Genera_el_PDF_de_varias_remisiones()
    {
        var logistica = await UsuarioAsync("Logistica", true, false);
        await Servicio().RegistrarAsync([Rem(1, "29635"), Rem(2, "29636")]);
        var ids = await db.Remisiones.Select(r => r.Id).ToListAsync();

        var pdf = await Servicio().GenerarPdfAsync(ids, logistica);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
