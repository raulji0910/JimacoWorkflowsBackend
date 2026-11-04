using Jimaco.Aprobaciones.Modelo.Entidades;

namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record RenglonInputDto(
    string? Codigo,
    string Descripcion,
    decimal Cantidad,
    string? UnidadMedida,
    decimal ValorUnitario,
    decimal PorcentajeIva);

public record RenglonDto(
    int Id,
    string? Codigo,
    string Descripcion,
    decimal Cantidad,
    string? UnidadMedida,
    decimal ValorUnitario,
    decimal PorcentajeIva,
    decimal Total);

/// <summary>Un renglón del documento vinculado que se está creando junto con el principal.</summary>
public record RenglonDocumentoVinculadoInputDto(
    string? Codigo,
    string Descripcion,
    decimal Cantidad,
    string? UnidadMedida,
    decimal ValorUnitario,
    decimal PorcentajeIva);

/// <summary>
/// Un documento externo de World Office ligado al que se está creando (ej. la Cotización de
/// origen de una Orden de Compra) — ver <see cref="Jimaco.Aprobaciones.Modelo.Entidades.DocumentoVinculado"/>.
/// </summary>
public record DocumentoVinculadoInputDto(
    string PrefijoOrigen,
    string NumeroReferencia,
    DateTime Fecha,
    DateTime FechaVencimiento,
    string? Cliente,
    string? NitCliente,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? ElaboradoPor,
    string? FormaDePago,
    string? CampoEntrega,
    IReadOnlyList<RenglonDocumentoVinculadoInputDto> Renglones);

public record CrearInstanciaDocumentoDto(
    int TipoDocumentoId,
    string? NumeroReferencia,
    string? Proveedor,
    decimal? Valor,
    DateTime? FechaDocumento,
    Dictionary<string, string>? Datos,
    IReadOnlyList<RenglonInputDto>? Renglones = null,
    // Origen en World Office (Fase 2) — null en documentos creados a mano. Van juntos: el prefijo
    // solo no identifica la fila (un mismo IdAsientoContable siempre es único, pero se guarda el
    // prefijo también como chequeo de seguridad al escribir la aprobación de vuelta en WO).
    int? IdAsientoContableOrigen = null,
    string? PrefijoOrigen = null,
    IReadOnlyList<DocumentoVinculadoInputDto>? DocumentosVinculados = null,
    // Usuario de WO que mandó a aprobar (puede ser distinto de Datos.elaboradoPor) — se usa para
    // notificarle el resultado del primer paso, si hay un Usuario acá con este UsuarioWO.
    string? SolicitanteWO = null);

public record HistorialAccionDto(int Id, string? PasoNombre, string UsuarioNombre, TipoAccion Accion, string? Comentario, DateTime Fecha);

public record AdjuntoDto(int Id, string NombreArchivo, string? ContentType, long TamanoBytes, DateTime FechaCarga);

public record InstanciaDocumentoResumenDto(
    int Id,
    string TipoDocumentoNombre,
    string? NumeroReferencia,
    string? Proveedor,
    decimal? Valor,
    DateTime? FechaDocumento,
    EstadoInstanciaDocumento Estado,
    string? PasoActualNombre,
    DateTime FechaCreacion);

public record RenglonDocumentoVinculadoDto(
    int Id,
    string? Codigo,
    string Descripcion,
    decimal Cantidad,
    string? UnidadMedida,
    decimal ValorUnitario,
    decimal PorcentajeIva,
    decimal Total,
    // Control de alistamiento (2026-09-28) — lo que Transporte va registrando, editable.
    decimal CantidadDisponible);

public record DocumentoVinculadoDto(
    int Id,
    string PrefijoOrigen,
    string NumeroReferencia,
    DateTime Fecha,
    DateTime FechaVencimiento,
    string? Cliente,
    string? NitCliente,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? ElaboradoPor,
    string? FormaDePago,
    string? CampoEntrega,
    // Control de alistamiento — cuándo entró esta hoja al sistema (reloj de la alerta) y si ya
    // todos sus renglones tienen CantidadDisponible >= Cantidad.
    DateTime FechaCreacion,
    bool Completa,
    IReadOnlyList<RenglonDocumentoVinculadoDto> Renglones);

