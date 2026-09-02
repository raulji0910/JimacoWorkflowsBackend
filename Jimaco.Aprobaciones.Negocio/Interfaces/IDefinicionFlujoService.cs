using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface IDefinicionFlujoService
{
    Task<IReadOnlyList<DefinicionFlujoDto>> ListarPorTipoDocumentoAsync(int tipoDocumentoId, CancellationToken ct = default);
    Task<DefinicionFlujoDto> ObtenerAsync(int id, CancellationToken ct = default);

    /// <summary>Crea una nueva definición y la marca Activa, desactivando cualquier otra definición activa del mismo tipo de documento.</summary>
    Task<DefinicionFlujoDto> CrearAsync(CrearDefinicionFlujoDto dto, CancellationToken ct = default);
}
