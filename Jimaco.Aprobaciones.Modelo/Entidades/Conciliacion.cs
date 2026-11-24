using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

// ---------------------------------------------------------------------------------------------
// Conciliación de facturas de proveedor (XML DIAN) contra las OC de World Office (2026-11-24).
// Copia del módulo que funciona en Ferrealiados Cotizaciones (ver
// ProyectosJimaco/ConciliacionFerrealiados/CONTEXTO-MODULO-CONCILIACION.md), adaptada a Jimaco:
// las facturas llegan a facturacion@jimaco.com.co (o se suben en ZIP de la DIAN), las OC y las FCJ
// las sube el Sincronizador. El cruce NO se guarda: se calcula al vuelo en cada consulta.
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Factura electrónica (XML DIAN) de un proveedor. Los campos salen del XML (LectorFacturaElectronica);
/// el XML se guarda completo — comprimido con gzip, porque Jimaco recibe unas 600 al mes — para
/// poder descargarlo a demanda y para la fase 2 (generar la FCJ en WO). Identidad: el CUFE.
/// </summary>
public class FacturaProveedor
{
    public int Id { get; set; }

    [MaxLength(200)]
    public required string Cufe { get; set; }

    [MaxLength(50)]
    public required string Numero { get; set; }

    /// <summary>Código DIAN: "01" factura, "91" nota crédito, "92" nota débito.</summary>
    [MaxLength(5)]
    public required string TipoDocumento { get; set; }

    public DateOnly FechaEmision { get; set; }

    [MaxLength(20)]
    public required string NitEmisor { get; set; }

    [MaxLength(300)]
    public required string NombreEmisor { get; set; }

    [MaxLength(20)]
    public required string NitReceptor { get; set; }

    /// <summary>Sin IVA y ya con descuentos a nivel factura (TaxExclusiveAmount del XML).</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Iva { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    /// <summary>cac:OrderReference tal cual vino (ej. "OC_1275"); la mayoría de proveedores no lo manda.</summary>
    [MaxLength(100)]
    public string? ReferenciaOrden { get; set; }

    /// <summary>El XML original comprimido con gzip (UTF-8). Ver <c>XmlComprimido</c> en Negocio.</summary>
    public required byte[] XmlGzip { get; set; }

    [MaxLength(300)]
    public string? NombreArchivo { get; set; }

    [MaxLength(500)]
    public string? CorreoAsunto { get; set; }

    public DateTime? CorreoFecha { get; set; }

    public DateTime FechaRegistro { get; set; }

    /// <summary>"Correo" (facturacion@), "Carpeta" (Sincronizador --carpeta) o "Dian" (ZIP subido desde la pantalla).</summary>
    [MaxLength(20)]
    public string Origen { get; set; } = OrigenFactura.Correo;

    public ICollection<FacturaProveedorRenglon> Renglones { get; set; } = [];
}

public class FacturaProveedorRenglon
{
    public int Id { get; set; }

    public int FacturaProveedorId { get; set; }
    public FacturaProveedor? FacturaProveedor { get; set; }

    public int Orden { get; set; }

