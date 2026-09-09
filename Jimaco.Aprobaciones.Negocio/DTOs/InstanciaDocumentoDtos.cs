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
    string? PrefijoOrigen = null);

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
    string CreadoPorNombre,
    DateTime FechaCreacion,
    IReadOnlyList<AdjuntoDto> Adjuntos,
    IReadOnlyList<HistorialAccionDto> Historial,
    IReadOnlyList<RenglonDto> Renglones,
    int? IdAsientoContableOrigen,
    string? PrefijoOrigen,
    bool PendienteEscrituraWO,
    string? ConflictoWO,
    DateTime? FechaEscrituraWO);

public record EjecutarAccionDto(TipoAccion Accion, string? Comentario);

public record ReenvioNotificacionResultadoDto(int Enviadas, int Fallidas);

// ---- Sincronización de aprobación de vuelta a World Office (Fase 2) ----

/// <summary>Un documento cuyo primer paso ya se aprobó acá y falta reflejarlo en WO.</summary>
public record PendienteEscrituraWODto(
    int InstanciaDocumentoId,
    int IdAsientoContableOrigen,
    string PrefijoOrigen,
    string? NumeroReferencia,
    string UsuarioWO);

public record ReportarConflictoWODto(string Mensaje);
