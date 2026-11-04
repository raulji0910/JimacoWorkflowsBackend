using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Secuencia de pasos de aprobación para un <see cref="TipoDocumento"/>. Puede haber VARIAS
/// definiciones Activas a la vez para el mismo tipo de documento (2026-09-21, ver
/// <see cref="ValorMinimo"/>/<see cref="ValorMaximo"/>/<see cref="FormaDePagoRequerida"/>) — cada
/// una cubre un rango de condiciones distinto (ej. "OC a crédito" vs "OC a contado menor a
/// $300.000"), y el servicio elige cuál aplica al crear el documento, según su Valor y su forma de
/// pago real. No deberían solaparse dos definiciones activas del mismo tipo de documento para el
/// mismo caso — lo valida el servicio, no una restricción de BD.
/// </summary>
public class DefinicionFlujo
{
    public int Id { get; set; }

    [MaxLength(150)]
    public required string Nombre { get; set; }

    public int TipoDocumentoId { get; set; }
    public TipoDocumento TipoDocumento { get; set; } = null!;

    public bool Activo { get; set; } = true;

    /// <summary>Si está, este flujo solo aplica cuando Valor &gt;= ValorMinimo. Null = sin piso.</summary>
    public decimal? ValorMinimo { get; set; }

    /// <summary>Si está, este flujo solo aplica cuando Valor &lt;= ValorMaximo. Null = sin techo.</summary>
    public decimal? ValorMaximo { get; set; }

    /// <summary>
    /// Si está, este flujo solo aplica cuando Datos["formaDePago"] coincide exacto (ej. "Contado",
    /// "Credito" — los valores reales que manda World Office, sin tildes). Null = cualquier forma
    /// de pago.
    /// </summary>
    [MaxLength(50)]
    public string? FormaDePagoRequerida { get; set; }

    public ICollection<PasoFlujo> Pasos { get; set; } = [];
}

/// <summary>
/// Un paso del flujo. El orden de avance ("Aprobar") es siempre al siguiente <see cref="Orden"/>
/// dentro del mismo flujo (o Completado si es el último). "Devolver" es configurable por paso, y
/// las dos formas se combinan (2026-09-21, caso real: "Gerencia General" necesitaba poder devolver
/// tanto al emisor como a dos pasos anteriores puntuales, no una cosa u la otra):
/// <see cref="PermiteDevolverAlEmisor"/> agrega "El emisor" como opción, y <see
/// cref="DestinosDevolucion"/> agrega pasos anteriores puntuales — el total de opciones es la
/// suma de ambas. Con una sola opción en total no hace falta elegir (se usa esa); con más de una,
/// quien devuelve elige a cuál.
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

    /// <summary>Si "Devolver" ofrece "El emisor" como una de las opciones (se combina con <see cref="DestinosDevolucion"/>).</summary>
    public bool PermiteDevolverAlEmisor { get; set; } = true;

    /// <summary>
    /// Si "Aprobar" en este paso exige escribir el número de un Comprobante de Egreso de WO (ej.
    /// "Área Contable" antes de un giro bancario) — ver <see
    /// cref="InstanciaDocumento.NumeroComprobanteEgresoSolicitado"/>. El paso avanza igual al
    /// aprobar; la notificación al paso siguiente se pospone hasta que el Sincronizador lo traiga.
    /// </summary>
    public bool RequiereComprobanteEgresoAlAprobar { get; set; }

    /// <summary>
    /// Si "Aprobar" en este paso exige un comentario obligatorio (ej. "Asistente de Gerencia" —
    /// necesita dejar constancia de a quién le entregó el dinero). A diferencia de
    /// Devolver/Rechazar (que YA piden comentario obligatorio siempre), en Aprobar es opcional por
    /// default — este flag lo vuelve obligatorio solo en los pasos que lo necesiten. El comentario
    /// se guarda igual que cualquier otro (ver HistorialAccion.Comentario), visible en el historial
    /// y en el PDF.
    /// </summary>
    public bool RequiereComentarioAlAprobar { get; set; }

    /// <summary>
    /// Si "Aprobar" en este paso exige adjuntar un PDF con el soporte del pago antes de dejar
    /// aprobar (2026-09-29, caso real: "Gerencia General" al hacer la transferencia) — ver <see
    /// cref="InstanciaDocumento.SoportePagoAdjuntoId"/>. Al aprobar con éxito se le avisa por
    /// correo a Contabilidad y al emisor real de la OC, con el PDF adjunto de verdad.
    /// </summary>
    public bool RequiereSoportePagoAlAprobar { get; set; }

    /// <summary>Pasos anteriores puntuales a los que este paso puede devolver — ver <see cref="PasoFlujoDestinoDevolucion"/>.</summary>
    public ICollection<PasoFlujoDestinoDevolucion> DestinosDevolucion { get; set; } = [];

    /// <summary>
    /// Si está, "Aprobar" en este paso también ofrece una acción alternativa —"Redirigir a pago de
    /// contado"— que manda el documento a este OTRO paso del mismo flujo en vez de seguir el avance
    /// normal por Orden (caso real 2026-09-25: "Gerencia General" recibe la OC para pagar por
    /// transferencia, pero a discreción puede decidir pagarla de contado; el documento pasa a
    /// "Asistente de Gerencia" para el desembolso en efectivo, insertado ANTES del paso final de
    /// Logística, y desde ahí sigue su curso normal). A diferencia de <see
    /// cref="DestinosDevolucion"/> (siempre hacia atrás, Estado puede volver a Devuelto), esto es
    /// hacia ADELANTE dentro del mismo flujo y el documento sigue EnProceso.
    /// </summary>
    public int? PasoRedirigirAPagoContadoId { get; set; }
    public PasoFlujo? PasoRedirigirAPagoContado { get; set; }

    public ICollection<PasoFlujoRol> PasoFlujoRoles { get; set; } = [];
}

/// <summary>
/// Un destino de devolución permitido desde un paso — relación muchos a muchos entre PasoFlujo y sí
/// mismo. Quien devuelve el documento desde <see cref="PasoFlujoOrigenId"/> elige entre estos
/// destinos (ver <see cref="Negocio.DTOs.EjecutarAccionDto.PasoDestinoId"/>); si un paso no tiene
/// ninguna fila acá, "Devolver" siempre vuelve al emisor.
/// </summary>
public class PasoFlujoDestinoDevolucion
{
    public int PasoFlujoOrigenId { get; set; }
    public PasoFlujo PasoFlujoOrigen { get; set; } = null!;

    public int PasoFlujoDestinoId { get; set; }
    public PasoFlujo PasoFlujoDestino { get; set; } = null!;
}

/// <summary>Roles habilitados para actuar en un paso. Cualquier usuario con alguno de estos roles puede aprobar/devolver/rechazar ese paso.</summary>
public class PasoFlujoRol
{
    public int PasoFlujoId { get; set; }
    public PasoFlujo PasoFlujo { get; set; } = null!;

    public int RolId { get; set; }
    public Rol Rol { get; set; } = null!;
}
