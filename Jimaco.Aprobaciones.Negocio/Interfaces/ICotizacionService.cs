using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Cotizaciones armadas en el portal y creadas en World Office (2026-11-25) — ver Entidades/Cotizacion.cs.</summary>
public interface ICotizacionService
{
    // Portal (Rol.PuedeCrearCotizaciones; cada quien ve y edita las suyas).
    Task<IReadOnlyList<CotizacionResumenDto>> ListarAsync(int usuarioId, CancellationToken ct = default);
    Task<CotizacionDto> ObtenerAsync(int id, int usuarioId, CancellationToken ct = default);
    Task<CotizacionDto> CrearAsync(GuardarCotizacionDto dto, int usuarioId, CancellationToken ct = default);
    Task<CotizacionDto> ActualizarAsync(int id, GuardarCotizacionDto dto, int usuarioId, CancellationToken ct = default);
    Task EliminarAsync(int id, int usuarioId, CancellationToken ct = default);
    Task<CotizacionDto> EnviarAsync(int id, int usuarioId, CancellationToken ct = default);
    Task<CotizacionDto> CopiarAsync(int id, int usuarioId, CancellationToken ct = default);
    /// <summary>PDF con el formato de impresión de WO; nombre de archivo sugerido.</summary>
    Task<(byte[] Pdf, string NombreArchivo)> GenerarPdfAsync(int id, int usuarioId, CancellationToken ct = default);

    // Sincronizador (modo --documentos).
    Task<IReadOnlyList<PendienteCreacionCotizacionDto>> ListarPendientesCreacionAsync(CancellationToken ct = default);
    Task ConfirmarCreadaAsync(int id, CotizacionCreadaDto dto, CancellationToken ct = default);
    Task ReportarErrorAsync(int id, string mensaje, CancellationToken ct = default);
}
