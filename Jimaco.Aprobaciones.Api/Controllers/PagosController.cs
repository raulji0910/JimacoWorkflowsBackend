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

    /// <summary>
    /// Paga como un lote: ids separados por coma, banco, fecha y los soportes PDF del lote (uno o
    /// varios, campo "soportes"; "soporte" sigue aceptándose por compatibilidad con la web anterior).
    /// </summary>
    [HttpPost("lotes")]
    [RequestSizeLimit(60_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 60_000_000)]
    public async Task<ActionResult<LotePagoDto>> PagarLote([FromForm] string ids, [FromForm] int? idCuentaBancoWO, [FromForm] DateTime? fechaPago,
        [FromForm] List<IFormFile>? soportes, IFormFile? soporte, CancellationToken ct)
    {
        var lista = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToList();

        var archivos = (soportes ?? []).Concat(soporte is null ? [] : [soporte]).Where(f => f.Length > 0).ToList();
        var abiertos = new List<Stream>();
        try
        {
            var entradas = archivos.Select(f =>
            {
                var contenido = f.OpenReadStream();
                abiertos.Add(contenido);
                return new ArchivoSoporteInput(f.FileName, f.ContentType, f.Length, contenido);
            }).ToList();

            // Sin el "ct" de la petición a propósito (mismo motivo que el guardado de alistamiento): si el
            // navegador se corta a mitad, el lote igual tiene que quedar completo o no quedar.
            return Ok(await pagosService.PagarLoteAsync(lista, idCuentaBancoWO, fechaPago, entradas, UsuarioActualId, CancellationToken.None));
        }
        finally
        {
            foreach (var a in abiertos) await a.DisposeAsync();
        }
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

    /// <summary>Manda (o reenvía) el reporte de un lote terminado a Contabilidad. Devuelve a cuántos usuarios.</summary>
    [HttpPost("lotes/{id:int}/reporte-contabilidad")]
    public async Task<ActionResult<object>> EnviarReporteContabilidad(int id) =>
        // Sin el "ct" de la petición: armar los PDF y mandar el correo puede tardar; que no se corte a mitad.
        Ok(new { enviados = await pagosService.EnviarReporteContabilidadAsync(id, UsuarioActualId, CancellationToken.None) });

    /// <summary>El primer soporte del lote (lotes viejos y web anterior).</summary>
    [HttpGet("lotes/{id:int}/soporte")]
    public async Task<IActionResult> Soporte(int id, CancellationToken ct)
    {
        var (contenido, nombre) = await pagosService.DescargarSoporteLoteAsync(id, null, UsuarioActualId, ct);
        return File(contenido, "application/pdf", nombre);
    }

    /// <summary>Un soporte puntual del lote (2026-11-23: puede haber varios). Id 0 = el único de un lote viejo.</summary>
    [HttpGet("lotes/{id:int}/soportes/{soporteId:int}")]
    public async Task<IActionResult> SoportePorId(int id, int soporteId, CancellationToken ct)
    {
        var (contenido, nombre) = await pagosService.DescargarSoporteLoteAsync(id, soporteId, UsuarioActualId, ct);
        return File(contenido, "application/pdf", nombre);
    }
}
