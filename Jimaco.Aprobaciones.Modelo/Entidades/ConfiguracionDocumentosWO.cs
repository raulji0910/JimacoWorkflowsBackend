using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Configuración de la creación de documentos en World Office desde el portal que maneja un
/// administrador (2026-11-26, igual que el prefijo del CE en ConfiguracionCuentasPorPagar) — UNA sola
/// fila (Id = 1). El Sincronizador --documentos la pide a la Api en cada corrida. Arranca APAGADA y con
/// el prefijo de PRUEBA "PRUEB": producción tiene que poner "COT" y encenderla explícitamente.
/// </summary>
public class ConfiguracionDocumentosWO
{
    public const int IdUnico = 1;
    public const string PrefijoCotizacionPorDefecto = "PRUEB";

    public int Id { get; set; }

    /// <summary>Prefijo con el que el Sincronizador crea las cotizaciones en WO (COT en producción).</summary>
    [MaxLength(20)]
    public required string PrefijoCotizacion { get; set; }

    /// <summary>false = el Sincronizador NO crea cotizaciones en WO (las enviadas quedan en cola, "Creando en WO…").</summary>
    public bool CrearCotizacionesEnWorldOffice { get; set; }

    public DateTime? FechaActualizacion { get; set; }
    public int? ActualizadoPorUsuarioId { get; set; }
    public Usuario? ActualizadoPorUsuario { get; set; }
}
