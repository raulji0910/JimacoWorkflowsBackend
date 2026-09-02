using System.Text.Json;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// El motor de workflow: crear una instancia, listar pendientes por usuario/rol, y ejecutar
/// las acciones (aprobar/devolver/rechazar/reenviar) validando permisos y transiciones.
/// Todo lo que sabe sobre "OC" es configuración (TipoDocumento/DefinicionFlujo) — este servicio
/// no tiene ninguna referencia a un tipo de documento concreto.
/// </summary>
public class InstanciaDocumentoService(AppDbContext db, IAlmacenamientoArchivos almacenamiento, TimeProvider timeProvider) : IInstanciaDocumentoService
{
    public async Task<InstanciaDocumentoDetalleDto> CrearAsync(CrearInstanciaDocumentoDto dto, int usuarioId, CancellationToken ct = default)
    {
        var tipoDocumento = await db.TiposDocumento.Include(t => t.Campos)
            .FirstOrDefaultAsync(t => t.Id == dto.TipoDocumentoId && t.Activo, ct)
            ?? throw new KeyNotFoundException("Tipo de documento no encontrado o inactivo.");

        var flujo = await db.DefinicionesFlujo.Include(f => f.Pasos)
            .FirstOrDefaultAsync(f => f.TipoDocumentoId == dto.TipoDocumentoId && f.Activo, ct)
            ?? throw new InvalidOperationException("No hay un flujo activo configurado para este tipo de documento.");

        var primerPaso = flujo.Pasos.OrderBy(p => p.Orden).FirstOrDefault()
            ?? throw new InvalidOperationException("El flujo no tiene pasos configurados.");

        ValidarCamposRequeridos(tipoDocumento.Campos, dto.Datos);

        var ahora = timeProvider.GetUtcNow().UtcDateTime;
        var instancia = new InstanciaDocumento
        {
            TipoDocumentoId = tipoDocumento.Id,
            DefinicionFlujoId = flujo.Id,
            PasoActualId = primerPaso.Id,
            Estado = EstadoInstanciaDocumento.EnProceso,
            NumeroReferencia = dto.NumeroReferencia,
            Proveedor = dto.Proveedor,
            Valor = dto.Valor,
            FechaDocumento = dto.FechaDocumento,
            DatosJson = dto.Datos is { Count: > 0 } ? JsonSerializer.Serialize(dto.Datos) : null,
            CreadoPorUsuarioId = usuarioId,
            FechaCreacion = ahora
        };
        db.InstanciasDocumento.Add(instancia);
        db.HistorialAcciones.Add(new HistorialAccion
        {
            InstanciaDocumento = instancia,
            PasoFlujoId = primerPaso.Id,
            UsuarioId = usuarioId,
            Accion = TipoAccion.Creado,
            Fecha = ahora
        });

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(instancia.Id, ct);
    }

