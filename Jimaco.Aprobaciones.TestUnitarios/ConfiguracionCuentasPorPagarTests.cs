using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>2026-11-11: el prefijo del CE y "crear CE en WO" se manejan desde la página.</summary>
public class ConfiguracionCuentasPorPagarTests
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private ConfiguracionCuentasPorPagarService Servicio() => new(db);

    private async Task<int> AdminAsync()
    {
        var u = new Usuario { Nombre = "Raúl Admin", Email = "admin@jimaco.local", PasswordHash = "x" };
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    [Fact]
    public async Task Sin_configurar_arranca_apagada_y_con_el_prefijo_de_prueba()
    {
        var config = await Servicio().ObtenerAsync();

        Assert.Equal("CEJ-2", config.PrefijoCE);
        Assert.False(config.CrearCEEnWorldOffice);
        Assert.Null(config.FechaActualizacion);
        Assert.Equal(1, await db.ConfiguracionesCuentasPorPagar.CountAsync());
    }

    [Fact]
    public async Task Actualizar_guarda_prefijo_en_mayusculas_y_quien_lo_cambio()
    {
        var adminId = await AdminAsync();

        var config = await Servicio().ActualizarAsync(new ActualizarConfiguracionCuentasPorPagarDto("  cej ", true), adminId);

        Assert.Equal("CEJ", config.PrefijoCE);
        Assert.True(config.CrearCEEnWorldOffice);
        Assert.Equal("Raúl Admin", config.ActualizadoPor);
        Assert.NotNull(config.FechaActualizacion);

        // Sigue habiendo una sola fila y la lectura devuelve lo guardado
        Assert.Equal(1, await db.ConfiguracionesCuentasPorPagar.CountAsync());
        var leida = await Servicio().ObtenerAsync();
        Assert.Equal("CEJ", leida.PrefijoCE);
        Assert.True(leida.CrearCEEnWorldOffice);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CE J")]
    [InlineData("CEJ;DROP")]
    [InlineData("PREFIJODEMASIADOLARGO1")]
    public async Task Rechaza_prefijos_invalidos_sin_tocar_lo_guardado(string prefijo)
    {
        var adminId = await AdminAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio().ActualizarAsync(new ActualizarConfiguracionCuentasPorPagarDto(prefijo, true), adminId));

        var config = await Servicio().ObtenerAsync();
        Assert.Equal("CEJ-2", config.PrefijoCE);
        Assert.False(config.CrearCEEnWorldOffice);
    }

    [Fact]
    public async Task Acepta_prefijo_con_guion()
    {
        var adminId = await AdminAsync();
        var config = await Servicio().ActualizarAsync(new ActualizarConfiguracionCuentasPorPagarDto("cej-2", false), adminId);
        Assert.Equal("CEJ-2", config.PrefijoCE);
        Assert.False(config.CrearCEEnWorldOffice);
    }

    [Fact]
    public async Task Cuentas_incluidas_arrancan_con_las_de_siempre_y_se_normalizan_al_guardar()
    {
        var adminId = await AdminAsync();
        Assert.Equal("2205,2210,2335", (await Servicio().ObtenerAsync()).CuentasIncluidas);

        var config = await Servicio().ActualizarAsync(new ActualizarConfiguracionCuentasPorPagarDto("CEJ", true, " 2205 ; 2210,2205 "), adminId);

        Assert.Equal("2205,2210", config.CuentasIncluidas);
        // Sin mandar cuentas (web anterior) no se tocan.
        Assert.Equal("2205,2210", (await Servicio().ActualizarAsync(new ActualizarConfiguracionCuentasPorPagarDto("CEJ", true), adminId)).CuentasIncluidas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1105")]
    [InlineData("2205,13300")]
    public async Task Cuentas_incluidas_invalidas_se_rechazan(string cuentas)
    {
        var adminId = await AdminAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio().ActualizarAsync(new ActualizarConfiguracionCuentasPorPagarDto("CEJ", true, cuentas), adminId));
        Assert.Equal("2205,2210,2335", (await Servicio().ObtenerAsync()).CuentasIncluidas);
    }
}
