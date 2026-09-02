using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class RolService(AppDbContext db) : IRolService
{
    public async Task<IReadOnlyList<RolDto>> ListarAsync(CancellationToken ct = default) =>
        await db.Roles
            .OrderBy(r => r.Nombre)
            .Select(r => new RolDto(r.Id, r.Nombre, r.Descripcion, r.Activo))
            .ToListAsync(ct);

    public async Task<RolDto> CrearAsync(CrearRolDto dto, CancellationToken ct = default)
    {
        if (await db.Roles.AnyAsync(r => r.Nombre == dto.Nombre, ct))
            throw new InvalidOperationException($"Ya existe un rol llamado \"{dto.Nombre}\".");

        var rol = new Rol { Nombre = dto.Nombre, Descripcion = dto.Descripcion, Activo = true };
        db.Roles.Add(rol);
        await db.SaveChangesAsync(ct);

        return new RolDto(rol.Id, rol.Nombre, rol.Descripcion, rol.Activo);
    }

    public async Task<RolDto> ActualizarAsync(int id, ActualizarRolDto dto, CancellationToken ct = default)
    {
        var rol = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException("Rol no encontrado.");

        if (await db.Roles.AnyAsync(r => r.Id != id && r.Nombre == dto.Nombre, ct))
            throw new InvalidOperationException($"Ya existe un rol llamado \"{dto.Nombre}\".");

        rol.Nombre = dto.Nombre;
        rol.Descripcion = dto.Descripcion;
        rol.Activo = dto.Activo;
        await db.SaveChangesAsync(ct);

        return new RolDto(rol.Id, rol.Nombre, rol.Descripcion, rol.Activo);
    }
}