    [MaxLength(500)]
    public required string Descripcion { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Cantidad { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal? PorcentajeIva { get; set; }
}

public static class OrigenFactura
{
    public const string Correo = "Correo";
    public const string Carpeta = "Carpeta";
    public const string Dian = "Dian";
}

/// <summary>
/// Copia de una OC de World Office (prefijo "OC") para cruzarla contra las facturas. WO es la fuente
/// de verdad: cada sincronización actualiza lo que cambió. Identidad: IdAsientoContable de WO.
/// </summary>
public class OrdenCompraWorldOffice
{
    public int Id { get; set; }

    public int IdAsientoContable { get; set; }

    public int Numero { get; set; }

    public DateOnly Fecha { get; set; }

    [MaxLength(20)]
    public required string NitProveedor { get; set; }

    [MaxLength(300)]
    public required string NombreProveedor { get; set; }

    /// <summary>Quién elaboró la OC en WO ("elaborado por" = IdTerceroInterno), igual que en las aprobaciones.</summary>
    [MaxLength(300)]
    public string? Emisor { get; set; }

    /// <summary>
    /// SUM(Cantidad × MontoMonetarioUnitario) — NUNCA SUM(TotalRenglon): en el WO de Jimaco
    /// TotalRenglon no es el costo (a veces el doble, a veces menos, a veces NULL; confirmado
    /// 2026-11-24). Backtest FCJ vs OC desde octubre: 433 de 483 cuadran ±100 con esta fórmula.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Iva { get; set; }

    public bool Anulada { get; set; }

    public DateTime FechaSincronizacion { get; set; }

    public ICollection<OrdenCompraWorldOfficeRenglon> Renglones { get; set; } = [];
}

public class OrdenCompraWorldOfficeRenglon
{
    public int Id { get; set; }

    public int OrdenCompraWorldOfficeId { get; set; }
    public OrdenCompraWorldOffice? OrdenCompraWorldOffice { get; set; }

    public int Orden { get; set; }

    [MaxLength(100)]
    public string? Codigo { get; set; }

    [MaxLength(500)]
    public required string Descripcion { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Cantidad { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ValorUnitario { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal PorcentajeIva { get; set; }
}

/// <summary>
/// Encabezado de una factura de compra de World Office (en Jimaco el prefijo es "FCJ"). Último
/// recurso del cruce: Contabilidad escribe en Personalizado1 la(s) OC ("20058", "19691-19583-19602")
/// y en NúmDocumentoExterno el número de la factura del proveedor (el del XML).
/// </summary>
public class FacturaCompraWorldOffice
{
    public int Id { get; set; }

    public int IdAsientoContable { get; set; }

    public int Numero { get; set; }

    public DateOnly Fecha { get; set; }

    [MaxLength(20)]
    public required string NitProveedor { get; set; }

    [MaxLength(300)]
    public required string NombreProveedor { get; set; }

    [MaxLength(100)]
    public string? DocumentoExterno { get; set; }

    /// <summary>Personalizado1 tal cual lo escribió Contabilidad.</summary>
    [MaxLength(200)]
    public string? ReferenciaOrdenes { get; set; }

    public bool Anulada { get; set; }

    public DateTime FechaSincronizacion { get; set; }
}

public enum TipoObservacionOrden
{
    Observacion = 1,
    /// <summary>Quien revisa marca que la OC no se va a facturar. Sigue en "OC sin factura", señalada.</summary>
    MarcaNoRequiereFactura = 2,
    QuitaMarcaNoRequiereFactura = 3
}

/// <summary>
/// Historial de revisión de una OC sin factura. Nunca se edita ni se borra. Se amarra al
/// IdAsientoContable de WO (no al Id de la copia) para que sobreviva a las sincronizaciones.
/// </summary>
public class ObservacionOrdenCompra
{
    public int Id { get; set; }

    public int IdAsientoContable { get; set; }

    public int NumeroOrden { get; set; }

    public TipoObservacionOrden Tipo { get; set; } = TipoObservacionOrden.Observacion;

    [MaxLength(2000)]
    public required string Texto { get; set; }

    public int UsuarioId { get; set; }

    [MaxLength(200)]
    public required string NombreUsuario { get; set; }

    public DateTime Fecha { get; set; }
}

public enum TipoObservacionFactura
{
    Observacion = 1,
    /// <summary>La diferencia o el parcial ya se aclaró (motivo obligatorio). Sale de "Sin revisar".</summary>
    MarcaRevisada = 2,
    QuitaMarcaRevisada = 3
}

/// <summary>
/// Historial de revisión de una factura Parcial o Con diferencia (adición de Jimaco, 2026-11-24).
/// Mismas reglas que el de las OC: nunca se edita ni se borra; la marca "Revisada" vigente es la
/// última acción de marcar/quitar. Se amarra a la factura (identidad CUFE, nunca se borra).
/// </summary>
public class ObservacionFacturaProveedor
{
    public int Id { get; set; }

    public int FacturaProveedorId { get; set; }
    public FacturaProveedor? FacturaProveedor { get; set; }

    public TipoObservacionFactura Tipo { get; set; } = TipoObservacionFactura.Observacion;

    [MaxLength(2000)]
    public required string Texto { get; set; }

    public int UsuarioId { get; set; }

    [MaxLength(200)]
    public required string NombreUsuario { get; set; }

    public DateTime Fecha { get; set; }
}

/// <summary>
/// Una sola fila (Id = 1): cuándo subió el Sincronizador por última vez las OC, para mostrarlo en la
/// pantalla. No se puede sacar de FechaSincronizacion de las OC porque solo se tocan las que cambian.
/// </summary>
public class SincronizacionConciliacion
{
    public int Id { get; set; }

    public DateTime? UltimaSincronizacionOrdenes { get; set; }

    /// <summary>
    /// Versión de la lectura del XML con la que están calculados los totales guardados de las facturas
    /// (2026-11-24). Si el código trae una versión mayor (ConciliacionService.VersionLecturaXml), al
    /// arrancar la Api se releen los XML guardados y se corrigen Subtotal/IVA/Total, una sola vez.
    /// </summary>
    public int VersionLecturaXml { get; set; }
}
