using System.Text.RegularExpressions;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <inheritdoc cref="IConfiguracionCuentasPorPagarService"/>
public partial class ConfiguracionCuentasPorPagarService(AppDbContext db) : IConfiguracionCuentasPorPagarService
{
    // Prefijos de WO: letras, números y guion (CEJ, CEJ-2). Se guarda en mayúsculas.
    [GeneratedRegex("^[A-Z0-9-]{1,20}$")]
    private static partial Regex FormatoPrefijo();

    public async Task<ConfiguracionCuentasPorPagarDto> ObtenerAsync(CancellationToken ct = default)
    {
        var config = await CargarAsync(ct);
        return ADto(config);
    }

    public async Task<ConfiguracionCuentasPorPagarDto> ActualizarAsync(ActualizarConfiguracionCuentasPorPagarDto dto, int usuarioId, CancellationToken ct = default)
    {
        var prefijo = (dto.PrefijoCE ?? "").Trim().ToUpperInvariant();
        if (!FormatoPrefijo().IsMatch(prefijo))
            throw new InvalidOperationException("El prefijo del CE solo puede tener letras, números y guion (máximo 20), por ejemplo CEJ.");

        var config = await CargarAsync(ct);
        config.PrefijoCE = prefijo;
        config.CrearCEEnWorldOffice = dto.CrearCEEnWorldOffice;
        config.FechaActualizacion = DateTime.UtcNow;
        config.ActualizadoPorUsuarioId = usuarioId;
        await db.SaveChangesAsync(ct);

        await db.Entry(config).Reference(c => c.ActualizadoPorUsuario).LoadAsync(ct);
        return ADto(config);
    }

    private async Task<ConfiguracionCuentasPorPagar> CargarAsync(CancellationToken ct)
    {
        var config = await db.ConfiguracionesCuentasPorPagar.Include(c => c.ActualizadoPorUsuario)
            .FirstOrDefaultAsync(c => c.Id == ConfiguracionCuentasPorPagar.IdUnico, ct);
        if (config is not null) return config;

        // La fila la siembra la migración; esto solo cubre una base sin ella (p. ej. pruebas).
        config = new ConfiguracionCuentasPorPagar
        {
            Id = ConfiguracionCuentasPorPagar.IdUnico,
            PrefijoCE = ConfiguracionCuentasPorPagar.PrefijoCEPorDefecto,
            CrearCEEnWorldOffice = false
        };
        db.ConfiguracionesCuentasPorPagar.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    private static ConfiguracionCuentasPorPagarDto ADto(ConfiguracionCuentasPorPagar c) =>
        new(c.PrefijoCE, c.CrearCEEnWorldOffice, c.FechaActualizacion, c.ActualizadoPorUsuario?.Nombre);
}
