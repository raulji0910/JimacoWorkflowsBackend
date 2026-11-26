using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>Prefijo e interruptor de la creación de cotizaciones en WO, manejados desde la página (2026-11-26).</summary>
public class ConfiguracionDocumentosWOServiceTests
{
    private static AppDbContext CrearContexto()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Usuarios.Add(new Usuario { Id = 1, Nombre = "Admin", Email = "admin@x.com", PasswordHash = "x" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task ArrancaApagadaYConElPrefijoDePrueba()
    {
        using var db = CrearContexto();

        var config = await new ConfiguracionDocumentosWOService(db, TimeProvider.System).ObtenerAsync();

        Assert.Equal("PRUEB", config.PrefijoCotizacion);
        Assert.False(config.CrearCotizacionesEnWorldOffice);
    }

    [Fact]
    public async Task Actualizar_GuardaEnMayusculasConQuienYCuantasEsperan()
    {
        using var db = CrearContexto();
        db.Cotizaciones.Add(new Cotizacion { UsuarioId = 1, Estado = EstadoCotizacion.PorCrear });
        await db.SaveChangesAsync();
        var servicio = new ConfiguracionDocumentosWOService(db, TimeProvider.System);

        var config = await servicio.ActualizarAsync(new ActualizarConfiguracionDocumentosWODto(" cot ", true), 1);

        Assert.Equal("COT", config.PrefijoCotizacion);
        Assert.True(config.CrearCotizacionesEnWorldOffice);
        Assert.Equal("Admin", config.ActualizadoPor);
        Assert.Equal(1, config.CotizacionesEnCola);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CO T")]
    [InlineData("COT#")]
    [InlineData("PREFIJODEMASIADOLARGO1")]
    public async Task PrefijoInvalido_SeRechaza(string prefijo)
    {
        using var db = CrearContexto();
        var servicio = new ConfiguracionDocumentosWOService(db, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(new ActualizarConfiguracionDocumentosWODto(prefijo, true), 1));
    }
}
