using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Jimaco.Aprobaciones.Negocio.Utilidades;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>Cotizaciones del portal que el Sincronizador crea en World Office (2026-11-25).</summary>
public class CotizacionServiceTests
{
    private const int Laura = 1;
    private const int Yazmin = 2;
    private const int SinPermiso = 3;
    private const int SinUsuarioWO = 4;

    private sealed class Reloj(DateTime ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(ahora, TimeSpan.Zero);
    }

    private static AppDbContext CrearContexto()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.AddRange(
            new Rol { Id = 900, Nombre = "Crea cotizaciones", Activo = true, PuedeCrearCotizaciones = true },
            new Rol { Id = 901, Nombre = "Logística", Activo = true });
        db.Usuarios.AddRange(
            new Usuario { Id = Laura, Nombre = "Laura", Email = "laura@x.com", PasswordHash = "x", UsuarioWO = "LAURA", IdTerceroWO = 9602 },
            new Usuario { Id = Yazmin, Nombre = "Yazmín", Email = "yaz@x.com", PasswordHash = "x", UsuarioWO = "YAZMIN", IdTerceroWO = 9603 },
            new Usuario { Id = SinPermiso, Nombre = "Pedro", Email = "pedro@x.com", PasswordHash = "x" },
            new Usuario { Id = SinUsuarioWO, Nombre = "Nueva", Email = "nueva@x.com", PasswordHash = "x" });
        db.UsuarioRoles.AddRange(
            new UsuarioRol { UsuarioId = Laura, RolId = 900 },
            new UsuarioRol { UsuarioId = Yazmin, RolId = 900 },
            new UsuarioRol { UsuarioId = SinPermiso, RolId = 901 },
            new UsuarioRol { UsuarioId = SinUsuarioWO, RolId = 900 });

        db.TercerosWO.Add(new TerceroWO { IdTerceroWO = 9044, Identificacion = "900123456", Nombre = "VENTUS COLOMBIA S.A.S." });
        db.DireccionesTerceroWO.AddRange(
            new DireccionTerceroWO { IdDireccionWO = 398, IdTerceroWO = 9044, Direccion = "CL 100 # 10-20", Ciudad = "BOGOTA", Principal = true },
            new DireccionTerceroWO { IdDireccionWO = 500, IdTerceroWO = 1, Direccion = "OTRA" });
        db.FormasPagoWO.Add(new FormaPagoWO { IdFormaPagoWO = 2, Nombre = "Credito" });
        db.BodegasWO.AddRange(new BodegaWO { IdBodegaWO = "Uno", Nombre = "Principal", Activa = true }, new BodegaWO { IdBodegaWO = "Dos", Nombre = "Garantia", Activa = false });
        db.ProductosWO.AddRange(
            new ProductoWO { IdInventarioWO = 3499, Codigo = "PJ-624", Descripcion = "AZ TAMAÑO CARTA", UnidadMedida = "Und.", Iva = 0.19m, IdClasificacion = 2, Activo = true },
            new ProductoWO { IdInventarioWO = 3692, Codigo = "PJ-817", Descripcion = "BOTIQUIN TIPO B", UnidadMedida = "Und.", Iva = 0m, IdClasificacion = 2, Activo = true },
            new ProductoWO { IdInventarioWO = 1, Codigo = "VIEJO", Descripcion = "DESCONTINUADO", UnidadMedida = "Und.", Iva = 0.19m, Activo = false });
        db.CamposPersonalizadosWO.AddRange(
            new CampoPersonalizadoWO { TipoDocumento = "COT", Numero = 1, Etiqueta = "CONTACTO", Activo = true },
            new CampoPersonalizadoWO { TipoDocumento = "COT", Numero = 2, Etiqueta = "2 OBSERVACIONES", Activo = true },
            new CampoPersonalizadoWO { TipoDocumento = "COT", Numero = 8, Etiqueta = "Personalizado 8", Activo = false });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static CotizacionService Servicio(AppDbContext db) => new(db, new Reloj(new DateTime(2026, 11, 25, 20, 0, 0)));

    private static GuardarCotizacionDto Cotizacion(IReadOnlyDictionary<int, string>? masDatos = null, params CotizacionRenglonInputDto[] renglones) =>
        new(9044, 398, 2, null, 30, masDatos,
            renglones.Length > 0 ? renglones : [new(3499, "Uno", 10, 4044, "8 dias habiles"), new(3692, "Uno", 20, 456250, null)]);

