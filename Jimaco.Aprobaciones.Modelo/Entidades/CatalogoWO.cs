using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

// ---------------------------------------------------------------------------------------------
// Catálogo de World Office para crear documentos desde el portal (2026-11-25, Fase 0 de
// "Creación de documentos en World Office" — ver CLAUDE.md). Es una COPIA de lectura que sube el
// Sincronizador (modo --documentos): WO sigue siendo la fuente de verdad, acá solo se guarda lo
// necesario para armar una cotización sin consultar WO en cada tecla. Los clientes son el catálogo
// TerceroWO que ya existe (Cuentas por pagar).
// ---------------------------------------------------------------------------------------------

/// <summary>Producto de WO (tabla <c>Inventarios</c>). Se actualiza incremental por <c>VersionCol</c>.</summary>
public class ProductoWO
{
    /// <summary><c>IdInventario</c> de WO — es la llave (no hay Id propio: el catálogo es un espejo).</summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int IdInventarioWO { get; set; }

    [MaxLength(50)]
    public required string Codigo { get; set; }

    [MaxLength(500)]
    public required string Descripcion { get; set; }

    [MaxLength(15)]
    public required string UnidadMedida { get; set; }

    /// <summary>Fracción, como la guarda WO (0.19 = 19 %).</summary>
    [Column(TypeName = "decimal(9,6)")]
    public decimal Iva { get; set; }

    /// <summary><c>IdClasificacion</c> del producto: es el "C. Costos" que WO pone en el renglón (2 = Ventas).</summary>
    public int? IdClasificacion { get; set; }

    /// <summary>Lista de precios 1 de WO; casi nunca tiene valor (40 de 30.307 productos): el vendedor digita el precio.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Precio1 { get; set; }

    public bool Activo { get; set; }

    /// <summary><c>VersionCol</c> (rowversion) de WO como número — marca de agua de la sincronización incremental.</summary>
    public long VersionWO { get; set; }

    public DateTime FechaActualizacion { get; set; }
}

/// <summary>Bodega de WO (<c>Inventarios - Bodegas</c>): "Uno" = Principal, "Dos" = Garantía, etc.</summary>
public class BodegaWO
{
    [MaxLength(15)]
    public required string IdBodegaWO { get; set; }

    [MaxLength(50)]
    public required string Nombre { get; set; }

    public bool Activa { get; set; }
}

/// <summary>
/// Existencia de un producto en una bodega — sale de la vista de WO <c>Vista_ExistenciasPorBodegas</c>
/// (la misma cifra que muestra WO; la vista está cifrada, no se recalcula acá). Foto completa: cada
/// sincronización reemplaza la anterior.
/// </summary>
public class ExistenciaWO
{
    public int IdInventarioWO { get; set; }

    [MaxLength(15)]
    public required string IdBodegaWO { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Existencia { get; set; }
}

/// <summary>Dirección (sucursal) de un tercero de WO (<c>Terceros - Direcciones</c>) — va en IdDirecciónExterno del documento.</summary>
public class DireccionTerceroWO
{
    /// <summary><c>IdTerceroDireccion</c> de WO.</summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int IdDireccionWO { get; set; }

    public int IdTerceroWO { get; set; }

    [MaxLength(300)]
    public string? Direccion { get; set; }

    [MaxLength(100)]
    public string? Telefonos { get; set; }

    [MaxLength(100)]
    public string? Ciudad { get; set; }

    public bool Principal { get; set; }
}

/// <summary>Forma de pago de WO (<c>Formas De Pago</c>): 2 = Crédito, etc.</summary>
public class FormaPagoWO
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int IdFormaPagoWO { get; set; }

    [MaxLength(100)]
    public required string Nombre { get; set; }
}

/// <summary>
/// Un campo de "Más Datos" de WO (<c>Campos_Pers_Docum</c>, solo los del encabezado — NumParte 1):
/// qué etiqueta tiene el PersonalizadoN en cada tipo de documento y si está en uso. Ej. COT 1 =
/// "CONTACTO", OC 4 = "OBSERVACIONES", REM 4 = "COTIZACION". Los de renglón (NumParte 2) no se usan.
/// </summary>
public class CampoPersonalizadoWO
{
    public int Id { get; set; }

    /// <summary>Tipo de documento de WO ("COT", "OC", "REM"...).</summary>
    [MaxLength(10)]
    public required string TipoDocumento { get; set; }

    /// <summary>1..15 → columna PersonalizadoN del encabezado.</summary>
    public int Numero { get; set; }

    [MaxLength(100)]
    public required string Etiqueta { get; set; }

    /// <summary><c>senUso</c> de WO (-1 = en uso). Solo los activos se muestran en el portal.</summary>
    public bool Activo { get; set; }
}

/// <summary>Estado de la sincronización del catálogo (una sola fila) — marca de agua y "actualizado hace X".</summary>
public class SincronizacionCatalogoWO
{
    public int Id { get; set; }

    /// <summary>Mayor <c>VersionCol</c> de <c>Inventarios</c> recibida — la próxima corrida pide solo lo posterior.</summary>
    public long UltimaVersionProductos { get; set; }

    public DateTime? FechaProductos { get; set; }

    public DateTime? FechaExistencias { get; set; }

    /// <summary>Bodegas, direcciones, formas de pago y "Más Datos" (cambian poco: se mandan cada hora).</summary>
    public DateTime? FechaCatalogos { get; set; }
}
