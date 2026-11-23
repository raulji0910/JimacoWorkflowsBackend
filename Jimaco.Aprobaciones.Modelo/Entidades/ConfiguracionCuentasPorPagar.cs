using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Configuración de Cuentas por Pagar que maneja un administrador desde la página (2026-11-11) —
/// UNA sola fila (Id = 1). Antes el prefijo del CE vivía en el appsettings del Sincronizador; ahora
/// el Sincronizador la pide a la Api en cada corrida (api/sincronizacion/pagos-proveedor/configuracion-ce).
/// Arranca APAGADA y con el prefijo de PRUEBA "CEJ-2" a propósito: producción tiene que poner "CEJ"
/// y encenderla explícitamente el día que se empiece a usar.
/// </summary>
public class ConfiguracionCuentasPorPagar
{
    public const int IdUnico = 1;
    public const string PrefijoCEPorDefecto = "CEJ-2";

    /// <summary>Las mismas cuentas que el Sincronizador usaba por defecto antes de que esto fuera configurable.</summary>
    public const string CuentasIncluidasPorDefecto = "2205,2210,2335";

    public int Id { get; set; }

    /// <summary>Prefijo con el que el Sincronizador crea los Comprobantes de Egreso en World Office.</summary>
    [MaxLength(20)]
    public required string PrefijoCE { get; set; }

    /// <summary>false = el Sincronizador NO crea CE en World Office (los pagos quedan en cola).</summary>
    public bool CrearCEEnWorldOffice { get; set; }

    /// <summary>
    /// Cuentas contables de WO que entran a Cuentas por Pagar (2026-11-23), separadas por coma: cada
    /// una es un PREFIJO del código ("2205" toma 220501, 220505…; "233540" solo esa subcuenta). La Api
    /// descarta al guardar la foto lo que no empiece por ninguna, y el Sincronizador nuevo las usa
    /// para leer de WO (el viejo seguía leyendo sus valores fijos — la Api filtra igual).
    /// </summary>
    [MaxLength(300)]
    public string CuentasIncluidas { get; set; } = CuentasIncluidasPorDefecto;

    /// <summary>"2205, 2210" → ["2205", "2210"] (sin vacíos ni repetidos).</summary>
    public static IReadOnlyList<string> SepararCuentas(string? cuentas) =>
        (cuentas ?? "").Split([',', ';', ' '], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct().ToList();

    public DateTime? FechaActualizacion { get; set; }
    public int? ActualizadoPorUsuarioId { get; set; }
    public Usuario? ActualizadoPorUsuario { get; set; }
}
