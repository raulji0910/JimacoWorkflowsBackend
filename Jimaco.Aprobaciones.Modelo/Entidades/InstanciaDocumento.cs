using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

public enum EstadoInstanciaDocumento
{
    /// <summary>Tiene un paso actual pendiente de acción.</summary>
    EnProceso = 1,

    /// <summary>Devuelta al emisor (sin paso actual) — el emisor debe reenviarla para que reingrese al flujo desde el primer paso.</summary>
    Devuelto = 2,

    /// <summary>Recorrió todos los pasos y fue aprobada en el último.</summary>
    Completado = 3,

    /// <summary>Rechazada de forma terminal en algún paso.</summary>
    Rechazado = 4
}

/// <summary>
/// Un documento concreto (ej. la OC #123) avanzando por su flujo. Los campos fijos
/// (NumeroReferencia, Proveedor, Valor, FechaDocumento) están indexados/tipados para
/// poder filtrar y reportar sin tener que parsear JSON; <see cref="DatosJson"/> guarda
/// los valores de los <see cref="CampoTipoDocumento"/> propios de cada tipo de documento.
/// </summary>
public class InstanciaDocumento
{
    public int Id { get; set; }

    public int TipoDocumentoId { get; set; }
    public TipoDocumento TipoDocumento { get; set; } = null!;

    public int DefinicionFlujoId { get; set; }
    public DefinicionFlujo DefinicionFlujo { get; set; } = null!;

    /// <summary>Null cuando Estado es Devuelto, Completado o Rechazado.</summary>
    public int? PasoActualId { get; set; }
    public PasoFlujo? PasoActual { get; set; }

    public EstadoInstanciaDocumento Estado { get; set; } = EstadoInstanciaDocumento.EnProceso;

    [MaxLength(100)]
    public string? NumeroReferencia { get; set; }

    [MaxLength(200)]
    public string? Proveedor { get; set; }

    public decimal? Valor { get; set; }

    public DateTime? FechaDocumento { get; set; }

    /// <summary>Valores de los campos dinámicos del tipo de documento, como objeto JSON {"clave": "valor"}.</summary>
    public string? DatosJson { get; set; }

    public int CreadoPorUsuarioId { get; set; }
    public Usuario CreadoPorUsuario { get; set; } = null!;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    // ---- Origen en World Office (Fase 2, solo se llena en documentos que trajo el Sincronizador;
    // null en los creados a mano desde el formulario) ----

    /// <summary>
    /// PK real de la fila en <c>[CuentasContables - Asientos]</c> (columna <c>IdAsientoContable</c>)
    /// de la que se sincronizó este documento — es lo único que identifica la fila exacta sin
    /// ambigüedad (<see cref="NumeroReferencia"/> por sí solo NO alcanza, un mismo número existe a
    /// la vez en distintos prefijos). Se usa para escribir la aprobación de vuelta en WO.
    /// </summary>
    public int? IdAsientoContableOrigen { get; set; }

    /// <summary>
    /// El <c>prefijo</c> de WO de esa misma fila (ej. "OC") — puede haber más de un prefijo que
    /// representa Órdenes de Compra en WO (incluso uno propio de pruebas), así que no alcanza con
    /// asumir "OC" siempre. Se guarda también como chequeo de seguridad al escribir de vuelta: el
    /// `UPDATE` valida `IdAsientoContable` Y `prefijo` juntos antes de tocar la fila, nunca solo el Id.
    /// </summary>
    [MaxLength(20)]
    public string? PrefijoOrigen { get; set; }

    /// <summary>
    /// true cuando el primer paso (Orden=1) del flujo ya se aprobó en este documento Y tiene
    /// origen de WO, pero todavía no se confirmó que la aprobación se escribió allá — el
    /// Sincronizador revisa esta cola en cada corrida (ver Jimaco.Aprobaciones.Sincronizador).
    /// </summary>
    public bool PendienteEscrituraWO { get; set; }

    /// <summary>
    /// Mensaje cuando el Sincronizador encontró un conflicto al intentar escribir en WO (ej. la
    /// fila ya estaba anulada allá) — queda visible en el detalle del documento en vez de
    /// reintentarse indefinidamente. Null mientras no haya conflicto.
    /// </summary>
    [MaxLength(500)]
    public string? ConflictoWO { get; set; }

    public DateTime? FechaEscrituraWO { get; set; }

    public ICollection<Adjunto> Adjuntos { get; set; } = [];
    public ICollection<HistorialAccion> Historial { get; set; } = [];
    public ICollection<RenglonInstanciaDocumento> Renglones { get; set; } = [];
}
