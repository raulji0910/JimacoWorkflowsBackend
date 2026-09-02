using System.Security.Claims;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsuariosController(IUsuarioService usuarioService) : ControllerBase
{
    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<UsuarioDto>>> Listar(CancellationToken ct) =>
        Ok(await usuarioService.ListarAsync(ct));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UsuarioDto>> Crear(CrearUsuarioDto dto, CancellationToken ct) =>
        Ok(await usuarioService.CrearAsync(dto, ct));

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UsuarioDto>> Actualizar(int id, ActualizarUsuarioDto dto, CancellationToken ct) =>
        Ok(await usuarioService.ActualizarAsync(id, dto, UsuarioActualId, ct));

    [HttpPost("mi-password")]
    public async Task<IActionResult> CambiarMiPassword(CambiarPasswordDto dto, CancellationToken ct)
    {
        await usuarioService.CambiarPasswordAsync(UsuarioActualId, dto, ct);
        return NoContent();
    }
}
