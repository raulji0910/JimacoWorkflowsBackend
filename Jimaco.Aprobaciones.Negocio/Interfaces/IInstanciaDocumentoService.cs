using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface IInstanciaDocumentoService
{
    // notificar=false (2026-11-09): las órdenes de pago a proveedor no avisan una por una — el aviso
    // sale una sola vez por relación (o por pago extraordinario), ver CuentasPorPagarService.
    Task<InstanciaDocumentoDetalleDto> CrearAsync(CrearInstanciaDocumentoDto dto, int usuarioId, CancellationToken ct = default, bool notificar = true);

    Task<InstanciaDocumentoDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);

    /// <summary>Documentos con un paso actual donde el usuario tiene un rol habilitado para actuar.</summary>
    Task<IReadOnlyList<InstanciaDocumentoResumenDto>> ListarPendientesAsync(int usuarioId, CancellationToken ct = default);

    /// <summary>Documentos creados por el usuario (incluye los Devueltos, que debe reenviar).</summary>
    Task<IReadOnlyList<InstanciaDocumentoResumenDto>> ListarMisDocumentosAsync(int usuarioId, CancellationToken ct = default);

    // omitirControlRol=true SOLO desde el menú "Pagos" (PagosService), que ya exigió Rol.PuedePagarCuentas.
    Task<InstanciaDocumentoDetalleDto> EjecutarAccionAsync(int id, int usuarioId, EjecutarAccionDto dto, CancellationToken ct = default, bool omitirControlRol = false);

    /// <summary>Si todas las órdenes del lote ya tienen su CE adjunto (o un conflicto), manda UN aviso de "lote terminado" — una sola vez.</summary>
    Task NotificarLotePagoSiTerminoAsync(int lotePagoId, CancellationToken ct = default);

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

    // ---- Buscar y devolver cualquier documento (2026-09-23) — permiso administrativo transversal,
    // ver Rol.PuedeDevolverCualquierDocumento. Ambos métodos rechazan (UnauthorizedAccessException)
    // si el usuario no tiene ningún rol con ese permiso. ----

    /// <summary>Busca por número de referencia (coincidencia parcial), sin importar el paso/rol del usuario.</summary>
    Task<IReadOnlyList<InstanciaDocumentoResumenDto>> BuscarPorNumeroReferenciaAsync(string numeroReferencia, int usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve el documento al emisor sin importar el paso actual ni el rol del usuario ahí —
    /// anula el paso en curso y SIEMPRE queda pendiente de reflejar "en espera de aprobación" en
    /// World Office (a diferencia de una devolución normal desde un paso posterior, que no toca WO).
    /// </summary>
    Task<InstanciaDocumentoDetalleDto> DevolverForzadoAsync(int id, int usuarioId, DevolverForzadoDto dto, CancellationToken ct = default);

    // ---- Sincronización de aprobación de vuelta a World Office (Fase 2) — usado por
    // Jimaco.Aprobaciones.Sincronizador, no por la UI. ----

    /// <summary>Documentos cuyo primer paso ya se aprobó acá y todavía no se confirmó que se escribió en WO.</summary>
    Task<IReadOnlyList<PendienteEscrituraWODto>> ListarPendientesEscrituraWOAsync(CancellationToken ct = default);

    /// <summary>El Sincronizador confirma que ya escribió la aprobación en WO — saca el documento de la cola.</summary>
    Task ConfirmarEscrituraWOAsync(int instanciaDocumentoId, CancellationToken ct = default);

    /// <summary>El Sincronizador encontró un conflicto en WO (ej. la fila ya estaba anulada) — no reintentar, dejarlo visible.</summary>
    Task ReportarConflictoWOAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default);

    // ---- Comprobante de Egreso (2026-09-22) — mismo patrón "pedir y recoger", usado por
    // Jimaco.Aprobaciones.Sincronizador, no por la UI. ----

    /// <summary>Documentos esperando que el Sincronizador busque su Comprobante de Egreso en WO.</summary>
    Task<IReadOnlyList<PendienteComprobanteEgresoDto>> ListarPendientesComprobanteEgresoAsync(CancellationToken ct = default);

    /// <summary>El Sincronizador encontró el comprobante en WO — se adjunta y se dispara la notificación pospuesta.</summary>
    Task AdjuntarComprobanteEgresoAsync(int instanciaDocumentoId, ComprobanteEgresoEncontradoDto dto, CancellationToken ct = default);

    /// <summary>El Sincronizador no encontró el comprobante (ej. número mal escrito) — no reintentar, dejarlo visible.</summary>
    Task ReportarConflictoComprobanteEgresoAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="esSoportePago"/> true (2026-09-29): valida que sea un PDF antes de guardarlo
    /// — usado cuando se sube el soporte de pago de Gerencia General, para dar el error apenas
    /// elige el archivo en vez de recién al intentar aprobar.
    /// </summary>
    Task<AdjuntoDto> AgregarAdjuntoAsync(int id, string nombreArchivo, string? contentType, Stream contenido, int usuarioId, bool esSoportePago = false, CancellationToken ct = default);

    Task<(Stream Contenido, string NombreArchivo, string? ContentType)> DescargarAdjuntoAsync(int adjuntoId, CancellationToken ct = default);
}
