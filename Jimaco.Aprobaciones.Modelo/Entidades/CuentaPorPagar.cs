using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Cuentas por pagar (2026-11-05) — una partida ABIERTA de la vista <c>Vista_CuentasPorPagar_Detallada</c>
/// de World Office (facturas de compra a crédito y demás saldos con proveedores, solo las cuentas
/// configuradas en el Sincronizador: 2205/2210/2335). Es una FOTO: el Sincronizador la reemplaza
/// entera en cada corrida (la Api no tiene acceso directo a WO), por eso no hay historia acá — una
/// partida que WO ya cruzó con un CEJ simplemente deja de venir en la foto siguiente.
/// </summary>
public class PartidaCuentaPorPagar
{
    public int Id { get; set; }

    /// <summary>
    /// <c>AbonaADocumento</c> de la vista = el <c>IdDocumentoDetalle</c> del renglón contable que
    /// abrió la partida (los abonos de WO apuntan a ese mismo Id). Es la llave estable entre fotos —
    /// el Id de esta tabla cambia en cada reemplazo, este no. Null en partidas sin renglón de origen
    /// (ej. algunos saldos iniciales): se muestran pero no se pueden mandar a pagar.
    /// </summary>
    public int? IdDocumentoDetalleWO { get; set; }

    [MaxLength(30)]
    public required string Identificacion { get; set; }

    [MaxLength(300)]
    public required string NombreTercero { get; set; }

    /// <summary>Tipo de documento según WO, ej. "Factura de Compra", "Compra Remisionada", "Comprobante de Egreso".</summary>
    [MaxLength(100)]
    public string? Documento { get; set; }

    [MaxLength(20)]
    public string? Prefijo { get; set; }

    [MaxLength(30)]
    public string? NumeroDocumento { get; set; }

    /// <summary>Número de la factura del proveedor (<c>NúmDocumentoExterno</c>), ej. "FE506".</summary>
    [MaxLength(100)]
    public string? NumeroDocumentoExterno { get; set; }

    public DateTime Fecha { get; set; }

    public DateTime? Vencimiento { get; set; }

    /// <summary>
    /// Lo que se le debe al tercero por esta partida: positivo = Jimaco debe (la vista de WO lo trae
    /// en negativo, naturaleza crédito — se invierte al guardar para que se lea natural); negativo =
    /// saldo a favor de Jimaco (pagos o notas crédito sin aplicar a ninguna factura).
    /// </summary>
    public decimal Pendiente { get; set; }

    [MaxLength(20)]
    public string? CodigoCuenta { get; set; }
}

/// <summary>
/// Un anticipo a proveedor sin cruzar (cuenta 133005 en WO) — plata que ya salió y que todavía no
/// se aplicó contra ninguna factura. Desde 2026-11-06 es POR PARTIDA (cada anticipo es un renglón de
/// un CEJ que se autorreferencia en <c>AbonaADocumento</c>; lo que le queda es la suma de los
/// renglones que apuntan a él), para poder cruzarlo puntualmente en el CE que crea el sistema. Misma
/// foto que <see cref="PartidaCuentaPorPagar"/>, reemplazada entera en cada corrida.
/// </summary>
public class AnticipoProveedor
{
    public int Id { get; set; }

    /// <summary><c>IdDocumentoDetalle</c> del renglón que abrió el anticipo — llave estable entre fotos. Null en fotos viejas (por tercero).</summary>
    public int? IdDocumentoDetalleWO { get; set; }

    [MaxLength(30)]
    public required string Identificacion { get; set; }

    [MaxLength(300)]
    public required string NombreTercero { get; set; }

    [MaxLength(20)]
    public string? Prefijo { get; set; }

    [MaxLength(30)]
    public string? NumeroDocumento { get; set; }

    public DateTime? Fecha { get; set; }

    /// <summary>Nota del renglón de origen, ej. "ANTICIPO COMPRA OC 19460-YAZMIN" — es lo que le dice al gerente a qué corresponde.</summary>
    [MaxLength(300)]
    public string? Nota { get; set; }

    public int Movimientos { get; set; }

    /// <summary>Positivo = Jimaco adelantó esta plata y todavía no se descontó de ninguna factura.</summary>
    public decimal Saldo { get; set; }
}

/// <summary>Una cuenta de banco o caja de WO (hojas activas de 1105/1110) desde donde se puede pagar — foto del Sincronizador.</summary>
public class CuentaBancoWO
{
    public int Id { get; set; }

