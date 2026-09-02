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
}
