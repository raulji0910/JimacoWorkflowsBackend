using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

public class InstanciaDocumentoServiceTests
{
    private static AppDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opciones);
    }

    private static InstanciaDocumentoService CrearServicio(AppDbContext db) =>
        new(db, Mock.Of<IAlmacenamientoArchivos>(), Mock.Of<INotificacionService>(), TimeProvider.System);

    /// <summary>Siembra un tipo de documento con un flujo de 2 pasos: Paso 1 (rol Comercial), Paso 2 (rol Contable). Devuelve (usuarioEmisor, usuarioComercial, usuarioContable, tipoDocumento, paso1, paso2).</summary>
    private static async Task<(Usuario emisor, Usuario comercial, Usuario contable, TipoDocumento tipo, PasoFlujo paso1, PasoFlujo paso2)> SembrarFlujoDeDosPasosAsync(AppDbContext db)
    {
        var rolComercial = new Rol { Nombre = "Comercial" };
        var rolContable = new Rol { Nombre = "Contable" };
        db.AddRange(rolComercial, rolContable);

        var emisor = new Usuario { Nombre = "Emisor", Email = "emisor@test.local", PasswordHash = "x" };
        var comercial = new Usuario { Nombre = "Comercial", Email = "comercial@test.local", PasswordHash = "x" };
        var contable = new Usuario { Nombre = "Contable", Email = "contable@test.local", PasswordHash = "x" };
        db.AddRange(emisor, comercial, contable);
        await db.SaveChangesAsync();

        db.UsuarioRoles.AddRange(
            new UsuarioRol { UsuarioId = comercial.Id, RolId = rolComercial.Id },
            new UsuarioRol { UsuarioId = contable.Id, RolId = rolContable.Id });

        var tipo = new TipoDocumento { Nombre = "Orden de Compra", Activo = true };
        db.TiposDocumento.Add(tipo);
        await db.SaveChangesAsync();

        var paso1 = new PasoFlujo { Nombre = "Aprobación comercial", Orden = 1, PermiteDevolver = true, PermiteRechazar = false };
        var paso2 = new PasoFlujo { Nombre = "Registro contable", Orden = 2, PermiteDevolver = true, PermiteRechazar = true };
        var flujo = new DefinicionFlujo { Nombre = "Flujo OC", TipoDocumentoId = tipo.Id, Activo = true, Pasos = [paso1, paso2] };
        db.DefinicionesFlujo.Add(flujo);
        await db.SaveChangesAsync();

        db.PasoFlujoRoles.AddRange(
            new PasoFlujoRol { PasoFlujoId = paso1.Id, RolId = rolComercial.Id },
            new PasoFlujoRol { PasoFlujoId = paso2.Id, RolId = rolContable.Id });
        await db.SaveChangesAsync();

        return (emisor, comercial, contable, tipo, paso1, paso2);
    }

    [Fact]
    public async Task CrearAsync_EntraAlPrimerPasoDelFlujoActivo()
    {
        await using var db = CrearContexto();
        var (emisor, _, _, tipo, paso1, _) = await SembrarFlujoDeDosPasosAsync(db);
        var servicio = CrearServicio(db);

        var resultado = await servicio.CrearAsync(new CrearInstanciaDocumentoDto(tipo.Id, "OC-1", "Proveedor X", 1000, null, null), emisor.Id);

        Assert.Equal(EstadoInstanciaDocumento.EnProceso, resultado.Estado);
        Assert.Equal(paso1.Nombre, resultado.PasoActualNombre);
    }

    [Fact]
    public async Task EjecutarAccionAsync_Aprobar_AvanzaAlSiguientePaso()
    {
        await using var db = CrearContexto();
        var (emisor, comercial, _, tipo, _, paso2) = await SembrarFlujoDeDosPasosAsync(db);
        var servicio = CrearServicio(db);
        var instancia = await servicio.CrearAsync(new CrearInstanciaDocumentoDto(tipo.Id, "OC-1", null, null, null, null), emisor.Id);

        var resultado = await servicio.EjecutarAccionAsync(instancia.Id, comercial.Id, new EjecutarAccionDto(TipoAccion.Aprobado, null));

        Assert.Equal(EstadoInstanciaDocumento.EnProceso, resultado.Estado);
        Assert.Equal(paso2.Nombre, resultado.PasoActualNombre);
    }

    [Fact]
    public async Task EjecutarAccionAsync_AprobarEnElUltimoPaso_Completa()
    {
        await using var db = CrearContexto();
        var (emisor, comercial, contable, tipo, _, _) = await SembrarFlujoDeDosPasosAsync(db);
        var servicio = CrearServicio(db);
        var instancia = await servicio.CrearAsync(new CrearInstanciaDocumentoDto(tipo.Id, "OC-1", null, null, null, null), emisor.Id);
        await servicio.EjecutarAccionAsync(instancia.Id, comercial.Id, new EjecutarAccionDto(TipoAccion.Aprobado, null));

        var resultado = await servicio.EjecutarAccionAsync(instancia.Id, contable.Id, new EjecutarAccionDto(TipoAccion.Aprobado, null));

        Assert.Equal(EstadoInstanciaDocumento.Completado, resultado.Estado);
        Assert.Null(resultado.PasoActualId);
    }

    [Fact]
    public async Task EjecutarAccionAsync_Devolver_SinPasoDestinoConfigurado_QuedaDevueltoParaReenviar()
    {
        await using var db = CrearContexto();
        var (emisor, comercial, _, tipo, _, _) = await SembrarFlujoDeDosPasosAsync(db);
        var servicio = CrearServicio(db);
        var instancia = await servicio.CrearAsync(new CrearInstanciaDocumentoDto(tipo.Id, "OC-1", null, null, null, null), emisor.Id);

        var resultado = await servicio.EjecutarAccionAsync(instancia.Id, comercial.Id, new EjecutarAccionDto(TipoAccion.Devuelto, "Falta el NIT del proveedor"));

        Assert.Equal(EstadoInstanciaDocumento.Devuelto, resultado.Estado);
        Assert.Null(resultado.PasoActualId);
    }

    [Fact]
    public async Task ReenviarAsync_DespuesDeDevuelto_VuelveAEntrarPorElPrimerPaso()
    {
        await using var db = CrearContexto();
        var (emisor, comercial, _, tipo, paso1, _) = await SembrarFlujoDeDosPasosAsync(db);
        var servicio = CrearServicio(db);
        var instancia = await servicio.CrearAsync(new CrearInstanciaDocumentoDto(tipo.Id, "OC-1", null, null, null, null), emisor.Id);
        await servicio.EjecutarAccionAsync(instancia.Id, comercial.Id, new EjecutarAccionDto(TipoAccion.Devuelto, "Falta el NIT del proveedor"));

        var resultado = await servicio.ReenviarAsync(instancia.Id, emisor.Id);

        Assert.Equal(EstadoInstanciaDocumento.EnProceso, resultado.Estado);
        Assert.Equal(paso1.Nombre, resultado.PasoActualNombre);
    }

    [Fact]
    public async Task EjecutarAccionAsync_UsuarioSinRolHabilitadoEnElPaso_LanzaUnauthorized()
    {
        await using var db = CrearContexto();
        var (emisor, _, contable, tipo, _, _) = await SembrarFlujoDeDosPasosAsync(db);
        var servicio = CrearServicio(db);
        var instancia = await servicio.CrearAsync(new CrearInstanciaDocumentoDto(tipo.Id, "OC-1", null, null, null, null), emisor.Id);

        // El paso actual (1) requiere rol Comercial; "contable" no lo tiene.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            servicio.EjecutarAccionAsync(instancia.Id, contable.Id, new EjecutarAccionDto(TipoAccion.Aprobado, null)));
    }

    [Fact]
    public async Task EjecutarAccionAsync_Devolver_SinComentario_LanzaExcepcion()
    {
        await using var db = CrearContexto();
        var (emisor, comercial, _, tipo, _, _) = await SembrarFlujoDeDosPasosAsync(db);
        var servicio = CrearServicio(db);
        var instancia = await servicio.CrearAsync(new CrearInstanciaDocumentoDto(tipo.Id, "OC-1", null, null, null, null), emisor.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.EjecutarAccionAsync(instancia.Id, comercial.Id, new EjecutarAccionDto(TipoAccion.Devuelto, null)));
    }

    [Fact]
    public async Task EjecutarAccionAsync_Rechazar_EnPasoQueNoLoPermite_LanzaExcepcion()
    {
        await using var db = CrearContexto();
        var (emisor, comercial, _, tipo, _, _) = await SembrarFlujoDeDosPasosAsync(db);
        var servicio = CrearServicio(db);
        var instancia = await servicio.CrearAsync(new CrearInstanciaDocumentoDto(tipo.Id, "OC-1", null, null, null, null), emisor.Id);

        // Paso 1 tiene PermiteRechazar = false.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.EjecutarAccionAsync(instancia.Id, comercial.Id, new EjecutarAccionDto(TipoAccion.Rechazado, "motivo")));
    }
}
