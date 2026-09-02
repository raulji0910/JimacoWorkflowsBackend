using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

public enum CanalNotificacion
{
    Email = 1,
    WhatsApp = 2,
    EnApp = 3
}

public enum EstadoNotificacion
{
    Pendiente = 1,
    Enviada = 2,
    Fallida = 3
}

/// <summary>
/// Cola de notificaciones a enviar cuando una instancia entra a un paso nuevo. El envío real
/// por correo/WhatsApp es un proveedor externo pendiente de definir (ver CLAUDE.md) — por ahora
/// esta tabla solo registra la intención y sirve como bandeja "EnApp" dentro del propio sistema.
/// </summary>
public class Notificacion
{
    public int Id { get; set; }

    public int InstanciaDocumentoId { get; set; }
    public InstanciaDocumento InstanciaDocumento { get; set; } = null!;

    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    public CanalNotificacion Canal { get; set; }

    [MaxLength(1000)]
    public required string Mensaje { get; set; }

    public EstadoNotificacion Estado { get; set; } = EstadoNotificacion.Pendiente;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public DateTime? FechaEnvio { get; set; }

    /// <summary>true una vez el destinatario la marcó como leída en la bandeja interna (canal EnApp).</summary>
    public bool Leida { get; set; }
}
