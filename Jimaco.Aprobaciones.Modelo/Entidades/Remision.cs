using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Remisión (prefijo REM de World Office) que Logística tiene que entregar (2026-11-12, módulo
/// "Entregas"). La trae el Sincronizador solo cuando ya está CONTABILIZADA — en WO eso no es una
/// bandera del encabezado: es que tenga movimientos en [CuentasContables - Asientos - Detalles]
/// (confirmado con las REM 29624–29653). Una vez contabilizada WO no deja editarla, así que es una
/// foto fija: no se refresca. Logística la imprime (PDF propio con QR) y confirma la entrega
/// adjuntando el soporte firmado por el cliente.
/// </summary>
public class Remision
{
    public int Id { get; set; }

    /// <summary>Clave del documento en WO — única: es lo que hace idempotente la sincronización.</summary>
    public int IdAsientoContableWO { get; set; }

    [MaxLength(20)]
    public required string Prefijo { get; set; }

    [MaxLength(30)]
    public required string Numero { get; set; }

    public DateTime Fecha { get; set; }

    [MaxLength(300)] public string? Cliente { get; set; }
    [MaxLength(50)] public string? NitCliente { get; set; }
    [MaxLength(300)] public string? Direccion { get; set; }
    [MaxLength(150)] public string? Ciudad { get; set; }
    [MaxLength(150)] public string? Telefono { get; set; }
    [MaxLength(200)] public string? Vendedor { get; set; }
    [MaxLength(100)] public string? ElaboradoPorWO { get; set; }
    [MaxLength(150)] public string? FormaDePago { get; set; }

    // Campos personalizados del encabezado de la REM (confirmados con la REM 29635).
    /// <summary>"DIRECCION DE ENTREGA" — Personalizado1.</summary>
    [MaxLength(300)] public string? DireccionEntrega { get; set; }
    /// <summary>"CAMPO ENTREGA/REC" — Personalizado2.</summary>
    [MaxLength(300)] public string? CampoEntrega { get; set; }
    /// <summary>"ORDEN DE COMPRA" del cliente — Personalizado3.</summary>
    [MaxLength(150)] public string? OrdenCompraCliente { get; set; }
    /// <summary>"COTIZACION" — Personalizado4 (texto libre; puede traer varias).</summary>
    [MaxLength(150)] public string? Cotizacion { get; set; }

    [MaxLength(1000)] public string? Nota { get; set; }

    public EstadoRemision Estado { get; set; } = EstadoRemision.PorEntregar;

    public DateTime FechaSincronizacion { get; set; } = DateTime.UtcNow;

    // ---- Entrega ----
    public DateTime? FechaEntrega { get; set; }
    public int? EntregadaPorUsuarioId { get; set; }
    public Usuario? EntregadaPorUsuario { get; set; }
    [MaxLength(1000)] public string? ObservacionEntrega { get; set; }

    /// <summary>Resultado de leer el QR del soporte de entrega (ver <see cref="VerificacionQRSoporte"/>).</summary>
    public VerificacionQRSoporte? VerificacionQR { get; set; }

    public ICollection<RenglonRemision> Renglones { get; set; } = [];

    /// <summary>
    /// Lista de documentos (no "una foto"), a propósito: en la fase 2 el emisor de la remisión
    /// agregará aquí los documentos que Contabilidad necesita para facturar.
    /// </summary>
    public ICollection<SoporteRemision> Soportes { get; set; } = [];
}

public enum EstadoRemision
{
    PorEntregar = 0,
    Entregada = 1,
    /// <summary>Se anuló en WO después de traerla — sale del dashboard.</summary>
    Anulada = 2
}

/// <summary>
/// Qué encontró el navegador al leer el QR del soporte de entrega. Nunca bloquea la entrega: lo
/// que no es <see cref="Coincide"/> queda marcado "Revisar" en el dashboard.
/// </summary>
public enum VerificacionQRSoporte
{
    /// <summary>El QR leído es el de esta remisión.</summary>
    Coincide = 0,
    /// <summary>Se leyó un QR de OTRA remisión — casi seguro se adjuntó el soporte equivocado.</summary>
    NoCoincide = 1,
    /// <summary>No se pudo leer ningún QR (foto borrosa, impresa desde WO, escaneo sin QR...).</summary>
    SinQR = 2
}

public class RenglonRemision
{
    public int Id { get; set; }
    public int RemisionId { get; set; }
    public Remision Remision { get; set; } = null!;
    public int Orden { get; set; }
    [MaxLength(50)] public string? Codigo { get; set; }
    [MaxLength(500)] public required string Descripcion { get; set; }
    public decimal Cantidad { get; set; }
    [MaxLength(30)] public string? UnidadMedida { get; set; }
}

public enum TipoSoporteRemision
{
    /// <summary>Remisión firmada por el cliente (foto o escaneo) — la sube Logística al entregar.</summary>
    EntregaFirmada = 0,
    /// <summary>Fase 2: documentos que agrega el emisor para que Contabilidad facture.</summary>
    DocumentoFacturacion = 1
}

public class SoporteRemision
{
    public int Id { get; set; }
    public int RemisionId { get; set; }
    public Remision Remision { get; set; } = null!;
    public TipoSoporteRemision Tipo { get; set; }

    [MaxLength(300)] public required string NombreArchivo { get; set; }
    /// <summary>Ruta relativa dentro del almacenamiento de adjuntos (no la ruta absoluta del disco).</summary>
    [MaxLength(500)] public required string RutaArchivo { get; set; }
    [MaxLength(150)] public string? ContentType { get; set; }
    public long TamanoBytes { get; set; }

    /// <summary>Texto del QR que leyó el navegador en este archivo (null = no se leyó ninguno).</summary>
    [MaxLength(200)] public string? QrLeido { get; set; }

    // Revisión con IA (2026-11-12): qué número de remisión leyó, si vio la firma del cliente y su
    // comentario. Null en los campos = no se revisó con IA (no estaba configurada).
    [MaxLength(30)] public string? IaNumeroLeido { get; set; }
    public bool? IaHayFirma { get; set; }
    /// <summary>La IA vio el sello del cliente (vale igual que la firma).</summary>
    public bool? IaHaySello { get; set; }
    [MaxLength(500)] public string? IaObservacion { get; set; }

    /// <summary>Este archivo cumplió la regla (es esta remisión y tiene la firma o el sello del cliente).</summary>
    public bool Valido { get; set; }

    public int SubidoPorUsuarioId { get; set; }
    public Usuario SubidoPorUsuario { get; set; } = null!;
    public DateTime FechaCarga { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Configuración de Entregas — UNA fila (Id = 1). La fecha de inicio la fija la migración con la
/// fecha del día en que se aplica (= el día que el módulo sale a producción), por pedido del
/// usuario: "desde el día que salga a producción", nada anterior.
/// </summary>
public class ConfiguracionEntregas
{
    public const int IdUnico = 1;
    public int Id { get; set; }
    public DateTime FechaInicio { get; set; }

    /// <summary>
    /// Interruptor de la revisión con IA (firma/sello) que maneja un administrador desde la página
    /// (2026-11-12, pedido del usuario: "dejarlo apagado por un momento, administrado por el
    /// administrador"). Arranca APAGADO. Apagado = solo se exige el QR. Encendido solo tiene efecto
    /// si además el servidor tiene la clave de Anthropic.
    /// </summary>
    public bool VerificacionIAActiva { get; set; }

    public DateTime? FechaActualizacion { get; set; }
    public int? ActualizadoPorUsuarioId { get; set; }
    public Usuario? ActualizadoPorUsuario { get; set; }
}
