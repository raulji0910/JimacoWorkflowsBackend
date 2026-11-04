using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// El Comprobante de Egreso real de World Office (prefijo <c>CEJ</c> en <c>[CuentasContables -
/// Asientos]</c>) que Contabilidad generó ahí para autorizar el giro — a diferencia de <see
/// cref="DocumentoVinculado"/> (que WO liga solo, vía <c>NúmDocumentoExterno</c>), este documento
/// NO está ligado a la OC en WO — Contabilidad escribe su número acá a mano al aprobar el paso
/// "Área Contable" (ver <see cref="InstanciaDocumento.NumeroComprobanteEgresoSolicitado"/>), y el
/// Sincronizador lo busca y trae una copia (mismo motivo que DocumentoVinculado: la Api no tiene
/// acceso directo a la base de WO).
/// </summary>
public class ComprobanteEgreso
{
    public int Id { get; set; }

    public int InstanciaDocumentoId { get; set; }
    public InstanciaDocumento InstanciaDocumento { get; set; } = null!;

    [MaxLength(50)]
    public required string NumeroReferencia { get; set; }

    public DateTime Fecha { get; set; }

    public decimal Valor { get; set; }

    [MaxLength(200)]
    public string? Beneficiario { get; set; }

    [MaxLength(30)]
    public string? NitBeneficiario { get; set; }

    [MaxLength(200)]
    public string? Direccion { get; set; }

    [MaxLength(100)]
    public string? Ciudad { get; set; }

    [MaxLength(50)]
    public string? Telefono { get; set; }

    [MaxLength(200)]
    public string? Concepto { get; set; }

    [MaxLength(200)]
    public string? ElaboradoPor { get; set; }

    public ICollection<RenglonComprobanteEgreso> Renglones { get; set; } = [];
}

/// <summary>Una línea de débito/crédito del comprobante — mismo criterio que RenglonDocumentoVinculado.</summary>
public class RenglonComprobanteEgreso
{
    public int Id { get; set; }

    public int ComprobanteEgresoId { get; set; }
    public ComprobanteEgreso ComprobanteEgreso { get; set; } = null!;

    public int Orden { get; set; }

    [MaxLength(20)]
    public string? CodigoCuenta { get; set; }

    [MaxLength(200)]
    public string? NombreCuenta { get; set; }

    [MaxLength(300)]
    public string? Concepto { get; set; }

    [MaxLength(200)]
    public string? Tercero { get; set; }

    public decimal Debito { get; set; }

    public decimal Credito { get; set; }
}
