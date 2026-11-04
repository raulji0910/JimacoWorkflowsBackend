using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

public enum TipoAccion
{
    Creado = 1,
    Aprobado = 2,
    Devuelto = 3,
    Rechazado = 4,
    Reenviado = 5,

    /// <summary>
    /// Redirección a discreción hacia un paso más adelante en el mismo flujo (ej. Gerencia General
    /// decide pagar de contado en vez de por transferencia, y el documento pasa a Asistente de
    /// Gerencia para el desembolso) — ver PasoFlujo.PasoRedirigirAPagoContadoId. A diferencia de
    /// Devolver, no es una corrección hacia atrás: el documento sigue su curso normal desde el
    /// paso destino.
    /// </summary>
    RedirigidoAContado = 6
}

/// <summary>Registro inmutable de auditoría: quién hizo qué, cuándo, y con qué comentario, sobre una instancia de documento.</summary>
public class HistorialAccion
{
    public int Id { get; set; }

    public int InstanciaDocumentoId { get; set; }
    public InstanciaDocumento InstanciaDocumento { get; set; } = null!;

    /// <summary>Paso en el que ocurrió la acción. Null solo para el evento inicial "Creado" antes de entrar al primer paso.</summary>
    public int? PasoFlujoId { get; set; }
    public PasoFlujo? PasoFlujo { get; set; }

    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    public TipoAccion Accion { get; set; }

    [MaxLength(1000)]
    public string? Comentario { get; set; }

    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
