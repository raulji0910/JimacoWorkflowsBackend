using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Caja menor de efectivo de una empresa (2026-11-27). Una por empresa (Jimaco y Ferrealiados), se
/// crean con la migración. Antes se manejaba con vales de papel sin cuadre: acá todo sale y entra
/// como <see cref="MovimientoCaja"/> y el saldo se lleva en <see cref="SaldoActual"/>. Solo control
/// en el portal: NADA se escribe en World Office (fase 1, decisión del usuario).
/// </summary>
public class Caja
{
    public int Id { get; set; }

    public Empresa Empresa { get; set; }

    [MaxLength(100)]
    public required string Nombre { get; set; }

    /// <summary>Monto del fondo (Jimaco ~2.000.000): referencia para recargar, lo maneja el administrador.</summary>
    public decimal Tope { get; set; }

    /// <summary>Si el efectivo baja de esto se avisa a los administradores para recargar (0 = sin aviso).</summary>
    public decimal SaldoMinimoAlerta { get; set; }

    /// <summary>Efectivo que debería haber físicamente = suma con signo de los movimientos no anulados.</summary>
    public decimal SaldoActual { get; set; }

    /// <summary>
    /// Se cambia en cada movimiento: si dos personas giran a la vez, la segunda choca (concurrencia
    /// optimista) en vez de dejar la caja en negativo. Funciona igual en SQL Server y en las pruebas.
    /// </summary>
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();

    /// <summary>Último aviso de saldo bajo: se avisa una vez cada vez que el saldo cruza el mínimo.</summary>
    public bool AvisoSaldoBajoEnviado { get; set; }

    public DateTime? FechaActualizacion { get; set; }
    public int? ActualizadoPorUsuarioId { get; set; }
    public Usuario? ActualizadoPorUsuario { get; set; }

    public ICollection<ConceptoCaja> Conceptos { get; set; } = [];
    public ICollection<MovimientoCaja> Movimientos { get; set; } = [];
    public ICollection<GiroCaja> Giros { get; set; } = [];
}

/// <summary>Concepto de los giros (ej. "Transporte", "Papelería"). Los crea el administrador de la caja.</summary>
public class ConceptoCaja
{
    public int Id { get; set; }
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;

    [MaxLength(100)]
    public required string Nombre { get; set; }

    public bool Activo { get; set; } = true;
}

/// <summary>
/// Persona a la que se le entrega efectivo (sobre todo gente de Logística). La crea el girador desde
/// el mismo giro y queda para la próxima vez. Compartida entre las dos cajas.
/// </summary>
public class BeneficiarioCaja
{
    public int Id { get; set; }

    [MaxLength(150)]
    public required string Nombre { get; set; }

    [MaxLength(20)]
    public required string Cedula { get; set; }

    [MaxLength(30)]
    public string? Telefono { get; set; }

    [MaxLength(150)]
    public string? Correo { get; set; }

    /// <summary>Interno (empleado) o externo (mensajero, proveedor). Null = no se indicó.</summary>
    public bool? EsInterno { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
    public int CreadoPorUsuarioId { get; set; }
    public Usuario CreadoPorUsuario { get; set; } = null!;
}

public enum TipoMovimientoCaja
{
    /// <summary>Entra plata nueva (gerencia repone desde el banco). Solo el administrador.</summary>
    Recarga = 1,
    /// <summary>Sale efectivo a una persona (el "vale"). Ver <see cref="GiroCaja"/>.</summary>
    Giro = 2,
    /// <summary>Regresa el sobrante de un giro (cierra el giro).</summary>
    Vuelta = 3,
    /// <summary>La persona gastó más de lo girado y se le devuelve la diferencia.</summary>
    Reintegro = 4,
    /// <summary>Diferencia del arqueo (conteo físico vs. sistema), con motivo. Solo el administrador.</summary>
    Ajuste = 5
}

/// <summary>
/// Todo lo que entra o sale de la caja. <see cref="Valor"/> va CON SIGNO (+ entra, − sale), así el
/// saldo es la suma. Nunca se borra: un error se anula con motivo y queda en el historial.
/// </summary>
public class MovimientoCaja
{
    public int Id { get; set; }
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;

    public TipoMovimientoCaja Tipo { get; set; }

    /// <summary>Con signo: + entra (recarga, vuelta, ajuste a favor), − sale (giro, reintegro, ajuste en contra).</summary>
    public decimal Valor { get; set; }

    /// <summary>Saldo de la caja justo después de este movimiento (para el historial).</summary>
    public decimal SaldoDespues { get; set; }

