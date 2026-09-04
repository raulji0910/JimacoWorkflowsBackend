namespace Jimaco.Aprobaciones.Sincronizador.Models;

/// <summary>
/// Fila de <c>[CuentasContables - Asientos]</c> con <c>prefijo = 'OC'</c> — la cabecera de una
/// Orden de Compra en World Office. Nombres de propiedad en español a propósito: reflejan
/// literalmente las columnas de la base de World Office (ver
/// ProyectosJimaco/WorkflowDocumentos/esquema-worldoffice-oc.md para el mapeo completo), no las
/// convenciones de Jimaco.Aprobaciones — este tipo vive del lado de la lectura, no del dominio.
/// </summary>
public class OcHeaderRow
{
    public int IdAsientoContable { get; set; }
    public required string Prefijo { get; set; }
    public decimal DocumentoNumero { get; set; }
    public DateTime Fecha { get; set; }
    public int? IdTerceroExterno { get; set; }
    public int? IdTerceroInterno { get; set; }
    public string? NumDocumentoExterno { get; set; }
    public string? Nota { get; set; }
    public int? IdFormaDePago { get; set; }
}

/// <summary>Fila resuelta de <c>Terceros</c> — alcanza con nombre/apellido/identificación para lo que necesitamos.</summary>
public class TerceroRow
{
    public int IdTercero { get; set; }
    public string? Nombre { get; set; }
    public string? Apellidos { get; set; }
    public string? Identificacion { get; set; }

    /// <summary>Nombre para mostrar: para una empresa "Nombre" ya trae la razón social completa y "Apellidos" queda vacío; para una persona se concatenan.</summary>
    public string NombreCompleto => string.IsNullOrWhiteSpace(Apellidos) ? Nombre ?? "" : $"{Nombre} {Apellidos}".Trim();
}
