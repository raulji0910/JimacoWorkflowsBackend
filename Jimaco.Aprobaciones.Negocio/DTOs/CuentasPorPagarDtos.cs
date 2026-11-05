namespace Jimaco.Aprobaciones.Negocio.DTOs;

// ---- Lo que manda el Sincronizador (foto completa de WO en cada corrida) ----

/// <summary>Una fila de Vista_CuentasPorPagar_Detallada, con el Saldo tal cual lo trae WO (negativo = se debe).</summary>
public record PartidaCuentaPorPagarInputDto(
    int? IdDocumentoDetalleWO,
    string Identificacion,
    string NombreTercero,
    string? Documento,
    string? Prefijo,
    string? NumeroDocumento,
    string? NumeroDocumentoExterno,
    DateTime Fecha,
    DateTime? Vencimiento,
    decimal SaldoWO,
    string? CodigoCuenta);

/// <summary>Saldo sin cruzar de anticipos a un tercero (Débito − Crédito de la cuenta de anticipos).</summary>
public record AnticipoProveedorInputDto(string Identificacion, string NombreTercero, int Movimientos, decimal Saldo);

public record FotoCuentasPorPagarDto(
    IReadOnlyList<PartidaCuentaPorPagarInputDto> Partidas,
    IReadOnlyList<AnticipoProveedorInputDto> Anticipos);

// ---- Lo que ve la pantalla ----

public enum EstadoPartidaCuentaPorPagar
{
    // Se puede mandar a pagar.
    Disponible = 1,
    // Ya está en un documento de pago en curso (o devuelto, todavía sin decidir).
    EnTramite = 2,
    // El pago ya se completó acá, pero WO todavía no lo cruzó (sigue en la foto con el mismo saldo).
    PagadaPendienteCruce = 3,
    // Saldo a favor de Jimaco o sin renglón de origen en WO — se muestra, no se paga.
    NoPagable = 4
}

public record ResumenCuentasPorPagarDto(
    DateTime? FechaCorte,
    decimal TotalPendiente,
    decimal TotalVencido,
    decimal VenceProximos7Dias,
    decimal AlDia,
    decimal Vencido1a30,
    decimal Vencido31a60,
    decimal VencidoMas60,
    decimal TotalAnticipos,
    decimal TotalEnTramite,
    IReadOnlyList<ProveedorCuentaPorPagarDto> Proveedores);

public record ProveedorCuentaPorPagarDto(
    string Identificacion,
    string Nombre,
    int Facturas,
    // Suma de lo que se le debe (solo partidas con saldo a favor del proveedor).
    decimal Pendiente,
    decimal Vencido,
    // Pagos o notas crédito sin aplicar dentro de sus cuentas por pagar (positivo = a favor de Jimaco).
    decimal SaldoAFavor,
    decimal Anticipos,
    // Lo que realmente se le debe: Pendiente − SaldoAFavor − Anticipos.
    decimal Neto,
    decimal EnTramite,
    DateTime? VencimientoMasAntiguo,
    int DiasMaximoVencido);

public record PartidaCuentaPorPagarDto(
    int? IdDocumentoDetalleWO,
    string? Documento,
    string? Prefijo,
    string? NumeroDocumento,
    string? NumeroDocumentoExterno,
    DateTime Fecha,
    DateTime? Vencimiento,
    // Positivo = días vencida; negativo = días que faltan para vencer; null sin vencimiento.
    int? DiasVencido,
    decimal Pendiente,
    string? CodigoCuenta,
    EstadoPartidaCuentaPorPagar Estado,
    int? PagoInstanciaDocumentoId,
    string? PagoNumeroReferencia);

public record DetalleProveedorCuentaPorPagarDto(
    DateTime? FechaCorte,
    ProveedorCuentaPorPagarDto Proveedor,
    IReadOnlyList<PartidaCuentaPorPagarDto> Partidas,
    int AnticiposMovimientos);

public record CrearPagoProveedorDto(string Identificacion, IReadOnlyList<int> IdsDocumentoDetalleWO, string? Observaciones);

/// <summary>Facturas incluidas en un documento "Pago a Proveedor" — para mostrarlas en su detalle.</summary>
public record FacturaPagoProveedorDto(
    int IdDocumentoDetalleWO,
    string? Prefijo,
    string? NumeroDocumento,
    string? NumeroDocumentoExterno,
    DateTime Fecha,
    DateTime? Vencimiento,
    decimal Valor,
    string? CodigoCuenta);
