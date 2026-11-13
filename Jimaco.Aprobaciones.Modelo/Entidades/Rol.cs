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

    /// <summary>
    /// Permiso administrativo transversal (2026-09-23): quien tenga un rol con esto en true puede
    /// buscar CUALQUIER documento (sin importar su paso actual ni si tiene el rol habilitado ahí) y
    /// devolverlo al emisor — anula el paso en curso y SIEMPRE refleja "en espera de aprobación" en
    /// World Office (a diferencia de la devolución normal desde un paso posterior, que no toca WO —
    /// ver InstanciaDocumentoService.DevolverForzadoAsync). Pensado para casos administrativos, no
    /// para el flujo normal.
    /// </summary>
    public bool PuedeDevolverCualquierDocumento { get; set; }

    /// <summary>
    /// Control de alistamiento (2026-09-28): quien tenga un rol con esto en true puede registrar la
    /// cantidad disponible de cada renglón de cualquier Hoja de Alistamiento (DocumentoVinculado),
    /// sin importar el paso actual del documento — es un proceso paralelo al flujo de aprobación,
    /// no depende de tener el rol habilitado en ningún paso puntual. También implica poder ver el
    /// dashboard de alistamiento (ver <see cref="PuedeVerAlistamiento"/>, que alcanza con cualquiera
    /// de los dos).
    /// </summary>
    public bool PuedeGestionarAlistamiento { get; set; }

    /// <summary>
    /// Solo ver el dashboard de alistamiento (completitud/antigüedad de las Hojas de Alistamiento),
    /// sin poder editar cantidades — pensado para Gerencia Comercial (supervisión), separado de
    /// <see cref="PuedeGestionarAlistamiento"/> para no darle a quien solo supervisa el permiso de
    /// tocar datos que le corresponden a Transporte.
    /// </summary>
    public bool PuedeVerAlistamiento { get; set; }

    /// <summary>
    /// Módulo de Cuentas por Pagar (2026-11-05): ver el estado de lo que se les debe a los
    /// proveedores (foto de World Office) y decidir qué facturas se mandan a pagar — pensado para
    /// Gerencia Comercial. Cada decisión crea un documento "Pago a Proveedor" que sigue el flujo
    /// normal (Contabilidad genera el CEJ, Gerencia General paga).
    /// </summary>
    public bool PuedeVerCuentasPorPagar { get; set; }

    /// <summary>
    /// "Pagador de cuentas" (2026-11-09): menú propio "Pagos" donde se ven las relaciones de pago que
    /// mandó Gerencia Comercial (y los pagos extraordinarios), se eligen las que se pagan como un lote
    /// con UN solo soporte, y se rechaza lo que no. Pensado para Gerencia General.
    /// </summary>
    public bool PuedePagarCuentas { get; set; }

    /// <summary>
    /// Entregas (2026-11-12): ver el dashboard de Remisiones de World Office, imprimirlas y confirmar
    /// la entrega adjuntando el soporte firmado por el cliente — pensado para Logística. Implica
    /// <see cref="PuedeVerEntregas"/>.
    /// </summary>
    public bool PuedeGestionarEntregas { get; set; }

    /// <summary>Solo consultar el estado de las entregas (sin confirmar ni adjuntar) — supervisión.</summary>
    public bool PuedeVerEntregas { get; set; }

    /// <summary>
    /// Entregas fase 2 (2026-11-13): el emisor de la remisión (vendedora) ve SUS remisiones (las que
    /// elaboró en WO = su <see cref="Usuario.UsuarioWO"/>), adjunta los documentos para facturar y
    /// las envía a contabilidad. Con <see cref="PuedeVerEntregas"/> además, puede hacerlo en todas.
    /// </summary>
    public bool PuedeDocumentarEntregas { get; set; }

    /// <summary>
    /// Entregas fase 2: el contable — ve las remisiones enviadas a facturación con sus documentos, le
    /// llega el correo y puede devolverlas al emisor con un comentario.
    /// </summary>
    public bool PuedeFacturarEntregas { get; set; }

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
