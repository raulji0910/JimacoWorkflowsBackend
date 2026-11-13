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
            UsuarioWO = dto.UsuarioWO,
            UsuarioWOFerrealiados = string.IsNullOrWhiteSpace(dto.UsuarioWOFerrealiados) ? null : dto.UsuarioWOFerrealiados.Trim(),
            NombresCompletosWO = NormalizarNombresCompletosWO(dto.NombresCompletosWO),
            Activo = true
        };
        usuario.UsuarioRoles = roles.Select(r => new UsuarioRol { Usuario = usuario, RolId = r.Id }).ToList();

        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);

        return new UsuarioDto(usuario.Id, usuario.Nombre, usuario.Email, usuario.Telefono, usuario.UsuarioWO, usuario.Activo,
            roles.Select(r => new RolDto(r.Id, r.Nombre, r.Descripcion, r.Activo, r.PuedeDevolverCualquierDocumento, r.PuedeGestionarAlistamiento, r.PuedeVerAlistamiento, r.PuedeVerCuentasPorPagar)).ToList(),
            usuario.NombresCompletosWO, usuario.UsuarioWOFerrealiados);
    }

    public async Task<UsuarioDto> ActualizarAsync(int id, ActualizarUsuarioDto dto, int usuarioQueEditaId, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios
            .Include(u => u.UsuarioRoles)
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        if (id == usuarioQueEditaId && !dto.Activo)
            throw new InvalidOperationException("No podés desactivarte a vos mismo.");

        if (await db.Usuarios.AnyAsync(u => u.Id != id && u.Email == dto.Email, ct))
            throw new InvalidOperationException($"Ya existe un usuario con el correo \"{dto.Email}\".");

        var roles = await db.Roles.Where(r => dto.RolesIds.Contains(r.Id)).ToListAsync(ct);
        if (roles.Count != dto.RolesIds.Distinct().Count())
            throw new InvalidOperationException("Uno o más roles indicados no existen.");

        usuario.Nombre = dto.Nombre;
        usuario.Email = dto.Email;
        usuario.Telefono = dto.Telefono;
        usuario.UsuarioWO = dto.UsuarioWO;
        usuario.UsuarioWOFerrealiados = string.IsNullOrWhiteSpace(dto.UsuarioWOFerrealiados) ? null : dto.UsuarioWOFerrealiados.Trim();
        usuario.NombresCompletosWO = NormalizarNombresCompletosWO(dto.NombresCompletosWO);
        usuario.Activo = dto.Activo;

        db.UsuarioRoles.RemoveRange(usuario.UsuarioRoles);
        usuario.UsuarioRoles = roles.Select(r => new UsuarioRol { UsuarioId = usuario.Id, RolId = r.Id }).ToList();

        await db.SaveChangesAsync(ct);

        return new UsuarioDto(usuario.Id, usuario.Nombre, usuario.Email, usuario.Telefono, usuario.UsuarioWO, usuario.Activo,
            roles.Select(r => new RolDto(r.Id, r.Nombre, r.Descripcion, r.Activo, r.PuedeDevolverCualquierDocumento, r.PuedeGestionarAlistamiento, r.PuedeVerAlistamiento, r.PuedeVerCuentasPorPagar)).ToList(),
            usuario.NombresCompletosWO, usuario.UsuarioWOFerrealiados);
    }

    /// <summary>Recorta cada nombre, descarta vacíos y repetidos, y los vuelve a unir con "; " — null si no queda ninguno.</summary>
    private static string? NormalizarNombresCompletosWO(string? valor)
    {
        var nombres = Utilidades.NombresCompletosWO.Separar(valor);
        return nombres.Count == 0 ? null : string.Join("; ", nombres);
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

    public async Task RestablecerPasswordAsync(int id, RestablecerPasswordDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.PasswordNueva) || dto.PasswordNueva.Length < 6)
            throw new InvalidOperationException("La contraseña nueva debe tener al menos 6 caracteres.");

        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new KeyNotFoundException("Usuario no encontrado.");

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.PasswordNueva);
        await db.SaveChangesAsync(ct);
    }

    private static UsuarioDto MapearDto(Usuario u) => new(
        u.Id, u.Nombre, u.Email, u.Telefono, u.UsuarioWO, u.Activo,
        u.UsuarioRoles.Select(ur => new RolDto(ur.Rol.Id, ur.Rol.Nombre, ur.Rol.Descripcion, ur.Rol.Activo, ur.Rol.PuedeDevolverCualquierDocumento, ur.Rol.PuedeGestionarAlistamiento, ur.Rol.PuedeVerAlistamiento, ur.Rol.PuedeVerCuentasPorPagar)).ToList(),
        u.NombresCompletosWO, u.UsuarioWOFerrealiados);
}
