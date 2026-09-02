using Jimaco.Aprobaciones.Modelo.Entidades;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface IJwtGenerador
{
    string GenerarToken(Usuario usuario, IReadOnlyList<string> roles);
}
