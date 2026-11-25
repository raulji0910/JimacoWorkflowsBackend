using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Catálogo de World Office para crear documentos desde el portal (2026-11-25) — ver Entidades/CatalogoWO.cs.</summary>
public interface ICatalogoWOService
{
    // Sincronizador (modo --documentos).
    Task<EstadoCatalogoWODto> ObtenerEstadoAsync(CancellationToken ct = default);
    Task<int> GuardarProductosAsync(IReadOnlyList<ProductoWOInputDto> productos, CancellationToken ct = default);
    Task<int> GuardarExistenciasAsync(IReadOnlyList<ExistenciaWOInputDto> existencias, CancellationToken ct = default);
    Task GuardarCatalogosAsync(CatalogosWOInputDto catalogos, CancellationToken ct = default);

    // Portal (Rol.PuedeCrearCotizaciones).
    Task<CatalogoCotizacionDto> ObtenerCatalogoCotizacionAsync(int usuarioId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductoCatalogoDto>> BuscarProductosAsync(string texto, int usuarioId, CancellationToken ct = default);
    Task<IReadOnlyList<ClienteCatalogoDto>> BuscarClientesAsync(string texto, int usuarioId, CancellationToken ct = default);
    Task<IReadOnlyList<DireccionClienteDto>> ListarDireccionesAsync(int idTercero, int usuarioId, CancellationToken ct = default);
}