    public async Task<InstanciaDocumentoDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var instancia = await CargarConDetalle().FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");
        return MapearDetalleDto(instancia);
    }

    public async Task<IReadOnlyList<InstanciaDocumentoResumenDto>> ListarPendientesAsync(int usuarioId, CancellationToken ct = default)
    {
        var rolesUsuario = await ObtenerRolesIdsAsync(usuarioId, ct);

        var instancias = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual).ThenInclude(p => p!.PasoFlujoRoles)
            .Where(i => i.Estado == EstadoInstanciaDocumento.EnProceso
                        && i.PasoActual!.PasoFlujoRoles.Any(pr => rolesUsuario.Contains(pr.RolId)))
            .OrderBy(i => i.FechaCreacion)
            .ToListAsync(ct);

        return instancias.Select(MapearResumenDto).ToList();
    }

    public async Task<IReadOnlyList<InstanciaDocumentoResumenDto>> ListarMisDocumentosAsync(int usuarioId, CancellationToken ct = default)
    {
        var instancias = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual)
            .Where(i => i.CreadoPorUsuarioId == usuarioId)
            .OrderByDescending(i => i.FechaCreacion)
            .ToListAsync(ct);

        return instancias.Select(MapearResumenDto).ToList();
    }

    public async Task<InstanciaDocumentoDetalleDto> EjecutarAccionAsync(int id, int usuarioId, EjecutarAccionDto dto, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.PasoActual).ThenInclude(p => p!.PasoFlujoRoles)
            .Include(i => i.DefinicionFlujo).ThenInclude(f => f.Pasos)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        if (instancia.Estado != EstadoInstanciaDocumento.EnProceso || instancia.PasoActual is null)
            throw new InvalidOperationException("El documento no tiene un paso activo pendiente de acción.");

        var paso = instancia.PasoActual;

        var rolesUsuario = await ObtenerRolesIdsAsync(usuarioId, ct);
        if (!paso.PasoFlujoRoles.Any(pr => rolesUsuario.Contains(pr.RolId)))
            throw new UnauthorizedAccessException("No tenés un rol habilitado para actuar en este paso.");

        var ahora = timeProvider.GetUtcNow().UtcDateTime;

        switch (dto.Accion)
        {
            case TipoAccion.Aprobado:
                var siguiente = instancia.DefinicionFlujo.Pasos
                    .Where(p => p.Orden > paso.Orden)
                    .OrderBy(p => p.Orden)
                    .FirstOrDefault();
                if (siguiente is null)
                {
                    instancia.Estado = EstadoInstanciaDocumento.Completado;
                    instancia.PasoActualId = null;
                }
                else
                {
                    instancia.PasoActualId = siguiente.Id;
                }
                break;

            case TipoAccion.Devuelto:
                if (!paso.PermiteDevolver)
                    throw new InvalidOperationException("Este paso no permite devolver el documento.");
                if (string.IsNullOrWhiteSpace(dto.Comentario))
                    throw new InvalidOperationException("Debés indicar un motivo para devolver el documento.");
                if (paso.PasoDestinoDevolucionId is int destinoId)
                {
                    instancia.PasoActualId = destinoId;
                }
                else
                {
                    instancia.Estado = EstadoInstanciaDocumento.Devuelto;
                    instancia.PasoActualId = null;
                }
                break;

            case TipoAccion.Rechazado:
                if (!paso.PermiteRechazar)
                    throw new InvalidOperationException("Este paso no permite rechazar el documento.");
                if (string.IsNullOrWhiteSpace(dto.Comentario))
                    throw new InvalidOperationException("Debés indicar un motivo para rechazar el documento.");
                instancia.Estado = EstadoInstanciaDocumento.Rechazado;
                instancia.PasoActualId = null;
                break;

            default:
                throw new InvalidOperationException($"Acción \"{dto.Accion}\" no soportada en este paso.");
        }

        db.HistorialAcciones.Add(new HistorialAccion
        {
            InstanciaDocumentoId = instancia.Id,
            PasoFlujoId = paso.Id,
            UsuarioId = usuarioId,
            Accion = dto.Accion,
            Comentario = dto.Comentario,
            Fecha = ahora
        });

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(instancia.Id, ct);
    }

    public async Task<InstanciaDocumentoDetalleDto> ReenviarAsync(int id, int usuarioId, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.DefinicionFlujo).ThenInclude(f => f.Pasos)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        if (instancia.Estado != EstadoInstanciaDocumento.Devuelto)
            throw new InvalidOperationException("Solo se puede reenviar un documento que fue devuelto.");

        if (instancia.CreadoPorUsuarioId != usuarioId)
            throw new UnauthorizedAccessException("Solo quien emitió el documento puede reenviarlo.");

        var primerPaso = instancia.DefinicionFlujo.Pasos.OrderBy(p => p.Orden).First();

        instancia.PasoActualId = primerPaso.Id;
        instancia.Estado = EstadoInstanciaDocumento.EnProceso;

        db.HistorialAcciones.Add(new HistorialAccion
        {
            InstanciaDocumentoId = instancia.Id,
            PasoFlujoId = primerPaso.Id,
            UsuarioId = usuarioId,
            Accion = TipoAccion.Reenviado,
            Fecha = timeProvider.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(instancia.Id, ct);
    }

    public async Task<AdjuntoDto> AgregarAdjuntoAsync(int id, string nombreArchivo, string? contentType, Stream contenido, int usuarioId, CancellationToken ct = default)
    {
        if (!await db.InstanciasDocumento.AnyAsync(i => i.Id == id, ct))
            throw new KeyNotFoundException("Documento no encontrado.");

        var rutaArchivo = await almacenamiento.GuardarAsync(nombreArchivo, contenido, ct);

        var adjunto = new Adjunto
        {
            InstanciaDocumentoId = id,
            NombreArchivo = nombreArchivo,
            RutaArchivo = rutaArchivo,
            ContentType = contentType,
            TamanoBytes = contenido.Length,
            SubidoPorUsuarioId = usuarioId,
            FechaCarga = timeProvider.GetUtcNow().UtcDateTime
        };
        db.Adjuntos.Add(adjunto);
        await db.SaveChangesAsync(ct);

        return new AdjuntoDto(adjunto.Id, adjunto.NombreArchivo, adjunto.ContentType, adjunto.TamanoBytes, adjunto.FechaCarga);
    }

    public async Task<(Stream Contenido, string NombreArchivo, string? ContentType)> DescargarAdjuntoAsync(int adjuntoId, CancellationToken ct = default)
    {
        var adjunto = await db.Adjuntos.FirstOrDefaultAsync(a => a.Id == adjuntoId, ct)
            ?? throw new KeyNotFoundException("Adjunto no encontrado.");

        var contenido = await almacenamiento.AbrirAsync(adjunto.RutaArchivo, ct);
        return (contenido, adjunto.NombreArchivo, adjunto.ContentType);
    }

    private static void ValidarCamposRequeridos(IEnumerable<CampoTipoDocumento> campos, Dictionary<string, string>? datos)
    {
        var faltantes = campos
            .Where(c => c.Requerido)
            .Where(c => datos is null || !datos.TryGetValue(c.Nombre, out var v) || string.IsNullOrWhiteSpace(v))
            .Select(c => c.Etiqueta)
            .ToList();

        if (faltantes.Count > 0)
            throw new InvalidOperationException($"Faltan campos requeridos: {string.Join(", ", faltantes)}.");
    }

    private async Task<List<int>> ObtenerRolesIdsAsync(int usuarioId, CancellationToken ct) =>
        await db.UsuarioRoles.Where(ur => ur.UsuarioId == usuarioId).Select(ur => ur.RolId).ToListAsync(ct);

    private IQueryable<InstanciaDocumento> CargarConDetalle() =>
        db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual)
            .Include(i => i.CreadoPorUsuario)
            .Include(i => i.Adjuntos)
            .Include(i => i.Historial).ThenInclude(h => h.Usuario)
            .Include(i => i.Historial).ThenInclude(h => h.PasoFlujo)
            .AsSplitQuery();

    private static InstanciaDocumentoResumenDto MapearResumenDto(InstanciaDocumento i) => new(
        i.Id, i.TipoDocumento.Nombre, i.NumeroReferencia, i.Proveedor, i.Valor, i.FechaDocumento,
        i.Estado, i.PasoActual?.Nombre, i.FechaCreacion);

    private static InstanciaDocumentoDetalleDto MapearDetalleDto(InstanciaDocumento i) => new(
        i.Id, i.TipoDocumento.Nombre, i.NumeroReferencia, i.Proveedor, i.Valor, i.FechaDocumento,
        i.DatosJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(i.DatosJson),
        i.Estado, i.PasoActualId, i.PasoActual?.Nombre,
        i.PasoActual?.PermiteDevolver ?? false, i.PasoActual?.PermiteRechazar ?? false,
        i.CreadoPorUsuario.Nombre, i.FechaCreacion,
        i.Adjuntos.Select(a => new AdjuntoDto(a.Id, a.NombreArchivo, a.ContentType, a.TamanoBytes, a.FechaCarga)).ToList(),
        i.Historial.OrderBy(h => h.Fecha).Select(h => new HistorialAccionDto(
            h.Id, h.PasoFlujo?.Nombre, h.Usuario.Nombre, h.Accion, h.Comentario, h.Fecha)).ToList());
}
