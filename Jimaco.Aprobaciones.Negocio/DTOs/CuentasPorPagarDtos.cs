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

/// <summary>
/// Un anticipo sin cruzar (cuenta de anticipos), por partida: <see cref="IdDocumentoDetalleWO"/> es
/// el renglón que lo abrió y <see cref="Saldo"/> lo que le queda (Débito − Crédito de todos los
/// renglones que apuntan a él). Prefijo/número/fecha/nota son del documento de origen.
/// </summary>
public record AnticipoProveedorInputDto(
    string Identificacion,
    string NombreTercero,
    int Movimientos,
    decimal Saldo,
    int? IdDocumentoDetalleWO = null,
    string? Prefijo = null,
    string? NumeroDocumento = null,
    DateTime? Fecha = null,
    string? Nota = null);

/// <summary>Una cuenta de banco/caja de WO desde donde se puede pagar.</summary>
public record CuentaBancoInputDto(int IdCuentaContableWO, string Codigo, string Nombre, bool PorDefecto);

public record FotoCuentasPorPagarDto(
    IReadOnlyList<PartidaCuentaPorPagarInputDto> Partidas,
    IReadOnlyList<AnticipoProveedorInputDto> Anticipos,
    // Null = el Sincronizador es de una versión vieja que no la manda: se conserva la lista anterior.
    IReadOnlyList<CuentaBancoInputDto>? Bancos = null);

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

/// <summary>Un anticipo abierto del proveedor, para elegir si se descuenta en el pago.</summary>
public record AnticipoPartidaDto(
    int? IdDocumentoDetalleWO,
    string? Prefijo,
    string? NumeroDocumento,
    DateTime? Fecha,
    string? Nota,
    decimal Saldo,
    // Disponible / EnTramite (ya está en otro pago) / PagadaPendienteCruce / NoPagable (sin partida de origen).
    EstadoPartidaCuentaPorPagar Estado,
    int? PagoInstanciaDocumentoId,
    string? PagoNumeroReferencia);

public record DetalleProveedorCuentaPorPagarDto(
    DateTime? FechaCorte,
    ProveedorCuentaPorPagarDto Proveedor,
    IReadOnlyList<PartidaCuentaPorPagarDto> Partidas,
    int AnticiposMovimientos,
    IReadOnlyList<AnticipoPartidaDto> Anticipos);

public record CrearPagoProveedorDto(
    string Identificacion,
    IReadOnlyList<int> IdsDocumentoDetalleWO,
    string? Observaciones,
    // Anticipos a descontar en el mismo CE (se aplican del más viejo al más nuevo, hasta cubrir las facturas).
    IReadOnlyList<int>? IdsAnticiposWO = null);

public record CuentaBancoDto(int IdCuentaContableWO, string Codigo, string Nombre, bool PorDefecto);

public record AnticipoPagoProveedorDto(int IdDocumentoDetalleWO, string? Prefijo, string? NumeroDocumento, DateTime? Fecha, string? Nota, decimal Valor);

/// <summary>Lo que se ve en el detalle de un documento "Pago a Proveedor" sobre el pago real y su CE en WO.</summary>
public record PagoProveedorDto(
    string Identificacion,
    decimal TotalFacturas,
    decimal TotalAnticipos,
    decimal Neto,
    IReadOnlyList<AnticipoPagoProveedorDto> Anticipos,
    int? IdCuentaBancoWO,
    string? NombreBanco,
    DateTime? FechaPago,
    bool PendienteCreacionCE,
    string? ConflictoCreacionCE,
    string? PrefijoCE,
    decimal? NumeroCE,
    DateTime? FechaCreacionCE,
    bool EsExtraordinario = false,
    string? NombreTercero = null,
    string? NombreTipoGiro = null,
    string? Concepto = null,
    int? NumeroRelacion = null,
    int? NumeroLote = null);

// ---- Creación del CE en WO (lo usa solo el Sincronizador) ----

public record FacturaCreacionCEDto(int IdDocumentoDetalleWO, string? Prefijo, string? NumeroDocumento, string? NumeroDocumentoExterno, decimal Valor);

public record AnticipoCreacionCEDto(int IdDocumentoDetalleWO, string? Prefijo, string? NumeroDocumento, decimal Valor);

public record PendienteCreacionCEDto(
    int InstanciaDocumentoId,
    string? NumeroReferencia,
    string Identificacion,
    DateTime FechaPago,
    int? IdCuentaBancoWO,
    decimal Neto,
    IReadOnlyList<FacturaCreacionCEDto> Facturas,
    IReadOnlyList<AnticipoCreacionCEDto> Anticipos,
    // Solo pagos extraordinarios (sin facturas): a quién, qué cuenta se debita y por qué.
    GiroCreacionCEDto? Giro = null);

/// <summary>Pago extraordinario para el CE: débito a la cuenta del tipo de giro (autorreferenciado si es anticipo), crédito al banco.</summary>
public record GiroCreacionCEDto(int IdTerceroWO, string Identificacion, string CodigoCuenta, bool EsAnticipo, string TipoGiro, string Concepto, decimal Valor);

public record CECreadoDto(string Prefijo, decimal Numero, int IdAsientoContable);

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

// ---- Relación de pagos (2026-11-09): Gerencia Comercial marca facturas de varios proveedores en
// un borrador, revisa y ratifica de una vez → una orden por proveedor y UN solo correo al pagador. ----