    [Fact]
    public void TotalesWO_RedondeaElIvaAPesosEnCadaRenglon_ComoLaCOT16915()
    {
        (decimal, decimal, decimal)[] renglones =
        [
            (10, 4044, .19m), (20, 456250, 0), (30, 77500, .19m), (1, 325000, .19m), (10, 17500, .19m), (7, 262605, .19m), (25, 4412, .19m),
            (5, 5672, .19m), (50, 7305, .19m), (70, 38750, .19m), (50, 38750, .19m), (5, 38750, .19m), (200, 116281, .19m), (3, 66250, .19m),
            (10, 15000, .19m), (840, 3021, .19m), (600, 3021, .19m), (2, 5252, .19m), (20, 12500, .19m), (50, 27048, .19m), (10, 112500, .19m),
            (200, 9100, .19m), (10, 68750, .19m), (26, 296875, .19m), (3, 65469, .19m), (30, 11875, .19m), (5, 400575, .19m), (30, 8500, .19m)
        ];

        var totales = TotalesWO.Documento(renglones);

        Assert.Equal(62_906_211m, totales.Subtotal);
        Assert.Equal(10_218_433m, totales.Iva);
        Assert.Equal(73_124_644m, totales.Total);
    }

    [Fact]
    public async Task Crear_ResuelveClienteProductosYTotalesDesdeElCatalogo()
    {
        using var db = CrearContexto();

        var cotizacion = await Servicio(db).CrearAsync(Cotizacion(new Dictionary<int, string> { [1] = " Andrea Ruiz ", [2] = "" }), Laura);

        Assert.Equal(EstadoCotizacion.Borrador, cotizacion.Estado);
        Assert.Equal("VENTUS COLOMBIA S.A.S.", cotizacion.ClienteNombre);
        Assert.Equal("CL 100 # 10-20 - BOGOTA", cotizacion.DireccionTexto);
        Assert.Equal("Credito", cotizacion.FormaPagoNombre);
        Assert.Equal("COTIZACIÓN", cotizacion.Concepto);
        Assert.Equal("Andrea Ruiz", Assert.Single(cotizacion.MasDatos).Value);
        Assert.Equal(["PJ-624", "PJ-817"], cotizacion.Renglones.Select(r => r.Codigo));
        Assert.Equal(0.19m, cotizacion.Renglones[0].Iva);
        Assert.Equal(40_440m + 9_125_000m, cotizacion.Subtotal);
        Assert.Equal(7_684m, cotizacion.Iva);
        Assert.Equal(30m, cotizacion.TotalCantidades);
    }

    [Fact]
    public async Task MasDatos_UnCampoQueWODejoDeUsarSeDescartaSinError()
    {
        using var db = CrearContexto();

        var cotizacion = await Servicio(db).CrearAsync(Cotizacion(new Dictionary<int, string> { [8] = "ya no se usa", [1] = "Andrea" }), Laura);

        Assert.Equal("Andrea", Assert.Single(cotizacion.MasDatos).Value);
    }

