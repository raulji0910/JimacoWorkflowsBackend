using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface IRolService
{
    Task<IReadOnlyList<RolDto>> ListarAsync(CancellationToken ct = default);
    Task<RolDto> CrearAsync(CrearRolDto dto, CancellationToken ct = default);
    Task<RolDto> ActualizarAsync(int id, ActualizarRolDto dto, CancellationToken ct = default);
}
