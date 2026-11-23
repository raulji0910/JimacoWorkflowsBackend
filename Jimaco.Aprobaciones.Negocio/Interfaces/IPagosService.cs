using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Menú "Pagos" del pagador (Rol.PuedePagarCuentas) — ver PagosService.</summary>
public interface IPagosService
{
    Task<IReadOnlyList<OrdenPagoPendienteDto>> ListarPendientesAsync(int usuarioId, CancellationToken ct = default);

    /// <param name="soportes">Uno o varios PDF (mínimo uno si el lote mueve plata) — 2026-11-23.</param>
    Task<LotePagoDto> PagarLoteAsync(IReadOnlyList<int> instanciasIds, int? idCuentaBancoWO, DateTime? fechaPago,
        IReadOnlyList<ArchivoSoporteInput> soportes, int usuarioId, CancellationToken ct = default);

    Task RechazarAsync(int instanciaDocumentoId, string comentario, int usuarioId, CancellationToken ct = default);

    /// <summary>Lo que el pagador rechazó en los últimos 120 días, con el motivo.</summary>
    Task<PaginaDto<SeguimientoPagoDto>> ListarRechazadosAsync(FiltroPagosDto filtro, int usuarioId, CancellationToken ct = default);

    Task<PaginaDto<LotePagoDto>> ListarLotesAsync(FiltroPagosDto filtro, int usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Manda (o reenvía) el reporte de un lote YA TERMINADO a Contabilidad — 2026-11-23, para lotes
    /// pagados antes de que existiera el reporte o si el correo no llegó. Devuelve a cuántos usuarios.
    /// </summary>
    Task<int> EnviarReporteContabilidadAsync(int lotePagoId, int usuarioId, CancellationToken ct = default);

    /// <param name="soporteId">null = el primero del lote.</param>
    Task<(Stream Contenido, string NombreArchivo)> DescargarSoporteLoteAsync(int lotePagoId, int? soporteId, int usuarioId, CancellationToken ct = default);
}
