namespace Jimaco.Aprobaciones.Negocio.DTOs;

// ---- Portal ----

public record CotizacionRenglonInputDto(int IdInventario, string IdBodega, decimal Cantidad, decimal ValorUnitario, string? Nota);

/// <summary>Lo que se guarda desde la pantalla (crear o actualizar un borrador). Todo opcional menos lo mínimo para guardar.</summary>
public record GuardarCotizacionDto(
    int? IdTercero,
    int? IdDireccion,
    int? IdFormaPago,
    string? Concepto,
    int VigenciaDias,
    IReadOnlyDictionary<int, string>? MasDatos,
    IReadOnlyList<CotizacionRenglonInputDto> Renglones);

public record CotizacionRenglonDto(int Orden, int IdInventario, string Codigo, string Descripcion, string UnidadMedida, string IdBodega,
    decimal Cantidad, decimal ValorUnitario, decimal Iva, string? Nota, decimal Subtotal, decimal ValorIva, decimal Total);

public record CotizacionDto(
    int Id,
    string Estado,
    int UsuarioId,
    string UsuarioNombre,
    int? IdTercero,
    string? ClienteIdentificacion,
    string? ClienteNombre,
    int? IdDireccion,
    string? DireccionTexto,
    int? IdFormaPago,
    string? FormaPagoNombre,
    string? Concepto,
    int VigenciaDias,
    IReadOnlyDictionary<int, string> MasDatos,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    decimal TotalCantidades,
    DateTime FechaCreacion,
    DateTime FechaActualizacion,
    DateOnly? FechaDocumento,
    DateTime? FechaEnvio,
    string? PrefijoWO,
    decimal? NumeroWO,
    DateTime? FechaCreacionWO,
    string? MensajeError,
    IReadOnlyList<CotizacionRenglonDto> Renglones);

public record CotizacionResumenDto(
    int Id,
    string Estado,
    string UsuarioNombre,
    string? ClienteNombre,
    string? ClienteIdentificacion,
    int Renglones,
    decimal Total,
    DateTime FechaActualizacion,
    DateTime? FechaEnvio,
    string? PrefijoWO,
    decimal? NumeroWO,
    string? MensajeError);

// ---- Sincronizador (modo --documentos) ----

public record RenglonCreacionCotizacionDto(int IdInventario, string Codigo, string IdBodega, string UnidadMedida,
    decimal Cantidad, decimal ValorUnitario, decimal Iva, int? IdClasificacion, string? Nota);

public record PendienteCreacionCotizacionDto(
    int CotizacionId,
    DateTime Fecha,
    int IdTercero,
    string ClienteIdentificacion,
    int? IdDireccion,
    int IdFormaPago,
    int IdTerceroElabora,
    string UsuarioWO,
    string Concepto,
    int VigenciaDias,
    IReadOnlyDictionary<int, string> MasDatos,
    decimal Total,
    IReadOnlyList<RenglonCreacionCotizacionDto> Renglones);

/// <param name="Fecha">Fecha con la que quedó en WO (el día en que se creó, decisión 2026-11-26).</param>
public record CotizacionCreadaDto(string Prefijo, decimal Numero, int IdAsientoContable, DateTime? Fecha = null);
