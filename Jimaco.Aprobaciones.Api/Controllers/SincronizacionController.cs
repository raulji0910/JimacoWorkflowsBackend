using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

/// <summary>
/// Endpoints que usa Jimaco.Aprobaciones.Sincronizador (no la UI) para reflejar en World Office la
/// aprobación del primer paso de un documento sincronizado — ver "Escritura de vuelta a World
/// Office" en CLAUDE.md. El Sincronizador ya se autentica como un Usuario normal (usuario de
/// servicio) vía /api/auth/login, así que estos endpoints solo requieren estar logueado, igual
/// que el resto de DocumentosController.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SincronizacionController(IInstanciaDocumentoService instanciaService) : ControllerBase
{
    [HttpGet("pendientes-wo")]
    public async Task<ActionResult<IReadOnlyList<PendienteEscrituraWODto>>> ListarPendientesWO(CancellationToken ct) =>
        Ok(await instanciaService.ListarPendientesEscrituraWOAsync(ct));

    [HttpPost("pendientes-wo/{id:int}/confirmar")]
    public async Task<IActionResult> ConfirmarWO(int id, CancellationToken ct)
    {
        await instanciaService.ConfirmarEscrituraWOAsync(id, ct);
        return Ok();
    }

    [HttpPost("pendientes-wo/{id:int}/conflicto")]
    public async Task<IActionResult> ReportarConflictoWO(int id, ReportarConflictoWODto dto, CancellationToken ct)
    {
        await instanciaService.ReportarConflictoWOAsync(id, dto.Mensaje, ct);
        return Ok();
    }
}
