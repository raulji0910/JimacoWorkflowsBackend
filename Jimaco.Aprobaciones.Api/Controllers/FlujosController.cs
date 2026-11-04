using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FlujosController(IDefinicionFlujoService flujoService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DefinicionFlujoDto>>> ListarPorTipoDocumento([FromQuery] int tipoDocumentoId, CancellationToken ct) =>
        Ok(await flujoService.ListarPorTipoDocumentoAsync(tipoDocumentoId, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DefinicionFlujoDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await flujoService.ObtenerAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<DefinicionFlujoDto>> Crear(CrearDefinicionFlujoDto dto, CancellationToken ct) =>
        Ok(await flujoService.CrearAsync(dto, ct));

    [HttpPost("{id:int}/desactivar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        await flujoService.DesactivarAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/activar")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Activar(int id, CancellationToken ct)
    {
        await flujoService.ActivarAsync(id, ct);
        return NoContent();
    }
}