    /// <summary><c>IdCuentaContable</c> en WO — es lo que va en el renglón crédito del CE.</summary>
    public int IdCuentaContableWO { get; set; }

    [MaxLength(20)]
    public required string Codigo { get; set; }

    [MaxLength(200)]
    public required string Nombre { get; set; }

    /// <summary>La que se propone por defecto al confirmar un pago (Bancolombia, según la configuración del Sincronizador).</summary>
    public bool PorDefecto { get; set; }
}

/// <summary>Un anticipo que se descuenta dentro de un documento "Pago a Proveedor" (crédito a 133005 en el CE).</summary>
public class AnticipoPagoProveedor
{
    public int Id { get; set; }

    public int InstanciaDocumentoId { get; set; }
    public InstanciaDocumento InstanciaDocumento { get; set; } = null!;

    public int IdDocumentoDetalleWO { get; set; }

    [MaxLength(20)]
    public string? Prefijo { get; set; }

    [MaxLength(30)]
    public string? NumeroDocumento { get; set; }

    public DateTime? Fecha { get; set; }

    [MaxLength(300)]
    public string? Nota { get; set; }

    /// <summary>Lo que le quedaba al anticipo cuando se decidió el pago — si WO sigue mostrando esto después de completado, el CE todavía no lo cruzó.</summary>
    public decimal SaldoAnterior { get; set; }

    /// <summary>Lo que se aplica en este pago (puede ser menos que el saldo si las facturas no alcanzan a cubrirlo).</summary>
    public decimal Valor { get; set; }
}

/// <summary>
/// Datos del pago real de un documento "Pago a Proveedor" (2026-11-06): lo que el gerente general
/// confirma al aprobar (banco y fecha) y el estado del Comprobante de Egreso que el Sincronizador
/// crea en WO con eso. 1:1 con el documento, se crea junto con él.
/// </summary>
public class PagoProveedor
{
    public int Id { get; set; }

    public int InstanciaDocumentoId { get; set; }
    public InstanciaDocumento InstanciaDocumento { get; set; } = null!;

    [MaxLength(30)]
    public required string Identificacion { get; set; }

    /// <summary>Banco elegido al confirmar el pago. Null si el neto es 0 (cruce puro de anticipos, sin salida de plata).</summary>
    public int? IdCuentaBancoWO { get; set; }

    [MaxLength(200)]
    public string? NombreBanco { get; set; }

    /// <summary>Fecha de calendario del pago = fecha del CE en WO.</summary>
    public DateTime? FechaPago { get; set; }

    /// <summary>true desde que el gerente general confirma el pago hasta que el Sincronizador crea el CE (o reporta un conflicto).</summary>
    public bool PendienteCreacionCE { get; set; }

    [MaxLength(1000)]
    public string? ConflictoCreacionCE { get; set; }

    [MaxLength(20)]
    public string? PrefijoCE { get; set; }

    public decimal? NumeroCE { get; set; }

    public int? IdAsientoContableCE { get; set; }

    public DateTime? FechaCreacionCE { get; set; }
}

/// <summary>Cuándo se tomó la foto vigente (una sola fila) — para mostrar "Actualizado hace X minutos".</summary>
public class SincronizacionCuentasPorPagar
{
    public int Id { get; set; }

    public DateTime FechaCorte { get; set; }

    public int Partidas { get; set; }

    public int Anticipos { get; set; }
}

/// <summary>
/// Una partida que el gerente comercial mandó a pagar dentro de un documento "Pago a Proveedor".
/// Copia los datos al momento de la decisión (la foto de WO cambia sola) y es lo que marca la
/// partida como "en trámite" para que no se pueda mandar a pagar dos veces.
/// </summary>
public class FacturaPagoProveedor
{
    public int Id { get; set; }

    public int InstanciaDocumentoId { get; set; }
    public InstanciaDocumento InstanciaDocumento { get; set; } = null!;

    public int IdDocumentoDetalleWO { get; set; }

    [MaxLength(20)]
    public string? Prefijo { get; set; }

    [MaxLength(30)]
    public string? NumeroDocumento { get; set; }

    [MaxLength(100)]
    public string? NumeroDocumentoExterno { get; set; }

    public DateTime Fecha { get; set; }

    public DateTime? Vencimiento { get; set; }

    public decimal Valor { get; set; }

    [MaxLength(20)]
    public string? CodigoCuenta { get; set; }
}
