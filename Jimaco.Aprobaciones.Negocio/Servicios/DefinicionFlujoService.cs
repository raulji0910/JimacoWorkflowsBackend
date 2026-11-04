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

        if (dto.ValorMinimo is not null && dto.ValorMaximo is not null && dto.ValorMinimo > dto.ValorMaximo)
            throw new InvalidOperationException("ValorMinimo no puede ser mayor que ValorMaximo.");

        if (!await db.TiposDocumento.AnyAsync(t => t.Id == dto.TipoDocumentoId, ct))
            throw new KeyNotFoundException("Tipo de documento no encontrado.");

        var rolesIds = dto.Pasos.SelectMany(p => p.RolesIds).Distinct().ToList();
        var rolesExistentes = await db.Roles.Where(r => rolesIds.Contains(r.Id)).Select(r => r.Id).ToListAsync(ct);
        if (rolesExistentes.Count != rolesIds.Count)
            throw new InvalidOperationException("Uno o más roles indicados no existen.");

        // Un paso que permite devolver necesita al menos UNA opción de destino (el emisor y/o algún
        // paso anterior puntual) — si no, "Devolver" queda configurado pero sin ningún lado a donde ir.
        var pasoSinDestino = dto.Pasos.FirstOrDefault(p =>
            p.PermiteDevolver && !p.PermiteDevolverAlEmisor && p.DestinosDevolucionOrdenes.Count == 0);
        if (pasoSinDestino is not null)
            throw new InvalidOperationException(
                $"El paso \"{pasoSinDestino.Nombre}\" permite devolver pero no tiene ningún destino — habilitá \"vuelve al emisor\" o elegí al menos un paso anterior.");

        // Regla de negocio confirmada 2026-09-22: solo el primer paso del flujo (el que recibe el
        // documento directo del emisor — Gerencia Comercial en los tres flujos reales de OC) puede
        // devolver al emisor Y rechazar. Cualquier paso posterior que quiera "devolver más atrás"
        // tiene que hacerlo a un paso anterior puntual (DestinosDevolucionOrdenes), nunca saltar
        // directo al emisor — y no puede rechazar (el rechazo/devolución al emisor son las dos
        // acciones que escriben de vuelta en World Office, y esa decisión es exclusiva del primer
        // paso). Antes de esto, el paso 1 de PermiteDevolverAlEmisor quedaba en true por default de
        // la migración para TODOS los pasos — este chequeo evita que se repita el error.
        var pasoPosteriorConAccionExclusivaDePaso1 = dto.Pasos.FirstOrDefault(p =>
            p.Orden != 1 && (p.PermiteDevolverAlEmisor || p.PermiteRechazar));
        if (pasoPosteriorConAccionExclusivaDePaso1 is not null)
            throw new InvalidOperationException(
                $"El paso \"{pasoPosteriorConAccionExclusivaDePaso1.Nombre}\" (orden {pasoPosteriorConAccionExclusivaDePaso1.Orden}) no puede devolver al emisor ni rechazar — eso es exclusivo del primer paso del flujo. Los pasos posteriores solo pueden devolver a un paso anterior puntual.");

        // A diferencia de antes (2026-09-21): ya NO se desactiva ninguna definición previa — puede
        // haber varias activas a la vez para el mismo tipo de documento, una por cada combinación
        // de Valor/FormaDePago (ver DefinicionFlujo). En cambio, se rechaza si esta condición nueva
        // se pisa con la de otra ya activa, para no dejar casos ambiguos sin que nadie lo note.
        await ValidarSinSolaparAsync(dto.TipoDocumentoId, dto.ValorMinimo, dto.ValorMaximo, dto.FormaDePagoRequerida, excluirId: null, ct);

        var flujo = new DefinicionFlujo
        {
            Nombre = dto.Nombre,
            TipoDocumentoId = dto.TipoDocumentoId,
            Activo = true,
            ValorMinimo = dto.ValorMinimo,
            ValorMaximo = dto.ValorMaximo,
            FormaDePagoRequerida = dto.FormaDePagoRequerida,
            Pasos = dto.Pasos.Select(p => new PasoFlujo
            {
                Nombre = p.Nombre,
                Orden = p.Orden,
                PermiteDevolver = p.PermiteDevolver,
                PermiteRechazar = p.PermiteRechazar,
                PermiteDevolverAlEmisor = p.PermiteDevolverAlEmisor,
                RequiereComprobanteEgresoAlAprobar = p.RequiereComprobanteEgresoAlAprobar,
                RequiereComentarioAlAprobar = p.RequiereComentarioAlAprobar,
                RequiereSoportePagoAlAprobar = p.RequiereSoportePagoAlAprobar,
                PasoFlujoRoles = p.RolesIds.Select(rid => new PasoFlujoRol { RolId = rid }).ToList()
                // PasoRedirigirAPagoContadoId se resuelve más abajo, por Orden — igual que
                // DestinosDevolucionOrdenes, no existe el Id todavía en esta primera pasada.
            }).ToList()
        };
        db.DefinicionesFlujo.Add(flujo);

        // Primera pasada: persistir los pasos para que tengan Id antes de resolver los destinos de
        // devolución (referenciados por Orden en el DTO de entrada, no por Id).
        await db.SaveChangesAsync(ct);

        var pasoIdPorOrden = flujo.Pasos.ToDictionary(p => p.Orden, p => p.Id);
        foreach (var (paso, input) in flujo.Pasos.Zip(dto.Pasos))
        {
            foreach (var ordenDestino in input.DestinosDevolucionOrdenes)
            {
                if (!pasoIdPorOrden.TryGetValue(ordenDestino, out var destinoId))
                    throw new InvalidOperationException($"El paso \"{paso.Nombre}\" apunta a un orden de destino ({ordenDestino}) que no existe en el flujo.");
                if (ordenDestino >= input.Orden)
                    throw new InvalidOperationException($"El paso \"{paso.Nombre}\" solo puede devolver a un paso ANTERIOR (orden {ordenDestino} no es menor que {input.Orden}).");

                db.PasoFlujoDestinosDevolucion.Add(new PasoFlujoDestinoDevolucion
                {
                    PasoFlujoOrigenId = paso.Id,
                    PasoFlujoDestinoId = destinoId
                });
            }

            if (input.RedirigirAPagoContadoOrden is int ordenRedireccion)
            {
                if (!pasoIdPorOrden.TryGetValue(ordenRedireccion, out var destinoRedireccionId))
                    throw new InvalidOperationException($"El paso \"{paso.Nombre}\" apunta a un orden de redirección ({ordenRedireccion}) que no existe en el flujo.");
                if (ordenRedireccion <= input.Orden)
                    throw new InvalidOperationException($"El paso \"{paso.Nombre}\" solo puede redirigir a un paso POSTERIOR (orden {ordenRedireccion} no es mayor que {input.Orden}).");

                paso.PasoRedirigirAPagoContadoId = destinoRedireccionId;
            }
        }
        await db.SaveChangesAsync(ct);

        return await ObtenerAsync(flujo.Id, ct);
    }

    public async Task DesactivarAsync(int id, CancellationToken ct = default)
    {
        var flujo = await db.DefinicionesFlujo.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException("Definición de flujo no encontrada.");
        flujo.Activo = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task ActivarAsync(int id, CancellationToken ct = default)
    {
        var flujo = await db.DefinicionesFlujo.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException("Definición de flujo no encontrada.");

        await ValidarSinSolaparAsync(flujo.TipoDocumentoId, flujo.ValorMinimo, flujo.ValorMaximo, flujo.FormaDePagoRequerida, excluirId: flujo.Id, ct);

        flujo.Activo = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Rechaza si la condición (ValorMinimo/ValorMaximo/FormaDePagoRequerida) se solapa con la de
    /// otra definición ya ACTIVA del mismo tipo de documento — dos condiciones se solapan si sus
    /// formas de pago no las separan claramente Y sus rangos de valor se cruzan (null = sin límite
    /// de ese lado). Sin esto, un documento con un Valor/FormaDePago dado podría calzar con más de
    /// un flujo a la vez sin que nadie lo note al crear la definición.
    /// </summary>
    private async Task ValidarSinSolaparAsync(int tipoDocumentoId, decimal? valorMinimo, decimal? valorMaximo, string? formaDePagoRequerida, int? excluirId, CancellationToken ct)
    {
        var activas = await db.DefinicionesFlujo
            .Where(f => f.TipoDocumentoId == tipoDocumentoId && f.Activo && (excluirId == null || f.Id != excluirId))
            .ToListAsync(ct);

        var min = valorMinimo ?? decimal.MinValue;
        var max = valorMaximo ?? decimal.MaxValue;

        foreach (var otra in activas)
        {
            var formasDistintas = formaDePagoRequerida is not null && otra.FormaDePagoRequerida is not null
                && !string.Equals(formaDePagoRequerida, otra.FormaDePagoRequerida, StringComparison.OrdinalIgnoreCase);
            if (formasDistintas) continue;

            var otroMin = otra.ValorMinimo ?? decimal.MinValue;
            var otroMax = otra.ValorMaximo ?? decimal.MaxValue;
            var rangosSeCruzan = min <= otroMax && otroMin <= max;

            if (rangosSeCruzan)
                throw new InvalidOperationException(
                    $"Esta condición se solapa con la definición activa \"{otra.Nombre}\" (id {otra.Id}) — un mismo documento podría calzar con las dos. Ajustá los rangos o desactivá la otra primero.");
        }
    }

    private IQueryable<DefinicionFlujo> CargarConDetalle() =>
        db.DefinicionesFlujo
            .Include(f => f.Pasos).ThenInclude(p => p.PasoFlujoRoles)
            .Include(f => f.Pasos).ThenInclude(p => p.DestinosDevolucion)
            .AsSplitQuery();

    private static DefinicionFlujoDto MapearDto(DefinicionFlujo f) => new(
        f.Id, f.Nombre, f.TipoDocumentoId, f.Activo, f.ValorMinimo, f.ValorMaximo, f.FormaDePagoRequerida,
        f.Pasos.OrderBy(p => p.Orden).Select(p => new PasoFlujoDto(
            p.Id, p.Nombre, p.Orden, p.PermiteDevolver, p.PermiteRechazar, p.PermiteDevolverAlEmisor,
            p.DestinosDevolucion.Select(d => d.PasoFlujoDestinoId).ToList(), p.RequiereComprobanteEgresoAlAprobar,
            p.RequiereComentarioAlAprobar, p.PasoRedirigirAPagoContadoId, p.RequiereSoportePagoAlAprobar,
            p.PasoFlujoRoles.Select(pr => pr.RolId).ToList())).ToList());
}
