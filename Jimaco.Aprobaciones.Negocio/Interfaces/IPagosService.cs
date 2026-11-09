using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Menú "Pagos" del pagador (Rol.PuedePagarCuentas) — ver PagosService.</summary>
public interface IPagosService
{
    Task<IReadOnlyList<OrdenPagoPendienteDto>> ListarPendientesAsync(int usuarioId, CancellationToken ct = default);

    Task<LotePagoDto> PagarLoteAsync(IReadOnlyList<int> instanciasIds, int? idCuentaBancoWO, DateTime? fechaPago,
        string? nombreArchivo, string? contentType, long tamanoBytes, Stream? soporte, int usuarioId, CancellationToken ct = default);

    Task RechazarAsync(int instanciaDocumentoId, string comentario, int usuarioId, CancellationToken ct = default);

    Task<IReadOnlyList<LotePagoDto>> ListarLotesAsync(int usuarioId, CancellationToken ct = default);

    Task<(Stream Contenido, string NombreArchivo)> DescargarSoporteLoteAsync(int lotePagoId, int usuarioId, CancellationToken ct = default);
}
