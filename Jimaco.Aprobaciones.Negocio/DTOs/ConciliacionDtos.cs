using Jimaco.Aprobaciones.Negocio.Conciliacion;

namespace Jimaco.Aprobaciones.Negocio.DTOs;

// ---- Lo que sube el Sincronizador ----

// Un XML tal como salió del ZIP adjunto al correo. La API lo interpreta (no el Sincronizador),
// así las reglas de lectura viven en un solo lugar.
public record FacturaXmlEntradaDto(string Xml, string? NombreArchivo, string? CorreoAsunto, DateTime? CorreoFecha);

// Resultado de subir ZIP/XML desde la pantalla (descargados de la DIAN): un renglón por XML leído.
// Estado: "Nueva", "Duplicada" (ya estaba, por correo o por otra carga), "NoReconocida" (no es una
// factura electrónica) u "OtroReceptor" (factura que no es para Jimaco).
public record FacturaCargadaDto(string? NombreArchivo, string? Numero, string? NitProveedor, string? NombreProveedor,
    DateOnly? FechaEmision, decimal? Total, string Estado, string? OrigenExistente);

public static class EstadoCarga
{
    public const string Nueva = "Nueva";
    public const string Duplicada = "Duplicada";
    public const string NoReconocida = "NoReconocida";
    public const string OtroReceptor = "OtroReceptor";
}

// Un archivo subido desde la pantalla (lo arma el controlador a partir del IFormFile).
public record ArchivoCargado(string Nombre, Stream Contenido);

public record CargaArchivosResultadoDto(int Archivos, int Nuevas, int Duplicadas, int NoReconocidas, int OtroReceptor,
    IReadOnlyList<FacturaCargadaDto> Facturas, IReadOnlyList<string> Avisos);

public record RegistroFacturasResultadoDto(int Nuevas, int Duplicadas, int NoReconocidas);

public record OrdenCompraRenglonEntradaDto(string? Codigo, string Descripcion, decimal Cantidad, decimal ValorUnitario, decimal PorcentajeIva);

public record OrdenCompraEntradaDto(
    int IdAsientoContable,
    int Numero,
    DateOnly Fecha,
    string NitProveedor,
    string NombreProveedor,
    bool Anulada,
    IReadOnlyList<OrdenCompraRenglonEntradaDto> Renglones,
    // "Elaborado por" de la OC en WO (IdTerceroInterno) — adición de Jimaco.
    string? Emisor = null);

public record SincronizacionOrdenesResultadoDto(int Nuevas, int Actualizadas);

// Encabezado de una FC de World Office. Desde: la ventana que leyó el Sincronizador; la API borra
// las FC de esa ventana que ya no vinieron (anuladas a mano, borradas).
public record FacturaCompraEntradaDto(
    int IdAsientoContable,
    int Numero,
    DateOnly Fecha,
    string NitProveedor,
    string NombreProveedor,
    string? DocumentoExterno,
    string? ReferenciaOrdenes,
    bool Anulada);

public record SincronizacionFacturasCompraEntradaDto(DateOnly Desde, IReadOnlyList<FacturaCompraEntradaDto> Facturas);

public record SincronizacionFacturasCompraResultadoDto(int Nuevas, int Actualizadas, int Eliminadas);

// ---- Lo que consulta la pantalla de conciliación ----

public record FacturaConciliadaDto(
    int FacturaId,
    string Numero,
    DateOnly FechaEmision,
    string NitProveedor,
    string NombreProveedor,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    string? ReferenciaOrden,
    EstadoConciliacion Estado,
    int? OrdenCompraNumero,
    DateOnly? OrdenCompraFecha,
    decimal? OrdenCompraSubtotal,
    decimal? OrdenCompraIva,
    decimal? DiferenciaSubtotal,
    decimal? DiferenciaIva,
    string Detalle,
    // Todas las OC del cruce (más de una solo cuando salió de una FC de WO que cita varias) y la FC
    // de WO de donde salió el cruce, si fue por esa vía. OrdenCompraSubtotal/Iva suman todas.
    IReadOnlyList<int> OrdenesCompra,
    int? FacturaCompraNumero,
    // Por dónde entró la factura: "Correo", "Carpeta" o "Dian" (ZIP subido desde la pantalla).
    string Origen,
    // Adiciones de Jimaco (2026-11-24): quién elaboró la(s) OC en WO y la revisión de la factura
    // (observaciones en Parciales / Con diferencia, con la marca "Revisada").
    IReadOnlyList<string>? EmisoresOrdenCompra = null,
    int Observaciones = 0,
    string? UltimaObservacion = null,
    bool Revisada = false);