/// <summary>Lo elegido de UN proveedor en el borrador. Sin facturas = se quita el proveedor del borrador.</summary>
public record GuardarBorradorProveedorDto(IReadOnlyList<int> IdsFacturas, IReadOnlyList<int> IdsAnticipos);

public record BorradorFacturaDto(int IdDocumentoDetalleWO, string Etiqueta, string? NumeroDocumentoExterno, DateTime? Vencimiento, decimal Valor, bool Disponible);

public record BorradorAnticipoDto(int IdDocumentoDetalleWO, string Etiqueta, string? Nota, decimal Valor, bool Disponible);

public record BorradorProveedorDto(
    string Identificacion,
    string Nombre,
    IReadOnlyList<BorradorFacturaDto> Facturas,
    IReadOnlyList<BorradorAnticipoDto> Anticipos,
    decimal TotalFacturas,
    decimal TotalAnticipos,
    decimal Neto,
    // Si algo del borrador ya no se puede pagar (WO lo cruzó, o ya está en otro pago) — hay que corregirlo antes de ratificar.
    string? Problema);

public record RelacionBorradorDto(IReadOnlyList<BorradorProveedorDto> Proveedores, int Facturas, decimal Total);

public record RatificarRelacionDto(string? Observaciones);

public record RelacionCreadaDto(int Numero, int Ordenes, decimal Total);

// ---- Pago extraordinario (2026-11-09) ----

public record TipoGiroDto(int Id, string Nombre, bool EsAnticipo, string CodigoCuenta = "", string? Grupo = null);

public record TerceroWODto(int IdTerceroWO, string Identificacion, string Nombre);

public record TerceroWOInputDto(int IdTerceroWO, string Identificacion, string Nombre);

public record CrearPagoExtraordinarioDto(int IdTerceroWO, decimal Valor, int TipoGiroId, string Concepto);

// ---- Menú "Pagos" del pagador (2026-11-09) ----

public record LineaOrdenPagoDto(string Etiqueta, string? Detalle, decimal Valor);

/// <summary>Filtros de las listas paginadas de pagos (querystring). Fechas = días de Colombia.
/// Estado: PorPagar | Pagado | Rechazado | null (todos).</summary>
public record FiltroPagosDto(string? Estado = null, DateTime? Desde = null, DateTime? Hasta = null, string? Buscar = null,
    int Pagina = 1, int? TamanoPagina = null);

public record PaginaDto<T>(IReadOnlyList<T> Items, int Total, int Pagina, int TamanoPagina);

public record ConteoEstadoDto(int Cantidad, decimal Monto);

/// <summary>Una relación de pagos (o una orden suelta / pago extraordinario) dentro de una página del seguimiento.</summary>
public record GrupoSeguimientoDto(string Clave, int? NumeroRelacion, bool EsExtraordinario, string? CreadoPor, DateTime Fecha,
    IReadOnlyList<SeguimientoPagoDto> Ordenes);

/// <summary>La página pide GRUPOS (relaciones completas), nunca parte una relación en dos páginas.
/// Los conteos son de todo lo que cumple fechas + búsqueda, sin importar la página ni el estado elegido.</summary>
public record SeguimientoPaginaDto(IReadOnlyList<GrupoSeguimientoDto> Grupos, int Total, int Pagina, int TamanoPagina,
    ConteoEstadoDto PorPagar, ConteoEstadoDto Pagados, ConteoEstadoDto Rechazados, ConteoEstadoDto Todos);

/// <summary>"Seguimiento de pagos" de Gerencia Comercial: qué pasó con cada orden que mandó pagar.
/// Estado: PorPagar | Pagado | Rechazado | Otro.</summary>
public record SeguimientoPagoDto(
    int InstanciaDocumentoId,
    string? NumeroReferencia,
    int? NumeroRelacion,
    bool EsExtraordinario,
    string Identificacion,
    string Tercero,
    decimal Neto,
    DateTime FechaCreacion,
    string? CreadoPor,
    string Estado,
    int? NumeroLote,
    DateTime? FechaPago,
    string? Banco,
    string? PrefijoCE,
    decimal? NumeroCE,
    bool PendienteCreacionCE,
    string? ConflictoCreacionCE,
    string? MotivoRechazo,
    string? RechazadoPor,
    DateTime? FechaRechazo,
    IReadOnlyList<LineaOrdenPagoDto> Lineas);

public record OrdenPagoPendienteDto(
    int InstanciaDocumentoId,
    string? NumeroReferencia,
    bool EsExtraordinario,
    string Identificacion,
    string Tercero,
    DateTime FechaCreacion,
    string? CreadoPor,
    int? NumeroRelacion,
    string? Observaciones,
    IReadOnlyList<LineaOrdenPagoDto> Lineas,
    decimal Neto);

public record RechazarOrdenPagoDto(string Comentario);

public record LoteOrdenDto(int InstanciaDocumentoId, string? NumeroReferencia, string Tercero, bool EsExtraordinario, decimal Neto,
    string? PrefijoCE, decimal? NumeroCE, bool PendienteCreacionCE, string? ConflictoCreacionCE);

public record LotePagoDto(int Id, int Numero, DateTime Fecha, DateTime FechaPago, string? NombreBanco, decimal Total, string? PagadoPor,
    bool TieneSoporte, IReadOnlyList<LoteOrdenDto> Ordenes);
