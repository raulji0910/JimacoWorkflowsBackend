using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>Archivo adjunto a una instancia de documento (ej. el PDF de la OC exportado de World Office).</summary>
public class Adjunto
{
    public int Id { get; set; }

    public int InstanciaDocumentoId { get; set; }
    public InstanciaDocumento InstanciaDocumento { get; set; } = null!;

    [MaxLength(300)]
    public required string NombreArchivo { get; set; }

    /// <summary>Ruta relativa dentro del almacenamiento configurado (no la ruta absoluta del disco).</summary>
    [MaxLength(500)]
    public required string RutaArchivo { get; set; }

    [MaxLength(150)]
    public string? ContentType { get; set; }

    public long TamanoBytes { get; set; }

    public int SubidoPorUsuarioId { get; set; }
    public Usuario SubidoPorUsuario { get; set; } = null!;

    public DateTime FechaCarga { get; set; } = DateTime.UtcNow;
}