    public DateTime Fecha { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    [MaxLength(500)]
    public string? Nota { get; set; }

    /// <summary>El giro al que pertenece (Giro, Vuelta, Reintegro). Null en recargas y ajustes.</summary>
    public int? GiroCajaId { get; set; }
    public GiroCaja? GiroCaja { get; set; }

    public bool Anulado { get; set; }
    public DateTime? FechaAnulacion { get; set; }
    public int? AnuladoPorUsuarioId { get; set; }
    public Usuario? AnuladoPorUsuario { get; set; }

    [MaxLength(500)]
    public string? MotivoAnulacion { get; set; }
}

public enum EstadoGiroCaja
{
    /// <summary>La persona tiene la plata y no ha dicho cuánto gastó.</summary>
    Abierto = 1,
    /// <summary>Legalizado: gastó menos de lo girado y debe devolver la diferencia.</summary>
    VueltaPendiente = 2,
    /// <summary>Cuadrado: legalizado y sin nada pendiente.</summary>
    Cerrado = 3,
    Anulado = 4
}

/// <summary>
/// El vale: efectivo que se le entrega a una persona por un concepto. Ciclo: Abierto → legalización
/// (cuánto gastó) → Cerrado, o VueltaPendiente hasta que entra la vuelta. Si gastó más, el reintegro
/// se paga al legalizar. Un giro puede pagar OC de contado hasta $300.000 (<see cref="OrdenesCompra"/>):
/// ahí se legaliza solo por la suma de las OC (ej. 500.000 para OC de 450.000 → 50.000 de vuelta).
/// </summary>
public class GiroCaja
{
    public int Id { get; set; }
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;

    /// <summary>Consecutivo por caja (Giro #1, #2… de Jimaco; aparte los de Ferrealiados).</summary>
    public int Numero { get; set; }

    public int BeneficiarioCajaId { get; set; }
    public BeneficiarioCaja BeneficiarioCaja { get; set; } = null!;

    public int ConceptoCajaId { get; set; }
    public ConceptoCaja ConceptoCaja { get; set; } = null!;

    public decimal ValorEntregado { get; set; }

    [MaxLength(500)]
    public string? Nota { get; set; }

    public EstadoGiroCaja Estado { get; set; } = EstadoGiroCaja.Abierto;

    public DateTime Fecha { get; set; }
    public int GiradoPorUsuarioId { get; set; }
    public Usuario GiradoPorUsuario { get; set; } = null!;

    /// <summary>Lo que realmente se gastó (al legalizar).</summary>
    public decimal? ValorLegalizado { get; set; }
    public DateTime? FechaLegalizacion { get; set; }
    public int? LegalizadoPorUsuarioId { get; set; }
    public Usuario? LegalizadoPorUsuario { get; set; }

    [MaxLength(500)]
    public string? NotaLegalizacion { get; set; }

    /// <summary>Vueltas recibidas hasta ahora (pueden llegar en partes).</summary>
    public decimal ValorVueltasRecibidas { get; set; }

    /// <summary>Foto opcional del soporte (factura/recibo) subida al legalizar.</summary>
    [MaxLength(300)]
    public string? SoporteNombreArchivo { get; set; }
    [MaxLength(500)]
    public string? SoporteRutaArchivo { get; set; }
    [MaxLength(150)]
    public string? SoporteContentType { get; set; }

    /// <summary>Último recordatorio (giro sin legalizar o vuelta sin entregar), cada 24 horas.</summary>
    public DateTime? UltimoRecordatorio { get; set; }

    public DateTime? FechaAnulacion { get; set; }
    public int? AnuladoPorUsuarioId { get; set; }
    public Usuario? AnuladoPorUsuario { get; set; }
    [MaxLength(500)]
    public string? MotivoAnulacion { get; set; }

    public ICollection<GiroCajaOrdenCompra> OrdenesCompra { get; set; } = [];
    public ICollection<MovimientoCaja> Movimientos { get; set; } = [];

    /// <summary>Vuelta que falta por entregar (0 si no hay).</summary>
    public decimal VueltaPendiente => Estado == EstadoGiroCaja.VueltaPendiente
        ? Math.Max(0, ValorEntregado - (ValorLegalizado ?? 0) - ValorVueltasRecibidas)
        : 0;
}

/// <summary>OC de contado (hasta $300.000) pagada con un giro. <see cref="Valor"/> = valor de la OC al girar.</summary>
public class GiroCajaOrdenCompra
{
    public int GiroCajaId { get; set; }
    public GiroCaja GiroCaja { get; set; } = null!;

    public int InstanciaDocumentoId { get; set; }
    public InstanciaDocumento InstanciaDocumento { get; set; } = null!;

    public decimal Valor { get; set; }
}
