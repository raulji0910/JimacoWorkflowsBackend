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

    // Cuenta del PUC: solo dígitos, de 2 (clase+grupo) a 10.
    [GeneratedRegex("^[0-9]{2,10}$")]
    private static partial Regex FormatoCuenta();

    /// <summary>
    /// Normaliza y valida la lista que escribe el administrador ("2205, 2210 ;2335" → "2205,2210,2335").
    /// Solo cuentas de pasivo (empiezan por 2): lo que se paga a terceros desde este módulo.
    /// </summary>
    internal static string NormalizarCuentas(string? texto)
    {
        var cuentas = ConfiguracionCuentasPorPagar.SepararCuentas(texto);
        if (cuentas.Count == 0)
            throw new InvalidOperationException("Escribí al menos una cuenta contable (por ejemplo 2205).");
        foreach (var c in cuentas)
        {
            if (!FormatoCuenta().IsMatch(c))
                throw new InvalidOperationException($"\"{c}\" no es una cuenta válida: solo números, por ejemplo 2205 o 220501.");
            if (!c.StartsWith('2'))
                throw new InvalidOperationException($"La cuenta {c} no es de pasivo (las cuentas por pagar empiezan por 2).");
        }
        var resultado = string.Join(",", cuentas);
        if (resultado.Length > 300)
            throw new InvalidOperationException("Son demasiadas cuentas: agrupalas por un prefijo más corto (por ejemplo 2335 en vez de cada subcuenta).");
        return resultado;
    }

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

        var cuentas = dto.CuentasIncluidas is null ? null : NormalizarCuentas(dto.CuentasIncluidas);

        var config = await CargarAsync(ct);
        config.PrefijoCE = prefijo;
        if (cuentas is not null) config.CuentasIncluidas = cuentas;
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
        new(c.PrefijoCE, c.CrearCEEnWorldOffice, c.FechaActualizacion, c.ActualizadoPorUsuario?.Nombre,
            string.IsNullOrWhiteSpace(c.CuentasIncluidas) ? ConfiguracionCuentasPorPagar.CuentasIncluidasPorDefecto : c.CuentasIncluidas);
}