    [Fact]
    public async Task SucursalDeOtroCliente_BodegaInactiva_SeRechazan()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.CrearAsync(Cotizacion() with { IdDireccion = 500 }, Laura));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.CrearAsync(Cotizacion(null, new CotizacionRenglonInputDto(3499, "Dos", 1, 1, null)), Laura));
    }

    [Fact]
    public async Task Enviar_DejaPorCrearConFechaColombiaYElaboradoPor_YElSincronizadorLaRecibe()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        var borrador = await servicio.CrearAsync(Cotizacion(new Dictionary<int, string> { [1] = "Andrea" }), Laura);

        var enviada = await servicio.EnviarAsync(borrador.Id, Laura);

        Assert.Equal(EstadoCotizacion.PorCrear, enviada.Estado);
        Assert.Equal(new DateOnly(2026, 11, 25), enviada.FechaDocumento);
        var pendiente = Assert.Single(await servicio.ListarPendientesCreacionAsync());
        Assert.Equal(9602, pendiente.IdTerceroElabora);
        Assert.Equal("LAURA", pendiente.UsuarioWO);
        Assert.Equal(9044, pendiente.IdTercero);
        Assert.Equal(398, pendiente.IdDireccion);
        Assert.Equal("Andrea", pendiente.MasDatos[1]);
        Assert.Equal(2, pendiente.Renglones.Count);
        Assert.Equal("8 dias habiles", pendiente.Renglones[0].Nota);
        Assert.Equal(enviada.Total, pendiente.Total);

        // Ya enviada no se edita.
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActualizarAsync(borrador.Id, Cotizacion(), Laura));
    }

    [Fact]
    public async Task Enviar_ValidaClienteProductosActivosYCantidades()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        var borrador = await servicio.CrearAsync(Cotizacion(null,
            new CotizacionRenglonInputDto(1, "Uno", 1, 100, null),
            new CotizacionRenglonInputDto(3499, "Uno", 0, 100, null)) with { IdTercero = null, IdFormaPago = null }, Laura);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EnviarAsync(borrador.Id, Laura));

        Assert.Contains("Falta el cliente", error.Message);
        Assert.Contains("Falta la forma de pago", error.Message);
        Assert.Contains("VIEJO", error.Message);
        Assert.Contains("cantidad debe ser mayor que cero", error.Message);
    }

    [Fact]
    public async Task Enviar_SinUsuarioDeWO_PideConfigurarlo()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        var borrador = await servicio.CrearAsync(Cotizacion(), SinUsuarioWO);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EnviarAsync(borrador.Id, SinUsuarioWO));
        Assert.Contains("elaborado por", error.Message);
    }

    [Fact]
    public async Task Creada_GuardaNumeroDeWO_YConfirmarDosVecesNoFalla()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        var borrador = await servicio.CrearAsync(Cotizacion(), Laura);
        await servicio.EnviarAsync(borrador.Id, Laura);

        await servicio.ConfirmarCreadaAsync(borrador.Id, new CotizacionCreadaDto("PRUEB", 3, 130500));
        await servicio.ConfirmarCreadaAsync(borrador.Id, new CotizacionCreadaDto("PRUEB", 3, 130500));

        var creada = await servicio.ObtenerAsync(borrador.Id, Laura);
        Assert.Equal(EstadoCotizacion.Creada, creada.Estado);
        Assert.Equal("PRUEB", creada.PrefijoWO);
        Assert.Equal(3m, creada.NumeroWO);
        Assert.Equal(new DateOnly(2026, 11, 25), creada.FechaDocumento);
        Assert.Empty(await servicio.ListarPendientesCreacionAsync());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ActualizarAsync(borrador.Id, Cotizacion(), Laura));
        Assert.Contains("PRUEB 3", error.Message);
    }

    [Fact]
    public async Task Creada_LaFechaEsLaDelDiaEnQueWOLaCreo()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        var borrador = await servicio.CrearAsync(Cotizacion(), Laura);
        await servicio.EnviarAsync(borrador.Id, Laura); // enviada el 25-nov

        await servicio.ConfirmarCreadaAsync(borrador.Id, new CotizacionCreadaDto("PRUEB", 3, 130186, new DateTime(2026, 11, 26)));

        Assert.Equal(new DateOnly(2026, 11, 26), (await servicio.ObtenerAsync(borrador.Id, Laura)).FechaDocumento);
    }

    [Fact]
    public async Task Error_SeCorrigeYSeReenvia()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        var borrador = await servicio.CrearAsync(Cotizacion(), Laura);
        await servicio.EnviarAsync(borrador.Id, Laura);

        await servicio.ReportarErrorAsync(borrador.Id, "El producto PJ-624 está inactivo en World Office.");
        var conError = await servicio.ObtenerAsync(borrador.Id, Laura);
        Assert.Equal(EstadoCotizacion.Error, conError.Estado);
        Assert.Contains("PJ-624", conError.MensajeError);

        await servicio.ActualizarAsync(borrador.Id, Cotizacion(null, new CotizacionRenglonInputDto(3692, "Uno", 1, 1000, null)), Laura);
        var reenviada = await servicio.EnviarAsync(borrador.Id, Laura);
        Assert.Equal(EstadoCotizacion.PorCrear, reenviada.Estado);
        Assert.Null(reenviada.MensajeError);
    }

    [Fact]
    public async Task CadaQuienVeLasSuyas_YSinRolNada()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        var deLaura = await servicio.CrearAsync(Cotizacion(), Laura);

        Assert.Empty(await servicio.ListarAsync(Yazmin));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.ObtenerAsync(deLaura.Id, Yazmin));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.ListarAsync(SinPermiso));
        Assert.Single(await servicio.ListarAsync(Laura));
    }

    [Fact]
    public async Task Copiar_CreaUnBorradorNuevoConElMismoContenido()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        var original = await servicio.CrearAsync(Cotizacion(new Dictionary<int, string> { [2] = "MALAMBO" }), Laura);
        await servicio.EnviarAsync(original.Id, Laura);
        await servicio.ConfirmarCreadaAsync(original.Id, new CotizacionCreadaDto("PRUEB", 3, 1));

        var copia = await servicio.CopiarAsync(original.Id, Laura);

        Assert.NotEqual(original.Id, copia.Id);
        Assert.Equal(EstadoCotizacion.Borrador, copia.Estado);
        Assert.Null(copia.NumeroWO);
        Assert.Equal("MALAMBO", copia.MasDatos[2]);
        Assert.Equal(original.Total, copia.Total);
        Assert.Equal(2, copia.Renglones.Count);
    }

    [Fact]
    public async Task SoloSeBorranBorradores()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        var borrador = await servicio.CrearAsync(Cotizacion(), Laura);
        var enviada = await servicio.CrearAsync(Cotizacion(), Laura);
        await servicio.EnviarAsync(enviada.Id, Laura);

        await servicio.EliminarAsync(borrador.Id, Laura);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EliminarAsync(enviada.Id, Laura));
        Assert.Single(await servicio.ListarAsync(Laura));
    }
}
