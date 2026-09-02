using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface ITipoDocumentoService
{
    Task<IReadOnlyList<TipoDocumentoDto>> ListarAsync(CancellationToken ct = default);
    Task<TipoDocumentoDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<TipoDocumentoDto> CrearAsync(CrearTipoDocumentoDto dto, CancellationToken ct = default);
    Task<TipoDocumentoDto> ActualizarAsync(int id, ActualizarTipoDocumentoDto dto, CancellationToken ct = default);
}
