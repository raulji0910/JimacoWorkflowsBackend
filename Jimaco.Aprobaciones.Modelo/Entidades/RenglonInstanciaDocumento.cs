using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Un renglón (línea de producto) de un documento — ej. un ítem de la Orden de Compra. A
/// diferencia de los "campos dinámicos" de <see cref="TipoDocumento"/> (libres, definidos por
/// admin), los renglones son un concepto de primera clase con forma fija, igual para cualquier
/// tipo de documento que los use: son la fuente de verdad del valor total del documento (no un
/// campo de valor tipeado a mano) — mismo criterio que usa World Office (su cabecera tampoco
/// guarda un total propio, se suma desde las líneas; ver esquema-worldoffice-oc.md).
/// </summary>
public class RenglonInstanciaDocumento
{
    public int Id { get; set; }

    public int InstanciaDocumentoId { get; set; }
    public InstanciaDocumento InstanciaDocumento { get; set; } = null!;

    public int Orden { get; set; }

    [MaxLength(50)]
    public string? Codigo { get; set; }

    [MaxLength(300)]
    public required string Descripcion { get; set; }

    public decimal Cantidad { get; set; }

    [MaxLength(20)]
    public string? UnidadMedida { get; set; }

    public decimal ValorUnitario { get; set; }

    /// <summary>Ej. 0.19 para 19%.</summary>
    public decimal PorcentajeIva { get; set; }

    /// <summary>Cantidad × ValorUnitario, antes de IVA — se calcula al guardar, no se recalcula en cada lectura.</summary>
    public decimal Total { get; set; }
}
