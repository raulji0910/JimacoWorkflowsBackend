using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface IUsuarioService
{
    Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct = default);
    Task<UsuarioDto> CrearAsync(CrearUsuarioDto dto, CancellationToken ct = default);
    Task<UsuarioDto> ActualizarAsync(int id, ActualizarUsuarioDto dto, int usuarioQueEditaId, CancellationToken ct = default);
    Task CambiarPasswordAsync(int id, CambiarPasswordDto dto, CancellationToken ct = default);
}
