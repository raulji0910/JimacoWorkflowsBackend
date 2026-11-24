using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Conciliación de facturas de proveedor (XML DIAN) vs OC de World Office — ver ConciliacionService.</summary>
public interface IConciliacionService
{
    // Sincronizador (usuario de servicio).
    Task<RegistroFacturasResultadoDto> RegistrarFacturasAsync(IReadOnlyList<FacturaXmlEntradaDto> facturas, CancellationToken ct = default);
    Task<SincronizacionOrdenesResultadoDto> SincronizarOrdenesAsync(IReadOnlyList<OrdenCompraEntradaDto> ordenes, CancellationToken ct = default);
    Task<SincronizacionFacturasCompraResultadoDto> SincronizarFacturasCompraAsync(SincronizacionFacturasCompraEntradaDto entrada, CancellationToken ct = default);

    // Pantalla (Rol.PuedeVerConciliacion).
    Task<ConciliacionDto> ConciliarAsync(DateOnly desde, DateOnly hasta, int usuarioId, CancellationToken ct = default);
    Task<DetalleFacturaConciliadaDto?> ObtenerDetalleAsync(int facturaId, int usuarioId, CancellationToken ct = default);
    Task<CargaArchivosResultadoDto> CargarArchivosAsync(IReadOnlyList<ArchivoCargado> archivos, int usuarioId, CancellationToken ct = default);
    Task<ArchivoDescargaDto> DescargarXmlAsync(IReadOnlyList<int> facturaIds, int usuarioId, CancellationToken ct = default);
    Task<ObservacionesOrdenDto?> ObtenerObservacionesOrdenAsync(int idAsientoContable, int usuarioId, CancellationToken ct = default);
    Task<ObservacionesOrdenDto?> AgregarObservacionOrdenAsync(int idAsientoContable, NuevaObservacionOrdenDto dto, int usuarioId, CancellationToken ct = default);
    Task<ObservacionesFacturaDto?> ObtenerObservacionesFacturaAsync(int facturaId, int usuarioId, CancellationToken ct = default);
    Task<ObservacionesFacturaDto?> AgregarObservacionFacturaAsync(int facturaId, NuevaObservacionFacturaDto dto, int usuarioId, CancellationToken ct = default);
}