/// <summary>Un paso posible de devolución — solo Id y Nombre, para armar el selector en el frontend.</summary>
public record PasoResumenDto(int Id, string Nombre);

public record RenglonComprobanteEgresoDto(
    int Id,
    string? CodigoCuenta,
    string? NombreCuenta,
    string? Concepto,
    string? Tercero,
    decimal Debito,
    decimal Credito);

/// <summary>El Comprobante de Egreso real de WO, ya encontrado y copiado — ver ComprobanteEgreso.</summary>
public record ComprobanteEgresoDto(
    int Id,
    string NumeroReferencia,
    DateTime Fecha,
    decimal Valor,
    string? Beneficiario,
    string? NitBeneficiario,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? Concepto,
    string? ElaboradoPor,
    IReadOnlyList<RenglonComprobanteEgresoDto> Renglones);

public record InstanciaDocumentoDetalleDto(
    int Id,
    string TipoDocumentoNombre,
    string? NumeroReferencia,
    string? Proveedor,
    decimal? Valor,
    DateTime? FechaDocumento,
    Dictionary<string, string>? Datos,
    EstadoInstanciaDocumento Estado,
    int? PasoActualId,
    string? PasoActualNombre,
    bool PasoActualPermiteDevolver,
    bool PasoActualPermiteRechazar,
    // Pasos anteriores a los que se puede devolver desde el paso actual — vacío = "Devolver" manda
    // al emisor directo, sin elegir nada (ver PasoFlujo.DestinosDevolucion).
    IReadOnlyList<PasoResumenDto> DestinosDevolucionDisponibles,
    // Si "Aprobar" en el paso actual exige el número de un Comprobante de Egreso (ver más abajo).
    bool PasoActualRequiereComprobanteEgreso,
    // Si "Aprobar" en el paso actual exige un comentario obligatorio (ej. "a quién le entregó el dinero").
    bool PasoActualRequiereComentarioAlAprobar,
    // Si el paso actual ofrece la acción "Redirigir a pago de contado" (ver PasoFlujo.PasoRedirigirAPagoContadoId).
    bool PasoActualPermiteRedirigirAPagoContado,
    // Si "Aprobar" en el paso actual exige adjuntar un PDF con el soporte del pago (ver más abajo).
    bool PasoActualRequiereSoportePagoAlAprobar,
    string CreadoPorNombre,
    DateTime FechaCreacion,
    IReadOnlyList<AdjuntoDto> Adjuntos,
    IReadOnlyList<HistorialAccionDto> Historial,
    IReadOnlyList<RenglonDto> Renglones,
    int? IdAsientoContableOrigen,
    string? PrefijoOrigen,
    bool PendienteEscrituraWO,
    string? ConflictoWO,
    DateTime? FechaEscrituraWO,
    IReadOnlyList<DocumentoVinculadoDto> DocumentosVinculados,
    string? SolicitanteWO,
    // Comprobante de Egreso pedido al aprobar "Área Contable" (o el paso que corresponda) — null
    // mientras no se pidió ninguno o todavía no llegó de WO (ver PendienteBusquedaComprobanteEgresoWO).
    string? NumeroComprobanteEgresoSolicitado,
    bool PendienteBusquedaComprobanteEgresoWO,
    string? ConflictoComprobanteEgresoWO,
    ComprobanteEgresoDto? ComprobanteEgreso,
    // El PDF que Gerencia General adjuntó al aprobar el pago real — null si ese paso no lo exigía o todavía no pasó por ahí.
    AdjuntoDto? SoportePago);

