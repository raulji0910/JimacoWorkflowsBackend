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

    public int Id { get; set; }

    /// <summary>Prefijo con el que el Sincronizador crea los Comprobantes de Egreso en World Office.</summary>
    [MaxLength(20)]
    public required string PrefijoCE { get; set; }

    /// <summary>false = el Sincronizador NO crea CE en World Office (los pagos quedan en cola).</summary>
    public bool CrearCEEnWorldOffice { get; set; }

    public DateTime? FechaActualizacion { get; set; }
    public int? ActualizadoPorUsuarioId { get; set; }
    public Usuario? ActualizadoPorUsuario { get; set; }
}
