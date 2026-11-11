using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>
/// Prefijo del CE y "crear CE en World Office" (2026-11-11) — los cambia un administrador desde la
/// página y el Sincronizador los lee en cada corrida. Ver Modelo.Entidades.ConfiguracionCuentasPorPagar.
/// </summary>
public interface IConfiguracionCuentasPorPagarService
{
    Task<ConfiguracionCuentasPorPagarDto> ObtenerAsync(CancellationToken ct = default);
    Task<ConfiguracionCuentasPorPagarDto> ActualizarAsync(ActualizarConfiguracionCuentasPorPagarDto dto, int usuarioId, CancellationToken ct = default);
}
