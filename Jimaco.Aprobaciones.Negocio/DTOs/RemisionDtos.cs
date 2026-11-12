namespace Jimaco.Aprobaciones.Negocio.DTOs;

// ---- Entregas / Remisiones (2026-11-12) ----

// Lo que manda el Sincronizador

/// <summary>Desde qué fecha traer REM y cuáles ya existen (por IdAsientoContable de WO).</summary>
public record EstadoSincronizacionRemisionesDto(DateTime FechaInicio, IReadOnlyList<int> IdsConocidos);

public record RenglonRemisionInputDto(string? Codigo, string Descripcion, decimal Cantidad, string? UnidadMedida);

public record RemisionInputDto(
    int IdAsientoContableWO,
    string Prefijo,
    string Numero,
    DateTime Fecha,
    string? Cliente,
    string? NitCliente,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? Vendedor,
    string? ElaboradoPorWO,
    string? FormaDePago,
    string? DireccionEntrega,
    string? CampoEntrega,
    string? OrdenCompraCliente,
    string? Cotizacion,
    string? Nota,
    IReadOnlyList<RenglonRemisionInputDto> Renglones);

public record ResultadoSincronizacionRemisionesDto(int Creadas, int YaExistian);

// Lo que ve la página

/// <summary>
/// Una fila del dashboard de Entregas (2026-11-12, mismo formato que el de Alistamiento). EnAlerta =
/// por entregar hace más de Entregas:HorasUmbralAlerta horas desde que llegó (contabilizada).
/// </summary>
public record RemisionTableroDto(
    int Id,
    string Numero,
    DateTime Fecha,
    string? Cliente,
    string? NitCliente,
    string? Ciudad,
    string? DireccionEntrega,
    string? CampoEntrega,
    string? ElaboradoPor,
    string? Cotizacion,
    string? OrdenCompraCliente,
    int CantidadRenglones,
    /// <summary>Cuándo llegó al sistema (≈ cuándo se contabilizó en WO) — desde acá corre el atraso.</summary>
    DateTime FechaContabilizada,
    string Estado,
    bool EnAlerta,
    DateTime? FechaEntrega,
    string? EntregadaPor,
    string? VerificacionQR);

public record RenglonRemisionDto(int Orden, string? Codigo, string Descripcion, decimal Cantidad, string? UnidadMedida);

public record SoporteRemisionDto(
    int Id,
    string Tipo,
    string NombreArchivo,
    string? ContentType,
    long TamanoBytes,
    string? QrLeido,
    string SubidoPor,
    DateTime FechaCarga);

public record RemisionDetalleDto(
    int Id,
    string Prefijo,
    string Numero,
    DateTime Fecha,
    string? Cliente,
    string? NitCliente,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? Vendedor,
    string? ElaboradoPorWO,
    string? FormaDePago,
    string? DireccionEntrega,
    string? CampoEntrega,
    string? OrdenCompraCliente,
    string? Cotizacion,
    string? Nota,
    string Estado,
    DateTime? FechaEntrega,
    string? EntregadaPor,
    string? ObservacionEntrega,
    string? VerificacionQR,
    /// <summary>Texto que lleva el QR impreso de ESTA remisión — el navegador lo compara con lo que lee.</summary>
    string CodigoQR,
    IReadOnlyList<RenglonRemisionDto> Renglones,
    IReadOnlyList<SoporteRemisionDto> Soportes,
    bool PuedeGestionar);

/// <summary>Un archivo del soporte de entrega, ya leído (contenido) junto con el QR que encontró el navegador.</summary>
public record ArchivoSoporteEntrega(string NombreArchivo, string? ContentType, Stream Contenido, long TamanoBytes, string? QrLeido);
