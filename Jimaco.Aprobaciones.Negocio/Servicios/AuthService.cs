using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class AuthService(AppDbContext db, IJwtGenerador jwtGenerador) : IAuthService
{
    public async Task<LoginResponseDto?> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios
            .Include(u => u.UsuarioRoles).ThenInclude(ur => ur.Rol)
            .FirstOrDefaultAsync(u => u.Email == email && u.Activo, ct);

        if (usuario is null || !BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
            return null;

        var roles = usuario.UsuarioRoles.Where(ur => ur.Rol.Activo).Select(ur => ur.Rol.Nombre).ToList();
        var token = jwtGenerador.GenerarToken(usuario, roles);
        return new LoginResponseDto(token, usuario.Nombre, usuario.Email, roles);
    }
}
