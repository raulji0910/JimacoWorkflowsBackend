using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

public class Usuario
{
    public int Id { get; set; }

    [MaxLength(200)]
    public required string Nombre { get; set; }

    [MaxLength(200)]
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    /// <summary>Para notificaciones por WhatsApp. Formato libre (incluir indicativo de país).</summary>
    [MaxLength(30)]
    public string? Telefono { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public ICollection<UsuarioRol> UsuarioRoles { get; set; } = [];
}
