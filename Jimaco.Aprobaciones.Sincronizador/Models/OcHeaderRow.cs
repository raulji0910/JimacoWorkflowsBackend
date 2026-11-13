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

    // Solo usados al leer un documento VINCULADO (ej. la Cotización de origen de una OC, ver
    // WorldOfficeReader.ObtenerDocumentoVinculadoAsync) — quedan null en la lectura normal de OC,
    // que nunca los pidió hasta ahora.
    public int? IdDireccionExterno { get; set; }

    /// <summary>"CAMPO ENTREGA - CIUDAD - DIRECCION" en la Hoja de Alistamiento — ver esquema-worldoffice-oc.md.</summary>
    public string? Personalizado2 { get; set; }

    /// <summary>
    /// Campo libre "OBSERVACIONES" de la pestaña Personalizados de "Más Datos" en la OC — texto
    /// escrito a mano, DISTINTO de <see cref="Nota"/> (que es el "BASADO EN COTIZACIÓN..." automático
    /// que ya se traía). Confirmado contra la OC real 19692 (2026-10-21): <c>Personalizado4</c> para
    /// prefijo 'OC' — mismo número de columna que "COTIZACION" en REM, pero es pura coincidencia, no
    /// asumir que el número se repite entre prefijos (ver nota de Personalizado2 arriba, que para COT
    /// es "Campo Entrega" y acá para OC es otra cosa).
    /// </summary>
    public string? Personalizado4 { get; set; }

    // 2026-11-13 (Ferrealiados): cada empresa usa los personalizados para cosas distintas, así que se
    // traen los cuatro y Program.cs elige cuál según la sección "MapeoWO" del appsettings.
    public string? Personalizado1 { get; set; }
    public string? Personalizado3 { get; set; }

    /// <summary>El campo personalizado indicado ("Personalizado1".."Personalizado4"); null si no existe.</summary>
    public string? Personalizado(string columna) => columna switch
    {
        "Personalizado1" => Personalizado1,
        "Personalizado2" => Personalizado2,
        "Personalizado3" => Personalizado3,
        "Personalizado4" => Personalizado4,
        _ => null
    };

    /// <summary>
    /// Usuario de WO que mandó el documento a aprobar (columna <c>IdUsuarioWO</c> — a pesar del
    /// nombre es texto libre, el usuario de sesión, ej. "LADY CARDONA"), NO necesariamente quien
    /// aparece como <c>IdTerceroInterno</c>/"elaborado por" — confirmado distintos en un caso real
    /// (2026-09-15).
    /// </summary>
    public string? SolicitanteWO { get; set; }
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

/// <summary>Un renglón/ítem de <c>CCA_M_Inventarios</c>, ya resuelto contra <c>Inventarios</c> para código/descripción.</summary>
public class RenglonRow
{
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public decimal Cantidad { get; set; }
    public string? UnidadMedida { get; set; }
    public decimal ValorUnitario { get; set; }
    public decimal? PorcentajeIva { get; set; }
}

/// <summary>Una dirección puntual de <c>Terceros - Direcciones</c>, con la ciudad ya resuelta contra <c>Ciudades</c> — a diferencia de <see cref="WorldOfficeReader.ObtenerDireccionPrincipalAsync"/>, esto busca por <c>IdDirección</c> exacto, no "la principal del tercero".</summary>
public class DireccionRow
{
    public string? Direccion { get; set; }
    public string? Telefonos { get; set; }
    public string? Ciudad { get; set; }
}

/// <summary>
/// Una línea débito/crédito de <c>[CuentasContables - Asientos - Detalles]</c> — esquema confirmado
/// con datos reales (comprobante 14893, 2026-09-22, ver ProyectosJimaco/WorkflowDocumentos/
/// investigar-comprobante-egreso-v3.sql): <c>IdCuentaContable</c> es interno, no el código impreso
/// (ese sale de <see cref="CuentaContableRow"/>).
/// </summary>
public class DetalleComprobanteEgresoRow
{
    public int IdCuentaContable { get; set; }
    public decimal Debito { get; set; }
    public decimal Credito { get; set; }
    public string? Nota { get; set; }
    public int Renglon { get; set; }
}

/// <summary>
/// Una fila del catálogo <c>CuentasContables</c> (sin sufijo) — resuelve <c>IdCuentaContable</c> al
/// código real impreso (ej. "133005") y su nombre (ej. "A PROVEEDORES"), confirmado exacto contra
/// el comprobante 14893 (2026-09-22).
/// </summary>
public class CuentaContableRow
{
    public int IdCuentaContable { get; set; }
    public string? CodigoCuentaContable { get; set; }
    public string? CuentaContable { get; set; }
}

/// <summary>
/// Cabecera de una Remisión (prefijo <c>REM</c>) con su campo libre "COTIZACION" —
/// <c>Personalizado4</c>, confirmado contra la Remisión real 29295 (2026-10-09). Es texto escrito a
/// mano por distintas personas, sin patrón fijo (puede traer "16113", "COT16113",
/// "16113-16159"...) — el parseo (extraer los números) se hace en <c>Program.cs</c>, no acá.
/// </summary>
public class RemisionRow
{
    public int IdAsientoContable { get; set; }
    public decimal DocumentoNumero { get; set; }
    public DateTime Fecha { get; set; }
    public string? Cotizacion { get; set; }
}

/// <summary>
/// Cabecera mínima de una Cotización reciente (prefijo <c>COT</c>) — solo lo necesario para decidir
/// si ya existe en nuestro sistema; la lectura completa (cliente, renglones, etc.) se hace después,
/// vía <see cref="WorldOfficeReader.ObtenerDocumentoVinculadoAsync"/>, igual que el resto del
/// Sincronizador (ver "Hojas de Alistamiento sin OC asociada", 2026-10-21).
/// </summary>
public class CotizacionRecienteRow
{
    public decimal DocumentoNumero { get; set; }
    public DateTime Fecha { get; set; }
}

/// <summary>
/// Encabezado de una Remisión (prefijo <c>REM</c>) para el módulo de Entregas (2026-11-12).
/// Personalizado1..4 confirmados con la REM 29635: dirección de entrega, campo entrega/rec, OC del
/// cliente, cotización. <see cref="Contabilizada"/> = tiene movimientos en
/// [CuentasContables - Asientos - Detalles] (WO no tiene bandera de "contabilizado" en el encabezado).
/// </summary>
public class RemisionEntregaRow
{
    public int IdAsientoContable { get; set; }
    public decimal DocumentoNumero { get; set; }
    public DateTime Fecha { get; set; }
    public int? IdTerceroExterno { get; set; }
    public int? IdTerceroInterno { get; set; }
    public int? IdDireccionExterno { get; set; }
    public int? IdFormaDePago { get; set; }
    public string? Nota { get; set; }
    public string? Personalizado1 { get; set; }
    public string? Personalizado2 { get; set; }
    public string? Personalizado3 { get; set; }
    public string? Personalizado4 { get; set; }
    public string? IdUsuarioWO { get; set; }
    public bool Anulada { get; set; }
    public bool Contabilizada { get; set; }
}

