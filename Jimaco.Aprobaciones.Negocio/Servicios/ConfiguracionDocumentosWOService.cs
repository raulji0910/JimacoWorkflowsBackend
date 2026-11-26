using System.Text.RegularExpressions;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <inheritdoc cref="IConfiguracionDocumentosWOService"/>
public partial class ConfiguracionDocumentosWOService(AppDbContext db, TimeProvider reloj) : IConfiguracionDocumentosWOService
{
    // Prefijos de WO: letras, números y guion (COT, PRUEB). Se guarda en mayúsculas.
    [GeneratedRegex("^[A-Z0-9-]{1,20}$")]
    private static partial Regex FormatoPrefijo();

    public async Task<ConfiguracionDocumentosWODto> ObtenerAsync(CancellationToken ct = default) => await ADtoAsync(await CargarAsync(ct), ct);

    public async Task<ConfiguracionDocumentosWODto> ActualizarAsync(ActualizarConfiguracionDocumentosWODto dto, int usuarioId, CancellationToken ct = default)
    {
        var prefijo = (dto.PrefijoCotizacion ?? "").Trim().ToUpperInvariant();
        if (!FormatoPrefijo().IsMatch(prefijo))
            throw new InvalidOperationException("El prefijo de la cotización solo puede tener letras, números y guion (máximo 20), por ejemplo COT.");

        var config = await CargarAsync(ct);
        config.PrefijoCotizacion = prefijo;
        config.CrearCotizacionesEnWorldOffice = dto.CrearCotizacionesEnWorldOffice;
        config.FechaActualizacion = reloj.GetUtcNow().UtcDateTime;
        config.ActualizadoPorUsuarioId = usuarioId;
        await db.SaveChangesAsync(ct);

        await db.Entry(config).Reference(c => c.ActualizadoPorUsuario).LoadAsync(ct);
        return await ADtoAsync(config, ct);
    }

    private async Task<ConfiguracionDocumentosWO> CargarAsync(CancellationToken ct)
    {
        var config = await db.ConfiguracionesDocumentosWO.Include(c => c.ActualizadoPorUsuario)
            .FirstOrDefaultAsync(c => c.Id == ConfiguracionDocumentosWO.IdUnico, ct);
        if (config is not null) return config;

        // La fila la siembra la migración; esto solo cubre una base sin ella (p. ej. pruebas).
        config = new ConfiguracionDocumentosWO
        {
            Id = ConfiguracionDocumentosWO.IdUnico,
            PrefijoCotizacion = ConfiguracionDocumentosWO.PrefijoCotizacionPorDefecto,
            CrearCotizacionesEnWorldOffice = false
        };
        db.ConfiguracionesDocumentosWO.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    private async Task<ConfiguracionDocumentosWODto> ADtoAsync(ConfiguracionDocumentosWO c, CancellationToken ct) =>
        new(c.PrefijoCotizacion, c.CrearCotizacionesEnWorldOffice, c.FechaActualizacion, c.ActualizadoPorUsuario?.Nombre,
            await db.Cotizaciones.CountAsync(x => x.Estado == EstadoCotizacion.PorCrear, ct));
}
