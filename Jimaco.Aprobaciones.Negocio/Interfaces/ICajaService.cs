using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Cajas menores de efectivo (2026-11-27). Ver <see cref="Modelo.Entidades.Caja"/>.</summary>
public interface ICajaService
{
    Task<IReadOnlyList<CajaResumenDto>> ListarAsync(int usuarioId, CancellationToken ct = default);
    Task<CajaResumenDto> ObtenerAsync(int cajaId, int usuarioId, CancellationToken ct = default);
    Task<CajaResumenDto> ActualizarAsync(int cajaId, ActualizarCajaDto dto, int usuarioId, CancellationToken ct = default);

    Task<IReadOnlyList<PasoPagoCajaDto>> ListarPasosPagoAsync(int cajaId, int usuarioId, CancellationToken ct = default);
    Task<PasoPagoCajaDto> MarcarPasoPagoAsync(int cajaId, int pasoId, MarcarPasoPagoCajaDto dto, int usuarioId, CancellationToken ct = default);

    Task<IReadOnlyList<ConceptoCajaDto>> ListarConceptosAsync(int cajaId, int usuarioId, CancellationToken ct = default);
    Task<ConceptoCajaDto> CrearConceptoAsync(int cajaId, GuardarConceptoCajaDto dto, int usuarioId, CancellationToken ct = default);
    Task<ConceptoCajaDto> ActualizarConceptoAsync(int cajaId, int conceptoId, GuardarConceptoCajaDto dto, int usuarioId, CancellationToken ct = default);

    Task<IReadOnlyList<BeneficiarioCajaDto>> BuscarBeneficiariosAsync(string? texto, int usuarioId, CancellationToken ct = default);
    Task<BeneficiarioCajaDto> CrearBeneficiarioAsync(GuardarBeneficiarioCajaDto dto, int usuarioId, CancellationToken ct = default);
    Task<BeneficiarioCajaDto> ActualizarBeneficiarioAsync(int beneficiarioId, GuardarBeneficiarioCajaDto dto, int usuarioId, CancellationToken ct = default);

    Task<MovimientoCajaDto> RegistrarRecargaAsync(int cajaId, RegistrarRecargaDto dto, int usuarioId, CancellationToken ct = default);
    Task<ResultadoArqueoDto> RegistrarArqueoAsync(int cajaId, RegistrarArqueoDto dto, int usuarioId, CancellationToken ct = default);
    Task<MovimientoCajaDto> AnularMovimientoAsync(int cajaId, int movimientoId, AnularDto dto, int usuarioId, CancellationToken ct = default);
    Task<HistorialCajaDto> HistorialAsync(int cajaId, FiltroHistorialCajaDto filtro, int usuarioId, CancellationToken ct = default);

    Task<IReadOnlyList<OrdenCompraPendientePagoDto>> OrdenesCompraPendientesAsync(int cajaId, int usuarioId, CancellationToken ct = default);
    Task<IReadOnlyList<GiroCajaResumenDto>> ListarGirosAsync(int cajaId, bool soloPendientes, int usuarioId, CancellationToken ct = default);
    Task<GiroCajaDto> ObtenerGiroAsync(int cajaId, int giroId, int usuarioId, CancellationToken ct = default);
    Task<GiroCajaDto> CrearGiroAsync(int cajaId, CrearGiroCajaDto dto, int usuarioId, CancellationToken ct = default);
    Task<GiroCajaDto> LegalizarGiroAsync(int cajaId, int giroId, LegalizarGiroCajaDto dto, int usuarioId, CancellationToken ct = default);
    Task<GiroCajaDto> SubirSoporteAsync(int cajaId, int giroId, string nombreArchivo, string? contentType, Stream contenido, int usuarioId, CancellationToken ct = default);
    Task<(Stream Contenido, string NombreArchivo, string ContentType)> AbrirSoporteAsync(int cajaId, int giroId, int usuarioId, CancellationToken ct = default);
    Task<GiroCajaDto> RecibirVueltaAsync(int cajaId, int giroId, RecibirVueltaDto dto, int usuarioId, CancellationToken ct = default);
    Task<GiroCajaDto> AnularGiroAsync(int cajaId, int giroId, AnularDto dto, int usuarioId, CancellationToken ct = default);
    Task<(byte[] Pdf, string NombreArchivo)> GenerarPdfGiroAsync(int cajaId, int giroId, int usuarioId, CancellationToken ct = default);

    /// <summary>Recordatorio diario (cada 24 h) de giros sin legalizar y vueltas sin entregar. Lo llama el servicio en segundo plano.</summary>
    Task<int> EnviarRecordatoriosAsync(CancellationToken ct = default);
}
