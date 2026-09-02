using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RolesController(IRolService rolService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RolDto>>> Listar(CancellationToken ct) =>
        Ok(await rolService.ListarAsync(ct));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RolDto>> Crear(CrearRolDto dto, CancellationToken ct) =>
        Ok(await rolService.CrearAsync(dto, ct));

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RolDto>> Actualizar(int id, ActualizarRolDto dto, CancellationToken ct) =>
        Ok(await rolService.ActualizarAsync(id, dto, ct));
}
