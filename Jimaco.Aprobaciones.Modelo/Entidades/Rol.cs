using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Rol de negocio (ej. "Gerente Comercial", "Asistente Contable"). No es un enum fijo:
/// se crea/edita desde la administración, para que agregar un rol nuevo sea configuración,
/// no un cambio de código.
/// </summary>
public class Rol
{
    public int Id { get; set; }

    [MaxLength(100)]
    public required string Nombre { get; set; }

    [MaxLength(300)]
    public string? Descripcion { get; set; }

    public bool Activo { get; set; } = true;

    public ICollection<UsuarioRol> UsuarioRoles { get; set; } = [];
    public ICollection<PasoFlujoRol> PasoFlujoRoles { get; set; } = [];
}

/// <summary>Asignación de un rol a un usuario (muchos a muchos: un usuario puede tener varios roles).</summary>
public class UsuarioRol
{
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    public int RolId { get; set; }
    public Rol Rol { get; set; } = null!;
}
