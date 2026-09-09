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

    /// <summary>
    /// Usuario de World Office de esta persona (texto libre, como lo guarda WO en
    /// "IdTerceroAprobador" — NO es un Id numérico a pesar del nombre en WO, ver
    /// esquema-worldoffice-oc.md). Se usa para que, cuando esta persona apruebe el primer paso de
    /// un documento que vino sincronizado desde WO, la aprobación se refleje allá a nombre de su
    /// propio usuario real — no de uno genérico. Null si la persona no tiene usuario en WO (ej. no
    /// participa nunca de documentos sincronizados), en cuyo caso se usa un texto de respaldo.
    /// </summary>
    [MaxLength(100)]
    public string? UsuarioWO { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    public ICollection<UsuarioRol> UsuarioRoles { get; set; } = [];
}
