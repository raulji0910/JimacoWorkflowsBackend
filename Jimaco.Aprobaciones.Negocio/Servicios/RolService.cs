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
            .Select(r => new RolDto(
                r.Id, r.Nombre, r.Descripcion, r.Activo,
                r.PuedeDevolverCualquierDocumento, r.PuedeGestionarAlistamiento, r.PuedeVerAlistamiento, r.PuedeVerCuentasPorPagar, r.PuedePagarCuentas,
                r.PuedeGestionarEntregas, r.PuedeVerEntregas, r.PuedeDocumentarEntregas, r.PuedeFacturarEntregas,
                r.PuedeGestionarAlistamientoFerrealiados, r.PuedeVerAlistamientoFerrealiados))
            .ToListAsync(ct);

    public async Task<RolDto> CrearAsync(CrearRolDto dto, CancellationToken ct = default)
    {
        if (await db.Roles.AnyAsync(r => r.Nombre == dto.Nombre, ct))
            throw new InvalidOperationException($"Ya existe un rol llamado \"{dto.Nombre}\".");

        var rol = new Rol
        {
            Nombre = dto.Nombre,
            Descripcion = dto.Descripcion,
            Activo = true,
            PuedeDevolverCualquierDocumento = dto.PuedeDevolverCualquierDocumento,
            PuedeGestionarAlistamiento = dto.PuedeGestionarAlistamiento,
            PuedeVerAlistamiento = dto.PuedeVerAlistamiento,
            PuedeVerCuentasPorPagar = dto.PuedeVerCuentasPorPagar,
            PuedePagarCuentas = dto.PuedePagarCuentas,
            PuedeGestionarEntregas = dto.PuedeGestionarEntregas,
            PuedeVerEntregas = dto.PuedeVerEntregas,
            PuedeDocumentarEntregas = dto.PuedeDocumentarEntregas,
            PuedeFacturarEntregas = dto.PuedeFacturarEntregas,
            PuedeGestionarAlistamientoFerrealiados = dto.PuedeGestionarAlistamientoFerrealiados,
            PuedeVerAlistamientoFerrealiados = dto.PuedeVerAlistamientoFerrealiados
        };
        db.Roles.Add(rol);
        await db.SaveChangesAsync(ct);

        return MapearDto(rol);
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
        rol.PuedeDevolverCualquierDocumento = dto.PuedeDevolverCualquierDocumento;
        rol.PuedeGestionarAlistamiento = dto.PuedeGestionarAlistamiento;
        rol.PuedeVerAlistamiento = dto.PuedeVerAlistamiento;
        rol.PuedeVerCuentasPorPagar = dto.PuedeVerCuentasPorPagar;
        rol.PuedePagarCuentas = dto.PuedePagarCuentas;
        rol.PuedeGestionarEntregas = dto.PuedeGestionarEntregas;
        rol.PuedeVerEntregas = dto.PuedeVerEntregas;
        rol.PuedeDocumentarEntregas = dto.PuedeDocumentarEntregas;
        rol.PuedeFacturarEntregas = dto.PuedeFacturarEntregas;
        rol.PuedeGestionarAlistamientoFerrealiados = dto.PuedeGestionarAlistamientoFerrealiados;
        rol.PuedeVerAlistamientoFerrealiados = dto.PuedeVerAlistamientoFerrealiados;
        await db.SaveChangesAsync(ct);

        return MapearDto(rol);
    }

    private static RolDto MapearDto(Rol rol) => new(
        rol.Id, rol.Nombre, rol.Descripcion, rol.Activo,
        rol.PuedeDevolverCualquierDocumento, rol.PuedeGestionarAlistamiento, rol.PuedeVerAlistamiento, rol.PuedeVerCuentasPorPagar, rol.PuedePagarCuentas,
        rol.PuedeGestionarEntregas, rol.PuedeVerEntregas, rol.PuedeDocumentarEntregas, rol.PuedeFacturarEntregas,
        rol.PuedeGestionarAlistamientoFerrealiados, rol.PuedeVerAlistamientoFerrealiados);
}
