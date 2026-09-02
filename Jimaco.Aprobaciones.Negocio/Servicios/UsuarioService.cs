using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class UsuarioService(AppDbContext db) : IUsuarioService
{
    public async Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct = default)
    {
        var usuarios = await db.Usuarios
            .Include(u => u.UsuarioRoles).ThenInclude(ur => ur.Rol)
            .OrderBy(u => u.Nombre)
            .ToListAsync(ct);

        return usuarios.Select(MapearDto).ToList();
    }

    public async Task<UsuarioDto> CrearAsync(CrearUsuarioDto dto, CancellationToken ct = default)
    {
        if (await db.Usuarios.AnyAsync(u => u.Email == dto.Email, ct))
            throw new InvalidOperationException($"Ya existe un usuario con el correo \"{dto.Email}\".");

        var roles = await db.Roles.Where(r => dto.RolesIds.Contains(r.Id)).ToListAsync(ct);
        if (roles.Count != dto.RolesIds.Distinct().Count())
            throw new InvalidOperationException("Uno o más roles indicados no existen.");

        var usuario = new Usuario
        {
            Nombre = dto.Nombre,
            Email = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Telefono = dto.Telefono,
            Activo = true
        };
        usuario.UsuarioRoles = roles.Select(r => new UsuarioRol { Usuario = usuario, RolId = r.Id }).ToList();

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);

        return new UsuarioDto(usuario.Id, usuario.Nombre, usuario.Email, usuario.Telefono, usuario.Activo,
            roles.Select(r => new RolDto(r.Id, r.Nombre, r.Descripcion, r.Activo)).ToList());
    }

    public async Task<UsuarioDto> ActualizarAsync(int id, ActualizarUsuarioDto dto, int usuarioQueEditaId, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios
            .Include(u => u.UsuarioRoles)
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        if (id == usuarioQueEditaId && !dto.Activo)
            throw new InvalidOperationException("No podés desactivarte a vos mismo.");

        var roles = await db.Roles.Where(r => dto.RolesIds.Contains(r.Id)).ToListAsync(ct);
        if (roles.Count != dto.RolesIds.Distinct().Count())
            throw new InvalidOperationException("Uno o más roles indicados no existen.");

        usuario.Nombre = dto.Nombre;
        usuario.Telefono = dto.Telefono;
        usuario.Activo = dto.Activo;

        db.UsuarioRoles.RemoveRange(usuario.UsuarioRoles);
        usuario.UsuarioRoles = roles.Select(r => new UsuarioRol { UsuarioId = usuario.Id, RolId = r.Id }).ToList();

        await db.SaveChangesAsync(ct);

        return new UsuarioDto(usuario.Id, usuario.Nombre, usuario.Email, usuario.Telefono, usuario.Activo,
            roles.Select(r => new RolDto(r.Id, r.Nombre, r.Descripcion, r.Activo)).ToList());
    }

    public async Task CambiarPasswordAsync(int id, CambiarPasswordDto dto, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        if (!BCrypt.Net.BCrypt.Verify(dto.PasswordActual, usuario.PasswordHash))
            throw new InvalidOperationException("La contraseña actual no es correcta.");

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.PasswordNueva);
        await db.SaveChangesAsync(ct);
    }

    private static UsuarioDto MapearDto(Usuario u) => new(
        u.Id, u.Nombre, u.Email, u.Telefono, u.Activo,
        u.UsuarioRoles.Select(ur => new RolDto(ur.Rol.Id, ur.Rol.Nombre, ur.Rol.Descripcion, ur.Rol.Activo)).ToList());
}
