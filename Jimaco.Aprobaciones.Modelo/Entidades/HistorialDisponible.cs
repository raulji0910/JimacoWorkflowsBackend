using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Bitácora de cada cambio de "Disponible" en una Hoja de Alistamiento (2026-11-09, pedido real tras
/// la Hoja 16100: "se borró lo que marcamos" — sin bitácora no había forma de saber si fue la
/// persona, la pantalla o una edición en World Office). Se guarda por Cotización (Prefijo+Número),
/// no por copia: las copias de un mismo grupo son réplicas, así que se registra una sola vez.
///
/// Además sirve de "depósito" para el progreso de un ítem que desaparece de la Cotización en WO
/// (Origen = RetiradoEnWO): si el ítem vuelve a aparecer con el mismo Código, se le devuelve lo que
/// ya se había marcado (CantidadPorRestaurar), en vez de arrancar otra vez en 0.
/// </summary>
public class HistorialDisponible
{
    public const string OrigenUsuario = "Usuario";
    public const string OrigenRefrescoWO = "RefrescoWO";
    public const string OrigenRetiradoEnWO = "RetiradoEnWO";
    public const string OrigenRestauradoDesdeWO = "RestauradoDesdeWO";

    public int Id { get; set; }

    [MaxLength(20)]
    public required string PrefijoOrigen { get; set; }

    [MaxLength(50)]
    public required string NumeroReferencia { get; set; }

    [MaxLength(50)]
    public string? Codigo { get; set; }

    [MaxLength(300)]
    public string? Descripcion { get; set; }

    public decimal CantidadAnterior { get; set; }

    public decimal CantidadNueva { get; set; }

    [MaxLength(30)]
    public required string Origen { get; set; }

    public int? UsuarioId { get; set; }

    [MaxLength(200)]
    public string? NombreUsuario { get; set; }

    /// <summary>Solo para Origen = RetiradoEnWO: lo que todavía se le puede devolver al ítem si reaparece.</summary>
    public decimal? CantidadPorRestaurar { get; set; }

    public DateTime Fecha { get; set; }
}
