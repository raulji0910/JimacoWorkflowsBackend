using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>Catálogo de WO que sube el Sincronizador --documentos y consulta la pantalla de Cotización (2026-11-25).</summary>
public class CatalogoWOServiceTests
{
    private const int Vendedora = 1;
    private const int SinPermiso = 2;

    private static AppDbContext CrearContexto()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.AddRange(
            new Rol { Id = 900, Nombre = "Crea cotizaciones", Activo = true, PuedeCrearCotizaciones = true },
            new Rol { Id = 901, Nombre = "Logística", Activo = true });
        db.Usuarios.AddRange(
            new Usuario { Id = Vendedora, Nombre = "Laura", Email = "laura@x.com", PasswordHash = "x" },
            new Usuario { Id = SinPermiso, Nombre = "Pedro", Email = "pedro@x.com", PasswordHash = "x" });
        db.UsuarioRoles.AddRange(
            new UsuarioRol { UsuarioId = Vendedora, RolId = 900 },
            new UsuarioRol { UsuarioId = SinPermiso, RolId = 901 });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static CatalogoWOService Servicio(AppDbContext db) => new(db, TimeProvider.System);

    private static ProductoWOInputDto Producto(int id, string codigo, string descripcion, long version, bool activo = true, decimal iva = 0.19m) =>
        new(id, codigo, descripcion, "Und.", iva, 2, 0m, activo, version);

    private static CatalogosWOInputDto Catalogos(params DireccionTerceroWOInputDto[] direcciones) => new(
        [new BodegaWOInputDto("Uno", "Principal", true), new BodegaWOInputDto("Tres", "Cartagena", true), new BodegaWOInputDto("Dos", "Garantia", false)],
        direcciones,
        [new FormaPagoWOInputDto(2, "Credito"), new FormaPagoWOInputDto(1, "Contado")],
        [new CampoPersonalizadoWOInputDto("COT", 1, "CONTACTO", true), new CampoPersonalizadoWOInputDto("COT", 3, "3 DATOS DE ENTREGA", false),
         new CampoPersonalizadoWOInputDto("COT", 9, "CUSTOMER PO", true), new CampoPersonalizadoWOInputDto("OC", 4, "OBSERVACIONES", true)]);

    [Fact]
    public async Task Productos_SeActualizanIncrementalYGuardanLaMayorVersion()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);

        await servicio.GuardarProductosAsync([Producto(3499, "PJ-624", "AZ TAMAÑO CARTA", 100), Producto(3692, "PJ-817", "BOTIQUIN TIPO B", 120)]);
        db.ChangeTracker.Clear();
        // Segunda corrida: solo lo que cambió (el 3499 se inactivó en WO) — el resto no se toca.
        await servicio.GuardarProductosAsync([Producto(3499, "PJ-624", "AZ TAMAÑO CARTA OFICIO", 150, activo: false)]);
        db.ChangeTracker.Clear();

        var productos = await db.ProductosWO.OrderBy(p => p.IdInventarioWO).ToListAsync();
        Assert.Equal(2, productos.Count);
        Assert.Equal("AZ TAMAÑO CARTA OFICIO", productos[0].Descripcion);
        Assert.False(productos[0].Activo);
        Assert.True(productos[1].Activo);
        Assert.Equal(150, (await servicio.ObtenerEstadoAsync()).UltimaVersionProductos);
    }

    [Fact]
    public async Task Existencias_SonUnaFotoQueReemplazaLaAnterior()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        await servicio.GuardarExistenciasAsync([new(3499, "Uno", 10m), new(3499, "Tres", 4m), new(3692, "Uno", 1m)]);
        db.ChangeTracker.Clear();

        await servicio.GuardarExistenciasAsync([new(3499, "Uno", 7m)]);
        db.ChangeTracker.Clear();

        var existencia = Assert.Single(await db.ExistenciasWO.ToListAsync());
        Assert.Equal(7m, existencia.Existencia);
        Assert.NotNull((await servicio.ObtenerEstadoAsync()).FechaExistencias);
    }

    [Fact]
    public async Task BuscarProductos_TodasLasPalabrasConExistenciasPorBodega_YSoloActivos()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        await servicio.GuardarCatalogosAsync(Catalogos());
        await servicio.GuardarProductosAsync([
            Producto(3499, "PJ-624", "AZ TAMAÑO CARTA", 1),
            Producto(3500, "PJ-625", "AZ TAMAÑO OFICIO", 2),
            Producto(3501, "PJ-626", "AZ CARTA DESCONTINUADA", 3, activo: false)]);
        await servicio.GuardarExistenciasAsync([new(3499, "Tres", 4m), new(3499, "Uno", 10m)]);
        db.ChangeTracker.Clear();

        var resultado = await servicio.BuscarProductosAsync("carta az", Vendedora);

        var producto = Assert.Single(resultado);
        Assert.Equal("PJ-624", producto.Codigo);
        Assert.Equal(14m, producto.ExistenciaTotal);
        Assert.Equal(["Principal", "Cartagena"], producto.Existencias.Select(e => e.Bodega));
    }

    [Fact]
    public async Task CatalogoCotizacion_SoloBodegasActivasYCamposEnUsoDeLaCOT()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);
        await servicio.GuardarCatalogosAsync(Catalogos(new DireccionTerceroWOInputDto(398, 9044, "CL 1", null, "BOGOTA", true)));
        // Segunda foto: reemplaza, no duplica.
        await servicio.GuardarCatalogosAsync(Catalogos(new DireccionTerceroWOInputDto(398, 9044, "CL 2", null, "BOGOTA", true)));
        db.ChangeTracker.Clear();

        var catalogo = await servicio.ObtenerCatalogoCotizacionAsync(Vendedora);

        Assert.Equal(["Cartagena", "Principal"], catalogo.Bodegas.Select(b => b.Nombre));
        Assert.Equal(["CONTACTO", "CUSTOMER PO"], catalogo.CamposMasDatos.Select(c => c.Etiqueta));
        Assert.Equal(2, catalogo.FormasPago.Count);
        var direccion = Assert.Single(await servicio.ListarDireccionesAsync(9044, Vendedora));
        Assert.Equal("CL 2", direccion.Direccion);
    }

    [Fact]
    public async Task BuscarClientes_PorNombreOIdentificacion()
    {
        using var db = CrearContexto();
        db.TercerosWO.AddRange(
            new TerceroWO { IdTerceroWO = 9044, Identificacion = "900123456", Nombre = "VENTUS COLOMBIA S.A.S." },
            new TerceroWO { IdTerceroWO = 9045, Identificacion = "800999888", Nombre = "MUNDIAL DE TORNILLOS SA" });
        await db.SaveChangesAsync();
        var servicio = Servicio(db);

        Assert.Equal(9044, Assert.Single(await servicio.BuscarClientesAsync("ventus", Vendedora)).IdTercero);
        Assert.Equal(9045, Assert.Single(await servicio.BuscarClientesAsync("800999", Vendedora)).IdTercero);
    }

    [Fact]
    public async Task SinElRol_NoPuedeConsultar()
    {
        using var db = CrearContexto();
        var servicio = Servicio(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.BuscarProductosAsync("az", SinPermiso));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => servicio.ObtenerCatalogoCotizacionAsync(SinPermiso));
    }
}
