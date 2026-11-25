using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Cotización armada en el portal (2026-11-25, Fase 1 de "Creación de documentos en World Office").
/// Ciclo: Borrador (se edita libremente) → PorCrear (enviada: el Sincronizador --documentos la crea
/// en WO) → Creada (WO le asignó prefijo y número) o Error (WO la rechazó: se corrige y se reenvía).
/// Una vez creada, la fuente de verdad es WO: cualquier corrección se hace allá.
/// </summary>
public class Cotizacion
{
    public int Id { get; set; }

    /// <summary>Quien la armó (vendedora) — su <c>UsuarioWO</c> y <c>IdTerceroWO</c> quedan como "elaborado por" en WO.</summary>
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;

    [MaxLength(20)]
    public string Estado { get; set; } = EstadoCotizacion.Borrador;

    // ---- Cliente ----
    public int? IdTerceroWO { get; set; }

    [MaxLength(30)]
    public string? ClienteIdentificacion { get; set; }

    [MaxLength(300)]
    public string? ClienteNombre { get; set; }

    /// <summary>Sucursal del cliente (<c>IdDirecciónExterno</c>); null = la principal.</summary>
    public int? IdDireccionWO { get; set; }

    [MaxLength(400)]
    public string? DireccionTexto { get; set; }

    public int? IdFormaPagoWO { get; set; }

    [MaxLength(100)]
    public string? FormaPagoNombre { get; set; }

    /// <summary>"CONCEPTO" de la pantalla de WO (columna <c>Nota</c> del encabezado). WO pone "COTIZACIÓN".</summary>
    [MaxLength(500)]
    public string? Concepto { get; set; }

    /// <summary>Días de vigencia: el <c>Vencimiento</c> de cada renglón en WO es la fecha + estos días.</summary>
    public int VigenciaDias { get; set; } = 30;

    /// <summary>"Más Datos": {"1":"CONTACTO...","9":"PO-123"} → PersonalizadoN del encabezado en WO.</summary>
    public string? MasDatosJson { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Iva { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Total { get; set; }

    public DateTime FechaCreacion { get; set; }
    public DateTime FechaActualizacion { get; set; }

    // ---- Envío y resultado en WO ----

    /// <summary>Fecha del documento en WO (el día en que se envió, hora Colombia).</summary>
    public DateOnly? FechaDocumento { get; set; }
    public DateTime? FechaEnvio { get; set; }

    /// <summary>Copia, al enviar, del UsuarioWO del usuario (WO: IdUsuarioWO).</summary>
    [MaxLength(100)]
    public string? UsuarioWO { get; set; }

    /// <summary>Copia, al enviar, del tercero "elaborado por" del usuario (WO: IdTerceroInterno).</summary>
    public int? IdTerceroElaboraWO { get; set; }

    [MaxLength(20)]
    public string? PrefijoWO { get; set; }

    [Column(TypeName = "decimal(18,0)")]
    public decimal? NumeroWO { get; set; }

    public int? IdAsientoContableWO { get; set; }

    public DateTime? FechaCreacionWO { get; set; }

    [MaxLength(1000)]
    public string? MensajeError { get; set; }

    public ICollection<CotizacionRenglon> Renglones { get; set; } = [];
}

public static class EstadoCotizacion
{
    public const string Borrador = "Borrador";
    public const string PorCrear = "PorCrear";
    public const string Creada = "Creada";
    public const string Error = "Error";
}

/// <summary>Un renglón de la cotización → una fila de <c>CCA_M_Inventarios</c> en WO.</summary>
public class CotizacionRenglon
{
    public int Id { get; set; }

    public int CotizacionId { get; set; }
    public Cotizacion Cotizacion { get; set; } = null!;

    public int Orden { get; set; }

    public int IdInventarioWO { get; set; }

    [MaxLength(50)]
    public required string Codigo { get; set; }

    [MaxLength(500)]
    public required string Descripcion { get; set; }

    [MaxLength(15)]
    public required string UnidadMedida { get; set; }

    [MaxLength(15)]
    public required string IdBodegaWO { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Cantidad { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ValorUnitario { get; set; }

    /// <summary>Fracción, como en WO (0.19).</summary>
    [Column(TypeName = "decimal(9,6)")]
    public decimal Iva { get; set; }

    public int? IdClasificacion { get; set; }

    /// <summary>Texto libre del renglón ("8 dias habiles") — columna <c>Nota</c> de CCA_M_Inventarios.</summary>
    [MaxLength(500)]
    public string? Nota { get; set; }
}