public record EjecutarAccionDto(
    TipoAccion Accion,
    string? Comentario,
    // Solo aplica a Devolver, cuando el paso actual tiene más de un destino posible (ver
    // DestinosDevolucionDisponibles) — indica a cuál de ellos. Si el paso solo tiene uno, se puede
    // omitir (se usa ese). Si no tiene ninguno, se ignora (siempre vuelve al emisor).
    int? PasoDestinoId = null,
    // Solo aplica a Aprobar, cuando el paso actual exige comprobante de egreso (ver
    // PasoActualRequiereComprobanteEgreso) — el número que la persona escribió, tal cual, sin el
    // prefijo "CEJ" (se asume fijo del lado del Sincronizador).
    string? NumeroComprobanteEgreso = null,
    // Solo aplica a Aprobar, cuando el paso actual exige soporte de pago (ver
    // PasoActualRequiereSoportePagoAlAprobar) — el Id del Adjunto (ya subido antes, vía el
    // endpoint normal de adjuntos) que se marca como EL soporte de esta aprobación.
    int? SoportePagoAdjuntoId = null);

public record ReenvioNotificacionResultadoDto(int Enviadas, int Fallidas);

// ---- Buscar y devolver cualquier documento (2026-09-23) — permiso administrativo transversal, ver
// Rol.PuedeDevolverCualquierDocumento. No depende de tener el rol habilitado en el paso actual. ----

public record DevolverForzadoDto(string Comentario);

// ---- Sincronización de aprobación de vuelta a World Office (Fase 2) ----

/// <summary>
/// Qué hay que reflejar en World Office para el primer paso de un documento sincronizado — ver
/// <see cref="Jimaco.Aprobaciones.Sincronizador"/> WorldOfficeWriter para el mapeo a <c>senAprobado</c>.
/// <c>Rechazado</c> y <c>Devuelto</c> son conceptos DISTINTOS acá a propósito (2026-09-16): antes
/// "Devolver" escribía lo mismo que "Rechazar" en WO (senAprobado=2), dejando la OC en un estado
/// terminal/inutilizable aunque la intención fuera "corregí esto y volvé a mandarlo" — ahora
/// "Devolver" vuelve a dejarla "En espera de aprobación" en WO (editable de nuevo, salvo que el
/// perfil del usuario de WO se lo impida por otro motivo — ver notas de permisos en el proyecto),
/// y solo "Rechazar" es el equivalente real al botón nativo "Rechazar" de WO.
/// </summary>
public enum ResultadoPrimerPaso
{
    /// <summary>senAprobado = 1.</summary>
    Aprobado,

    /// <summary>senAprobado = 2 — terminal, mismo resultado que el botón nativo "Rechazar" de WO.</summary>
    Rechazado,

    /// <summary>senAprobado = NULL, IdTerceroAprobador = NULL — vuelve a "En espera de aprobación".</summary>
    Devuelto
}

/// <summary>Un documento cuyo primer paso ya se resolvió acá y falta reflejarlo en WO.</summary>
public record PendienteEscrituraWODto(
    int InstanciaDocumentoId,
    int IdAsientoContableOrigen,
    string PrefijoOrigen,
    string? NumeroReferencia,
    string UsuarioWO,
    ResultadoPrimerPaso Resultado);

public record ReportarConflictoWODto(string Mensaje);

// ---- Comprobante de Egreso (2026-09-22) — mismo patrón "pedir y recoger" que la escritura de
// aprobación: el Sincronizador revisa esta cola en cada corrida, busca en WO, y empuja de vuelta. ----

/// <summary>Un documento esperando que el Sincronizador busque su Comprobante de Egreso en WO.</summary>
public record PendienteComprobanteEgresoDto(
    int InstanciaDocumentoId,
    string NumeroComprobanteEgresoSolicitado);

/// <summary>Un renglón (línea débito/crédito) del comprobante, tal como lo trajo el Sincronizador de WO.</summary>
public record RenglonComprobanteEgresoInputDto(
    string? CodigoCuenta,
    string? NombreCuenta,
    string? Concepto,
    string? Tercero,
    decimal Debito,
    decimal Credito);

/// <summary>El Comprobante de Egreso que el Sincronizador encontró en WO — se adjunta a la instancia.</summary>
public record ComprobanteEgresoEncontradoDto(
    string NumeroReferencia,
    DateTime Fecha,
    decimal Valor,
    string? Beneficiario,
    string? NitBeneficiario,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? Concepto,
    string? ElaboradoPor,
    IReadOnlyList<RenglonComprobanteEgresoInputDto> Renglones);
