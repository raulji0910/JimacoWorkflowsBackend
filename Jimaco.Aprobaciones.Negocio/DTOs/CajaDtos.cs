using Jimaco.Aprobaciones.Modelo.Entidades;

namespace Jimaco.Aprobaciones.Negocio.DTOs;

// ---- Cajas menores (2026-11-27) ----

/// <summary>Una caja con su saldo en tiempo real y lo que puede hacer quien la consulta.</summary>
public record CajaResumenDto(
    int Id,
    Empresa Empresa,
    string Nombre,
    decimal Tope,
    decimal SaldoMinimoAlerta,
    decimal SaldoActual,
    // Plata en manos de alguien: giros abiertos (sin legalizar).
    int GirosAbiertos,
    decimal ValorGirosAbiertos,
    // Vueltas que alguien debe devolver.
    int GirosConVueltaPendiente,
    decimal ValorVueltasPendientes,
    // Cuánto falta para llegar al tope (sugerencia de recarga; 0 si ya está en el tope o más).
    decimal FaltaParaTope,
    bool SaldoBajo,
    bool EsAdministrador,
    bool EsGirador,
    DateTime? FechaActualizacion,
    string? ActualizadoPor);

public record ConceptoCajaDto(int Id, string Nombre, bool Activo);

public record GuardarConceptoCajaDto(string Nombre, bool Activo = true);

public record ActualizarCajaDto(decimal Tope, decimal SaldoMinimoAlerta);

public record BeneficiarioCajaDto(int Id, string Nombre, string Cedula, string? Telefono, string? Correo, bool? EsInterno, bool Activo);

public record GuardarBeneficiarioCajaDto(string Nombre, string Cedula, string? Telefono, string? Correo, bool? EsInterno, bool Activo = true);

public record RegistrarRecargaDto(decimal Valor, string? Nota);

/// <summary>Arqueo: el administrador cuenta el efectivo; si no cuadra con el sistema se ajusta por la diferencia.</summary>
public record RegistrarArqueoDto(decimal EfectivoContado, string? Motivo);

public record ResultadoArqueoDto(decimal SaldoSistema, decimal EfectivoContado, decimal Diferencia, MovimientoCajaDto? Ajuste);

public record CrearGiroCajaDto(
    int BeneficiarioId,
    int ConceptoId,
    decimal ValorEntregado,
    string? Nota,
    // OC de contado hasta $300.000 que se pagan con este giro (pueden ser varias). Vacío = vale normal.
    IReadOnlyList<int>? OrdenesCompraIds);

public record LegalizarGiroCajaDto(decimal ValorGastado, string? Nota);

public record RecibirVueltaDto(decimal Valor, string? Nota);

public record AnularDto(string Motivo);

public record OrdenCompraGiroDto(int InstanciaDocumentoId, string? NumeroReferencia, string? Proveedor, decimal Valor, string? PasoActual);

/// <summary>OC de contado esperando el pago en efectivo del asistente (paso que exige giro) y sin giro todavía.</summary>
public record OrdenCompraPendientePagoDto(int InstanciaDocumentoId, string? NumeroReferencia, string? Proveedor, decimal Valor,
    DateTime FechaCreacion, string PasoActual);

public record MovimientoCajaDto(
    int Id,
    TipoMovimientoCaja Tipo,
    decimal Valor,
    decimal SaldoDespues,
    DateTime Fecha,
    string Usuario,
    string? Nota,
    int? GiroId,
    int? GiroNumero,
    string? Beneficiario,
    string? Concepto,
    bool Anulado,
    string? MotivoAnulacion,
    string? AnuladoPor,
    DateTime? FechaAnulacion);

public record GiroCajaResumenDto(
    int Id,
    int Numero,
    EstadoGiroCaja Estado,
    DateTime Fecha,
    string Beneficiario,
    string BeneficiarioCedula,
    string Concepto,
    decimal ValorEntregado,
    decimal? ValorLegalizado,
    decimal VueltaPendiente,
    string GiradoPor,
    // Horas desde el giro (abierto) o desde la legalización (vuelta pendiente) — para resaltar los atrasados.
    double HorasPendiente,
    IReadOnlyList<string> OrdenesCompra);

public record GiroCajaDto(
    int Id,
    int CajaId,
    Empresa Empresa,
    int Numero,
    EstadoGiroCaja Estado,
    DateTime Fecha,
    BeneficiarioCajaDto Beneficiario,
    ConceptoCajaDto Concepto,
    decimal ValorEntregado,
    string? Nota,
    string GiradoPor,
    decimal? ValorLegalizado,
    DateTime? FechaLegalizacion,
    string? LegalizadoPor,
    string? NotaLegalizacion,
    decimal ValorVueltasRecibidas,
    decimal VueltaPendiente,
    bool TieneSoporte,
    string? SoporteNombreArchivo,
    DateTime? FechaAnulacion,
    string? AnuladoPor,
    string? MotivoAnulacion,
    IReadOnlyList<OrdenCompraGiroDto> OrdenesCompra,
    IReadOnlyList<MovimientoCajaDto> Movimientos);

public record FiltroHistorialCajaDto(
    DateTime? Desde = null,
    DateTime? Hasta = null,
    TipoMovimientoCaja? Tipo = null,
    int? BeneficiarioId = null,
    int? ConceptoId = null,
    string? Texto = null,
    bool IncluirAnulados = true,
    int Pagina = 1,
    int TamanoPagina = 50);

/// <summary>Paso de un flujo activo de OC de la empresa: el administrador marca cuál se paga con la caja (exige giro al aprobar).</summary>
public record PasoPagoCajaDto(int PasoId, string Flujo, int Orden, string Paso, bool RequiereGiroCaja, int OrdenesEnElPaso);

public record MarcarPasoPagoCajaDto(bool RequiereGiroCaja);

public record HistorialCajaDto(IReadOnlyList<MovimientoCajaDto> Movimientos, int Total, decimal TotalEntradas, decimal TotalSalidas);
