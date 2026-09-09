using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface IInstanciaDocumentoService
{
    Task<InstanciaDocumentoDetalleDto> CrearAsync(CrearInstanciaDocumentoDto dto, int usuarioId, CancellationToken ct = default);

    Task<InstanciaDocumentoDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);

    /// <summary>Documentos con un paso actual donde el usuario tiene un rol habilitado para actuar.</summary>
    Task<IReadOnlyList<InstanciaDocumentoResumenDto>> ListarPendientesAsync(int usuarioId, CancellationToken ct = default);

    /// <summary>Documentos creados por el usuario (incluye los Devueltos, que debe reenviar).</summary>
    Task<IReadOnlyList<InstanciaDocumentoResumenDto>> ListarMisDocumentosAsync(int usuarioId, CancellationToken ct = default);

    Task<InstanciaDocumentoDetalleDto> EjecutarAccionAsync(int id, int usuarioId, EjecutarAccionDto dto, CancellationToken ct = default);

    /// <summary>El emisor reenvía un documento Devuelto — vuelve a entrar al flujo desde el primer paso.</summary>
    Task<InstanciaDocumentoDetalleDto> ReenviarAsync(int id, int usuarioId, CancellationToken ct = default);

    /// <summary>
    /// El emisor dispara de nuevo, a demanda, la notificación del paso actual (sin cambiar nada del
    /// documento) — para cuando el envío automático de la creación falló (ej. el correo no salió) o
    /// simplemente para controlar el momento exacto del envío. Devuelve cuántas notificaciones de
    /// esta tanda se enviaron y cuántas fallaron — a diferencia del envío automático de
    /// Crear/EjecutarAccion, acá SÍ importa que quien lo pidió se entere si falló.
    /// </summary>
    Task<ReenvioNotificacionResultadoDto> ReenviarNotificacionAsync(int id, int usuarioId, CancellationToken ct = default);

    // ---- Sincronización de aprobación de vuelta a World Office (Fase 2) — usado por
    // Jimaco.Aprobaciones.Sincronizador, no por la UI. ----

    /// <summary>Documentos cuyo primer paso ya se aprobó acá y todavía no se confirmó que se escribió en WO.</summary>
    Task<IReadOnlyList<PendienteEscrituraWODto>> ListarPendientesEscrituraWOAsync(CancellationToken ct = default);

    /// <summary>El Sincronizador confirma que ya escribió la aprobación en WO — saca el documento de la cola.</summary>
    Task ConfirmarEscrituraWOAsync(int instanciaDocumentoId, CancellationToken ct = default);

    /// <summary>El Sincronizador encontró un conflicto en WO (ej. la fila ya estaba anulada) — no reintentar, dejarlo visible.</summary>
    Task ReportarConflictoWOAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default);

    Task<AdjuntoDto> AgregarAdjuntoAsync(int id, string nombreArchivo, string? contentType, Stream contenido, int usuarioId, CancellationToken ct = default);

    Task<(Stream Contenido, string NombreArchivo, string? ContentType)> DescargarAdjuntoAsync(int adjuntoId, CancellationToken ct = default);
}
