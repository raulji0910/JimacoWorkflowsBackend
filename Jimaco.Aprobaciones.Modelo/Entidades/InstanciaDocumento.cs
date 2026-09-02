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

    public ICollection<Adjunto> Adjuntos { get; set; } = [];
    public ICollection<HistorialAccion> Historial { get; set; } = [];
}
