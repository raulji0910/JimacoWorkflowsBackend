using System.Security.Claims;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

/// <summary>Menú "Pagos" del pagador (2026-11-09) — ver PagosService.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PagosController(IPagosService pagosService) : ControllerBase
{
    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("pendientes")]
    public async Task<ActionResult<IReadOnlyList<OrdenPagoPendienteDto>>> Pendientes(CancellationToken ct) =>
        Ok(await pagosService.ListarPendientesAsync(UsuarioActualId, ct));

    /// <summary>Paga como un lote: ids separados por coma, banco, fecha y UN soporte PDF para todo.</summary>
    [HttpPost("lotes")]
    [RequestSizeLimit(30_000_000)]
    public async Task<ActionResult<LotePagoDto>> PagarLote([FromForm] string ids, [FromForm] int? idCuentaBancoWO, [FromForm] DateTime? fechaPago,
        IFormFile? soporte, CancellationToken ct)
    {
        var lista = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToList();

        // Sin el "ct" de la petición a propósito (mismo motivo que el guardado de alistamiento): si el
        // navegador se corta a mitad, el lote igual tiene que quedar completo o no quedar.
        await using var contenido = soporte?.OpenReadStream();
        return Ok(await pagosService.PagarLoteAsync(lista, idCuentaBancoWO, fechaPago, soporte?.FileName, soporte?.ContentType,
            soporte?.Length ?? 0, contenido, UsuarioActualId, CancellationToken.None));
    }

    [HttpPost("ordenes/{id:int}/rechazar")]
    public async Task<IActionResult> Rechazar(int id, RechazarOrdenPagoDto dto, CancellationToken ct)
    {
        await pagosService.RechazarAsync(id, dto.Comentario, UsuarioActualId, ct);
        return NoContent();
    }

    [HttpGet("rechazados")]
    public async Task<ActionResult<PaginaDto<SeguimientoPagoDto>>> Rechazados([FromQuery] FiltroPagosDto filtro, CancellationToken ct) =>
        Ok(await pagosService.ListarRechazadosAsync(filtro, UsuarioActualId, ct));

    [HttpGet("lotes")]
    public async Task<ActionResult<PaginaDto<LotePagoDto>>> Lotes([FromQuery] FiltroPagosDto filtro, CancellationToken ct) =>
        Ok(await pagosService.ListarLotesAsync(filtro, UsuarioActualId, ct));

    [HttpGet("lotes/{id:int}/soporte")]
    public async Task<IActionResult> Soporte(int id, CancellationToken ct)
    {
        var (contenido, nombre) = await pagosService.DescargarSoporteLoteAsync(id, UsuarioActualId, ct);
        return File(contenido, "application/pdf", nombre);
    }
}
