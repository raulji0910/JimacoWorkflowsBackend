namespace Jimaco.Aprobaciones.Sincronizador.Models;

/// <summary>
/// Una partida abierta de <c>Vista_CuentasPorPagar_Detallada</c> (vista propia de World Office, la
/// misma que usa Contabilidad allá) — el Saldo viene con naturaleza crédito: negativo = se debe.
/// </summary>
public class PartidaCuentaPorPagarRow
{
    public int? AbonaADocumento { get; set; }
    public string? Identificacion { get; set; }
    public string? NombresTerceros { get; set; }
    public string? Documento { get; set; }
    public string? Prefijo { get; set; }
    public decimal? DocumentoNumero { get; set; }
    public string? NumDocumentoExterno { get; set; }
    public DateTime Fecha { get; set; }
    public DateTime? Vencimiento { get; set; }
    public decimal Saldo { get; set; }
    public string? CodigoCuentaContable { get; set; }
}

/// <summary>Saldo sin cruzar de la cuenta de anticipos a proveedores, por tercero.</summary>
public class AnticipoProveedorRow
{
    public string? Identificacion { get; set; }
    public string? NombreTercero { get; set; }
    public int Movimientos { get; set; }
    public decimal Saldo { get; set; }
}