// Observaciones: cuántas entradas tiene el historial de revisión; UltimaObservacion: el texto más
// reciente (para el tooltip); NoRequiereFactura: quien revisó la marcó así (sigue en la lista).
public record OrdenPendienteDto(
    int Numero,
    DateOnly Fecha,
    string NitProveedor,
    string NombreProveedor,
    decimal Subtotal,
    decimal Iva,
    decimal SaldoSubtotal,
    int IdAsientoContable = 0,
    int Observaciones = 0,
    string? UltimaObservacion = null,
    bool NoRequiereFactura = false,
    string? Emisor = null);

// ---- Observaciones de las OC sin factura ----

public enum AccionObservacionOrden
{
    Observacion,
    MarcarNoRequiereFactura,
    QuitarMarcaNoRequiereFactura
}

public record NuevaObservacionOrdenDto(string Texto, AccionObservacionOrden Accion = AccionObservacionOrden.Observacion);

public record ObservacionOrdenDto(int Id, string Tipo, string Texto, string NombreUsuario, DateTime Fecha);

public record ObservacionesOrdenDto(
    int IdAsientoContable,
    int Numero,
    DateOnly Fecha,
    string NitProveedor,
    string NombreProveedor,
    decimal Subtotal,
    decimal Iva,
    bool NoRequiereFactura,
    IReadOnlyList<ObservacionOrdenDto> Historial,
    string? Emisor = null);

// ---- Observaciones de las facturas Parciales / Con diferencia (adición de Jimaco, 2026-11-24) ----

public enum AccionObservacionFactura
{
    Observacion,
    MarcarRevisada,
    QuitarMarcaRevisada
}

public record NuevaObservacionFacturaDto(string Texto, AccionObservacionFactura Accion = AccionObservacionFactura.Observacion);

public record ObservacionesFacturaDto(
    int FacturaId,
    string Numero,
    DateOnly FechaEmision,
    string NitProveedor,
    string NombreProveedor,
    decimal Subtotal,
    decimal Iva,
    bool Revisada,
    IReadOnlyList<ObservacionOrdenDto> Historial);

// Un XML para descargar (uno solo, o el ZIP de varios).
public record ArchivoDescargaDto(string NombreArchivo, string ContentType, byte[] Contenido);

public record OrdenFacturadaSinXmlDto(
    int Numero,
    DateOnly Fecha,
    string NitProveedor,
    string NombreProveedor,
    decimal Subtotal,
    decimal Iva,
    int FacturaCompraNumero,
    DateOnly FacturaCompraFecha,
    string? DocumentoExterno,
    string? Emisor = null);

public record ReferenciaOrdenInvalidaDto(
    int FacturaCompraNumero,
    DateOnly Fecha,
    string NitProveedor,
    string NombreProveedor,
    string? DocumentoExterno,
    string? ReferenciaOrdenes,
    int OrdenCitada,
    string Motivo);

public record ResumenConciliacionDto(
    int Cuadran,
    int ConDiferencia,
    int Parciales,
    int SinOrdenCompra,
    int OrdenesPendientes,
    int OrdenesFacturadasSinXml,
    int ReferenciasInvalidas);

public record ConciliacionDto(
    DateOnly Desde,
    DateOnly Hasta,
    ResumenConciliacionDto Resumen,
    IReadOnlyList<FacturaConciliadaDto> Facturas,
    IReadOnlyList<OrdenPendienteDto> OrdenesPendientes,
    IReadOnlyList<OrdenFacturadaSinXmlDto> OrdenesFacturadasSinXml,
    IReadOnlyList<ReferenciaOrdenInvalidaDto> ReferenciasInvalidas,
    DateTime? UltimaSincronizacionOrdenes,
    DateTime? UltimaFacturaRecibida);

public record RenglonComparacionDto(string? Codigo, string Descripcion, decimal Cantidad, decimal ValorUnitario, decimal Subtotal, decimal? PorcentajeIva);

public record DetalleFacturaConciliadaDto(
    FacturaConciliadaDto Conciliacion,
    IReadOnlyList<RenglonComparacionDto> RenglonesFactura,
    IReadOnlyList<RenglonComparacionDto> RenglonesOrdenCompra);
