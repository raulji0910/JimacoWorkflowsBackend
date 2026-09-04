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
    IReadOnlyList<RenglonInputDto>? Renglones = null);

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
    IReadOnlyList<RenglonDto> Renglones);

public record EjecutarAccionDto(TipoAccion Accion, string? Comentario);
