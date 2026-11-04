namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface INotificacionService
{
    /// <summary>Notifica a todos los usuarios con un rol habilitado en ese paso que tienen un documento nuevo para revisar.</summary>
    Task NotificarPasoAsync(int instanciaDocumentoId, int pasoFlujoId, CancellationToken ct = default);

    /// <summary>
    /// Avisa a quien tiene el documento ACTUAL (ej. Gerencia General) que el Sincronizador no pudo
    /// encontrar el Comprobante de Egreso que Contabilidad escribió — para que decida si lo devuelve
    /// a Contabilidad a corregir, en vez de quedar esperando en silencio un aviso que nunca va a
    /// llegar (ver InstanciaDocumentoService.ReportarConflictoComprobanteEgresoAsync).
    /// </summary>
    Task NotificarConflictoComprobanteEgresoAsync(int instanciaDocumentoId, int pasoFlujoId, string numeroSolicitado, string mensajeConflicto, CancellationToken ct = default);

    /// <summary>
    /// Avisa a todos los usuarios con un rol habilitado en el paso de Contabilidad (el que exige
    /// Comprobante de Egreso al aprobar) que el comprobante ya generado no se va a usar, porque el
    /// documento fue redirigido a pago de contado (ver InstanciaDocumentoService.EjecutarAccionAsync,
    /// caso TipoAccion.RedirigidoAContado).
    /// </summary>
    Task NotificarComprobanteEgresoNoUtilizadoAsync(int instanciaDocumentoId, int pasoFlujoContabilidadId, string numeroComprobante, string motivoRedireccion, CancellationToken ct = default);

    /// <summary>
    /// Avisa a Logística (<see cref="Modelo.Entidades.Rol.PuedeGestionarAlistamiento"/>) y a quien
    /// elaboró la Cotización (<see cref="Modelo.Entidades.Usuario.NombresCompletosWO"/>) que una Hoja
    /// de Alistamiento sigue incompleta más allá del umbral configurado — ver el chequeo periódico en el backend.
    /// </summary>
    Task NotificarHojaAlistamientoAtrasadaAsync(int documentoVinculadoId, CancellationToken ct = default);

    /// <summary>
    /// Avisa, una sola vez, que una Hoja de Alistamiento quedó Completa (Transporte terminó de
    /// marcar cantidades) Y Remisionada (World Office ya la despachó) a la vez — a los mismos
    /// destinatarios que el aviso de atraso. <paramref name="instanciaDocumentoIdsDelGrupo"/> son los Id
    /// de InstanciaDocumento de TODAS las OC que comparten la Cotización, usados como respaldo (vía
    /// SolicitanteWO) cuando ningún usuario tiene configurado el nombre de quien la elaboró.
    /// </summary>
    Task NotificarHojaCompletaYRemisionadaAsync(int documentoVinculadoId, IReadOnlyList<int> instanciaDocumentoIdsDelGrupo, CancellationToken ct = default);

    /// <summary>
    /// Avisa a todo el rol de Contabilidad (habilitado en el paso que exige Comprobante de Egreso)
    /// y al emisor real de la OC (por <see cref="Modelo.Entidades.InstanciaDocumento.SolicitanteWO"/>)
    /// que Gerencia General aprobó el pago — con el soporte de pago pegado de verdad al correo.
    /// </summary>
    Task NotificarPagoRealizadoAsync(int instanciaDocumentoId, int pasoFlujoContabilidadId, int soportePagoAdjuntoId, CancellationToken ct = default);

    /// <summary>Notifica a un usuario puntual (ej. al emisor cuando su documento fue devuelto/rechazado/completado).</summary>
    Task NotificarUsuarioAsync(int instanciaDocumentoId, int usuarioId, string asunto, string mensaje, CancellationToken ct = default);

    /// <summary>Envía un correo de prueba suelto, sin asociarlo a ningún documento — para validar que la configuración SMTP funciona.</summary>
    Task EnviarPruebaAsync(string destinatarioEmail, CancellationToken ct = default);
}
