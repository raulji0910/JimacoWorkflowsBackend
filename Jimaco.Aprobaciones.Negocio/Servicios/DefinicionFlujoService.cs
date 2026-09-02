using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class DefinicionFlujoService(AppDbContext db) : IDefinicionFlujoService
{
    public async Task<IReadOnlyList<DefinicionFlujoDto>> ListarPorTipoDocumentoAsync(int tipoDocumentoId, CancellationToken ct = default)
    {
        var flujos = await CargarConDetalle().Where(f => f.TipoDocumentoId == tipoDocumentoId).ToListAsync(ct);
        return flujos.Select(MapearDto).ToList();
    }

    public async Task<DefinicionFlujoDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var flujo = await CargarConDetalle().FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException("Definición de flujo no encontrada.");
        return MapearDto(flujo);
    }

    public async Task<DefinicionFlujoDto> CrearAsync(CrearDefinicionFlujoDto dto, CancellationToken ct = default)
    {
        if (dto.Pasos.Count == 0)
            throw new InvalidOperationException("El flujo debe tener al menos un paso.");

        var ordenesDuplicados = dto.Pasos.GroupBy(p => p.Orden).Any(g => g.Count() > 1);
        if (ordenesDuplicados)
            throw new InvalidOperationException("No puede haber dos pasos con el mismo orden.");

        if (!await db.TiposDocumento.AnyAsync(t => t.Id == dto.TipoDocumentoId, ct))
            throw new KeyNotFoundException("Tipo de documento no encontrado.");

        var rolesIds = dto.Pasos.SelectMany(p => p.RolesIds).Distinct().ToList();
        var rolesExistentes = await db.Roles.Where(r => rolesIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        if (rolesExistentes.Count != rolesIds.Count)
            throw new InvalidOperationException("Uno o más roles indicados no existen.");

        // Desactiva cualquier definición activa previa del mismo tipo de documento — solo una activa a la vez.
        var previasActivas = await db.DefinicionesFlujo
            .Where(f => f.TipoDocumentoId == dto.TipoDocumentoId && f.Activo)
            .ToListAsync(ct);
        foreach (var previa in previasActivas)
            previa.Activo = false;

        var flujo = new DefinicionFlujo
        {
            Nombre = dto.Nombre,
            TipoDocumentoId = dto.TipoDocumentoId,
            Activo = true,
            Pasos = dto.Pasos.Select(p => new PasoFlujo
            {
                Nombre = p.Nombre,
                Orden = p.Orden,
                PermiteDevolver = p.PermiteDevolver,
                PermiteRechazar = p.PermiteRechazar,
                PasoFlujoRoles = p.RolesIds.Select(rid => new PasoFlujoRol { RolId = rid }).ToList()
            }).ToList()
        };
        db.DefinicionesFlujo.Add(flujo);

        // Primera pasada: persistir los pasos para que tengan Id antes de resolver los destinos de devolución (referenciados por Orden en el DTO de entrada).
        await db.SaveChangesAsync(ct);

        var pasoIdPorOrden = flujo.Pasos.ToDictionary(p => p.Orden, p => p.Id);
        foreach (var (paso, input) in flujo.Pasos.Zip(dto.Pasos))
        {
            if (input.PasoDestinoDevolucionOrden is int ordenDestino)
            {
                if (!pasoIdPorOrden.TryGetValue(ordenDestino, out var destinoId))
                    throw new InvalidOperationException($"El paso \"{paso.Nombre}\" apunta a un orden de destino ({ordenDestino}) que no existe en el flujo.");
                paso.PasoDestinoDevolucionId = destinoId;
            }
        }
        await db.SaveChangesAsync(ct);

        return await ObtenerAsync(flujo.Id, ct);
    }

    private IQueryable<DefinicionFlujo> CargarConDetalle() =>
        db.DefinicionesFlujo
            .Include(f => f.Pasos).ThenInclude(p => p.PasoFlujoRoles)
            .AsSplitQuery();

    private static DefinicionFlujoDto MapearDto(DefinicionFlujo f) => new(
        f.Id, f.Nombre, f.TipoDocumentoId, f.Activo,
        f.Pasos.OrderBy(p => p.Orden).Select(p => new PasoFlujoDto(
            p.Id, p.Nombre, p.Orden, p.PermiteDevolver, p.PermiteRechazar, p.PasoDestinoDevolucionId,
            p.PasoFlujoRoles.Select(pr => pr.RolId).ToList())).ToList());
}
