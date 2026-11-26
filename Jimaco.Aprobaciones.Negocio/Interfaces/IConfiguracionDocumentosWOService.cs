using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Prefijo e interruptor de la creación de cotizaciones en WO (Administración → Config. de cotizaciones).</summary>
public interface IConfiguracionDocumentosWOService
{
    Task<ConfiguracionDocumentosWODto> ObtenerAsync(CancellationToken ct = default);
    Task<ConfiguracionDocumentosWODto> ActualizarAsync(ActualizarConfiguracionDocumentosWODto dto, int usuarioId, CancellationToken ct = default);
}
