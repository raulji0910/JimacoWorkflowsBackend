using System.Text;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>
/// 2026-11-13: Entregas fase 2 — correo al emisor al entregar, documentos para facturar por tipo
/// (proforma solo PARES/VERANO), aviso de IA que no bloquea, envío a contabilidad y devolución.
/// </summary>
public class RemisionFacturacionTests
{
    private const string Pares = "900268747";
    private const string Verano = "900331322";

    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Mock<IAlmacenamientoArchivos> almacenamiento = new();
    private readonly Mock<IVerificadorSoporteEntrega> ia = new();
    private readonly Mock<INotificacionService> notificaciones = new();
    private readonly MemoryCache cache = new(new MemoryCacheOptions());

    public RemisionFacturacionTests()
    {
        almacenamiento.Setup(a => a.GuardarAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string nombre, Stream _, CancellationToken _) => "adjuntos/" + nombre);
        db.ConfiguracionesEntregas.Add(new ConfiguracionEntregas { Id = 1, FechaInicio = new DateTime(2026, 11, 12) });
        // Los mismos que siembra la migración (InMemory no aplica HasData sin EnsureCreated).
        db.TiposDocumentoFacturacion.AddRange(
            new TipoDocumentoFacturacion { Id = 1, Nombre = "Orden de Compra", Orden = 1 },
            new TipoDocumentoFacturacion { Id = 2, Nombre = "Entrada de almacén", Orden = 2 },
            new TipoDocumentoFacturacion { Id = 3, Nombre = "Proforma", Orden = 3, NitsClientes = $"{Pares};{Verano}" });
        db.SaveChanges();
    }

    private RemisionService Servicio() => new(db, almacenamiento.Object, new RemisionPdfGenerador(),
        Mock.Of<IConfiguration>(), TimeProvider.System, ia.Object, cache, notificaciones.Object,
        NullLogger<RemisionService>.Instance);

    private async Task<int> UsuarioAsync(string nombre, string? usuarioWO = null, bool gestiona = false, bool ve = false,
        bool documenta = false, bool factura = false)
    {
        var rol = new Rol
        {
            Nombre = "Rol " + nombre, Activo = true, PuedeGestionarEntregas = gestiona, PuedeVerEntregas = ve,
            PuedeDocumentarEntregas = documenta, PuedeFacturarEntregas = factura
        };
        var u = new Usuario { Nombre = nombre, Email = nombre.Replace(" ", "") + "@jimaco.local", PasswordHash = "x", UsuarioWO = usuarioWO };
        db.AddRange(rol, u);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = u.Id, RolId = rol.Id });
        await db.SaveChangesAsync();
        return u.Id;
    }

    private static RemisionInputDto Rem(int idWO, string numero, string nit = "900123", string elaboradoPor = "YRENDON") => new(
        idWO, "REM", numero, new DateTime(2026, 11, 13), "CLIENTE " + nit, nit, "CRA 9", "Bogota D.C.", "601",
        "YAZMIN RENDON RODRIGUEZ", elaboradoPor, "Credito", "VALLE", "CARMELO II", "2017383", "16374", null,
        [new RenglonRemisionInputDto("PJ-1", "Ítem 1", 1, "Und.")]);

    private static ArchivoSoporteEntrega Archivo(string nombre, string? qr = null) =>
        new(nombre, "application/pdf", new MemoryStream(Encoding.UTF8.GetBytes("contenido-" + nombre)), 20, qr);

    /// <summary>Remisión registrada y entregada por Logística (con el QR correcto, sin IA).</summary>
    private async Task<(int Id, int Logistica)> EntregadaAsync(string numero = "29700", string nit = "900123", string elaboradoPor = "YRENDON")
    {
        var logistica = await UsuarioAsync("Logistica " + numero, gestiona: true);
        await Servicio().RegistrarAsync([Rem(int.Parse(numero), numero, nit, elaboradoPor)]);
        var id = (await db.Remisiones.SingleAsync(r => r.Numero == numero)).Id;
        await Servicio().ConfirmarEntregaAsync(id, logistica, null, [Archivo("firmada.jpg", $"JIMACO-REM-{numero}")]);
        return (id, logistica);
    }

    private void EncenderIA(ResultadoDocumentoFacturacionIA resultado)
    {
        db.ConfiguracionesEntregas.Single().VerificacionIAActiva = true;
        db.SaveChanges();
        ia.SetupGet(x => x.Activo).Returns(true);
        ia.Setup(x => x.VerificarDocumentoAsync(It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<ContextoDocumentoFacturacion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultado);
    }

    [Fact]
    public async Task Al_entregar_le_llega_el_correo_al_emisor_y_queda_pendiente_de_documentos()
    {
        var emisor = await UsuarioAsync("Yazmin", usuarioWO: "yrendon", documenta: true);
        var (id, logistica) = await EntregadaAsync();

        notificaciones.Verify(n => n.NotificarHtmlAsync(null, emisor, It.Is<string>(a => a.Contains("29700")),
            It.Is<string>(c => c.Contains("Orden de Compra") && c.Contains("Entrada de almacén") && !c.Contains("Proforma")),
            $"/entregas/{id}", It.IsAny<CancellationToken>()), Times.Once);
        notificaciones.Verify(n => n.NotificarHtmlAsync(null, logistica, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        var detalle = await Servicio().ObtenerAsync(id, emisor);
        Assert.Equal("PendienteDocumentos", detalle.EstadoFacturacion);
        Assert.Equal("Yazmin", detalle.Emisor);
        Assert.True(detalle.PuedeAdjuntarDocumentos);
        Assert.Equal(["Orden de Compra", "Entrada de almacén"], detalle.DocumentosRequeridos!.Select(d => d.Nombre));
    }

    [Theory]
    [InlineData(Pares)]
    [InlineData("9002687479")]   // con dígito de verificación pegado
    [InlineData("900268747-9")]
    [InlineData(Verano)]
    public async Task La_proforma_solo_se_pide_a_PARES_y_VERANO(string nit)
    {
        var emisor = await UsuarioAsync("Yazmin", usuarioWO: "YRENDON", documenta: true);
        var (id, _) = await EntregadaAsync(nit: nit);

        var requeridos = (await Servicio().ObtenerAsync(id, emisor)).DocumentosRequeridos!;
        Assert.Contains(requeridos, d => d.Nombre == "Proforma");
    }

    [Fact]
    public async Task Otro_cliente_no_puede_adjuntar_proforma()
    {
        var emisor = await UsuarioAsync("Yazmin", usuarioWO: "YRENDON", documenta: true);
        var (id, _) = await EntregadaAsync(nit: "900268740"); // parecido a PARES, pero no

        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().AdjuntarDocumentoAsync(id, emisor, 3, Archivo("proforma.pdf")));
    }

    [Fact]
    public async Task El_emisor_solo_ve_sus_remisiones_y_otro_vendedor_no_puede_adjuntar()
    {
        var yazmin = await UsuarioAsync("Yazmin", usuarioWO: "YRENDON", documenta: true);
        var laura = await UsuarioAsync("Laura", usuarioWO: "LHERNANDEZ", documenta: true);
        var (id, _) = await EntregadaAsync("29700");
        await EntregadaAsync("29701", elaboradoPor: "LHERNANDEZ");

        Assert.Equal(["29700"], (await Servicio().ListarAsync(yazmin)).Select(r => r.Numero));
        Assert.True((await Servicio().ListarAsync(yazmin)).Single().EsMia);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Servicio().ObtenerAsync(id, laura));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Servicio().AdjuntarDocumentoAsync(id, laura, 1, Archivo("oc.pdf")));
    }

    [Fact]
    public async Task Flujo_completo_adjuntar_enviar_devolver_y_reenviar()
    {
        var emisor = await UsuarioAsync("Yazmin", usuarioWO: "YRENDON", documenta: true);
        var contable = await UsuarioAsync("Contable", factura: true);
        var (id, _) = await EntregadaAsync();
        var s = Servicio();

        // Sin la entrada de almacén no se puede enviar.
        await s.AdjuntarDocumentoAsync(id, emisor, 1, Archivo("oc.pdf"));
        Assert.Equal(1, (await s.ListarAsync(emisor)).Single().DocumentosFaltantes);
        var falta = await Assert.ThrowsAsync<InvalidOperationException>(() => s.EnviarAFacturacionAsync(id, emisor, null));
        Assert.Contains("Entrada de almacén", falta.Message);

        await s.AdjuntarDocumentoAsync(id, emisor, 2, Archivo("entrada.pdf"));
        var enviada = await s.EnviarAFacturacionAsync(id, emisor, "  Todo listo  ");
        Assert.Equal("EnFacturacion", enviada.EstadoFacturacion);
        Assert.False(enviada.PuedeAdjuntarDocumentos);
        notificaciones.Verify(n => n.NotificarHtmlAsync(null, contable, It.Is<string>(a => a.Contains("lista para facturar")),
            It.Is<string>(c => c.Contains("oc.pdf") && c.Contains("Todo listo")), $"/entregas/{id}", It.IsAny<CancellationToken>()), Times.Once);

        // En contabilidad el emisor ya no la toca; el contable ve y puede devolver.
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.AdjuntarDocumentoAsync(id, emisor, 1, Archivo("otra.pdf")));
        Assert.True((await s.ObtenerAsync(id, contable)).PuedeDevolver);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.DevolverAsync(id, contable, "   "));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.DevolverAsync(id, emisor, "yo misma"));

        var devuelta = await s.DevolverAsync(id, contable, "La OC no es de este pedido");
        Assert.Equal("Devuelta", devuelta.EstadoFacturacion);
        notificaciones.Verify(n => n.NotificarHtmlAsync(null, emisor, It.Is<string>(a => a.Contains("devuelta")),
            It.Is<string>(c => c.Contains("La OC no es de este pedido")), $"/entregas/{id}", It.IsAny<CancellationToken>()), Times.Once);

        // El emisor corrige (quita y vuelve a subir la OC) y la reenvía.
        var oc = (await s.ObtenerAsync(id, emisor)).Soportes.Single(x => x.NombreArchivo == "oc.pdf");
        Assert.True(oc.PuedeEliminar);
        await s.EliminarDocumentoAsync(id, oc.Id, emisor);
        await s.AdjuntarDocumentoAsync(id, emisor, 1, Archivo("oc-correcta.pdf"));
        var reenviada = await s.EnviarAFacturacionAsync(id, emisor, null);

        Assert.Equal("EnFacturacion", reenviada.EstadoFacturacion);
        Assert.Equal(["EnviadaAFacturacion", "Devuelta", "EnviadaAFacturacion"], reenviada.Historial!.Select(h => h.Accion));
        Assert.Equal("Todo listo", reenviada.Historial![0].Comentario);
    }

    [Fact]
    public async Task Con_IA_un_documento_equivocado_queda_con_aviso_pero_no_bloquea()
    {
        var emisor = await UsuarioAsync("Yazmin", usuarioWO: "YRENDON", documenta: true);
        await UsuarioAsync("Contable", factura: true);
        var (id, _) = await EntregadaAsync();

        EncenderIA(new ResultadoDocumentoFacturacionIA(true, false, "Factura", true, null, true, "Es una factura de venta.", null));
        var detalle = await Servicio().AdjuntarDocumentoAsync(id, emisor, 1, Archivo("factura.pdf"));
        var doc = detalle.Soportes.Single(s => s.Tipo == "DocumentoFacturacion");
        Assert.False(doc.Valido);
        Assert.Contains("parece Factura", doc.Aviso);
        Assert.Equal("Orden de Compra", doc.TipoDocumentoFacturacion);

        EncenderIA(new ResultadoDocumentoFacturacionIA(true, true, "Entrada de almacén", true, "OC-2017383", true, "ok", null));
        var ok = (await Servicio().AdjuntarDocumentoAsync(id, emisor, 2, Archivo("entrada.pdf")))
            .Soportes.Single(s => s.NombreArchivo == "entrada.pdf");
        Assert.True(ok.Valido);
        Assert.Null(ok.Aviso);

        // Avisar sin bloquear: se puede enviar igual.
        Assert.Equal("EnFacturacion", (await Servicio().EnviarAFacturacionAsync(id, emisor, "La OC la mandó así el cliente")).EstadoFacturacion);
        Assert.Equal(1, (await Servicio().ListarAsync(emisor)).Single().DocumentosConAviso);
    }

    [Theory]
    [InlineData("2017999", "no coincide")]
    [InlineData(null, null)]
    public async Task Con_IA_avisa_si_el_numero_de_OC_no_es_el_de_la_remision(string? ocLeida, string? aviso)
    {
        var emisor = await UsuarioAsync("Yazmin", usuarioWO: "YRENDON", documenta: true);
        var (id, _) = await EntregadaAsync();
        EncenderIA(new ResultadoDocumentoFacturacionIA(true, true, "Orden de compra", true, ocLeida, true, "OC", null));

        var doc = (await Servicio().AdjuntarDocumentoAsync(id, emisor, 1, Archivo("oc.pdf"))).Soportes.Single(s => s.NombreArchivo == "oc.pdf");

        if (aviso is null) Assert.Null(doc.Aviso);
        else Assert.Contains(aviso, doc.Aviso);
    }

    [Fact]
    public async Task Sin_emisor_configurado_no_falla_la_entrega()
    {
        var (id, logistica) = await EntregadaAsync(elaboradoPor: "NADIE");

        var detalle = await Servicio().ObtenerAsync(id, logistica);
        Assert.Equal("Entregada", detalle.Estado);
        Assert.Null(detalle.Emisor);
        notificaciones.Verify(n => n.NotificarHtmlAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task El_admin_crea_un_tipo_para_un_cliente_con_NIT_normalizado()
    {
        var tipo = await Servicio().GuardarTipoDocumentoAsync(null, new GuardarTipoDocumentoFacturacionDto(
            "Certificado de calidad", null, true, "800.111.222-3, 800111222 ; 900555666", 4, true));

        Assert.Equal("800111222;900555666", tipo.NitsClientes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().GuardarTipoDocumentoAsync(null,
            new GuardarTipoDocumentoFacturacionDto("Proforma", null, true, null, 9, true)));
    }
}
