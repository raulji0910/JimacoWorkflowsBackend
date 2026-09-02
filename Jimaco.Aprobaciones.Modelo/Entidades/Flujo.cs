using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Secuencia de pasos de aprobación para un <see cref="TipoDocumento"/>. Solo debería haber
/// una definición Activa por tipo de documento a la vez (lo valida el servicio, no una
/// restricción de BD, para poder tener versiones históricas inactivas).
/// </summary>
public class DefinicionFlujo
{
    public int Id { get; set; }

    [MaxLength(150)]
    public required string Nombre { get; set; }

    public int TipoDocumentoId { get; set; }
    public TipoDocumento TipoDocumento { get; set; } = null!;

    public bool Activo { get; set; } = true;

    public ICollection<PasoFlujo> Pasos { get; set; } = [];
}

/// <summary>
/// Un paso del flujo. El orden de avance ("Aprobar") es siempre al siguiente <see cref="Orden"/>
/// dentro del mismo flujo (o Completado si es el último). "Devolver" es configurable por paso:
/// si <see cref="PasoDestinoDevolucionId"/> es null, el documento vuelve al emisor (estado
/// Devuelto) para que lo reenvíe; si apunta a otro paso, regresa directamente a ese paso.
/// </summary>
public class PasoFlujo
{
    public int Id { get; set; }

    public int DefinicionFlujoId { get; set; }
    public DefinicionFlujo DefinicionFlujo { get; set; } = null!;

    [MaxLength(150)]
    public required string Nombre { get; set; }

    /// <summary>Posición dentro del flujo (1, 2, 3...). Determina a qué paso se avanza al aprobar.</summary>
    public int Orden { get; set; }

    public bool PermiteDevolver { get; set; } = true;

    public bool PermiteRechazar { get; set; }

    public int? PasoDestinoDevolucionId { get; set; }
    public PasoFlujo? PasoDestinoDevolucion { get; set; }

    public ICollection<PasoFlujoRol> PasoFlujoRoles { get; set; } = [];
}

/// <summary>Roles habilitados para actuar en un paso. Cualquier usuario con alguno de estos roles puede aprobar/devolver/rechazar ese paso.</summary>
public class PasoFlujoRol
{
    public int PasoFlujoId { get; set; }
    public PasoFlujo PasoFlujo { get; set; } = null!;

    public int RolId { get; set; }
    public Rol Rol { get; set; } = null!;
}
