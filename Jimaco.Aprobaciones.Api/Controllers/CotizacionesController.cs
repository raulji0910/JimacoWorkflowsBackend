using System.Security.Claims;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

/// <summary>
/// Cotizaciones del portal (2026-11-25): se arman acá y el Sincronizador --documentos las crea en
/// World Office. Exige <c>Rol.PuedeCrearCotizaciones</c>; cada quien ve y edita las suyas (lo valida el servicio).
/// </summary>
[ApiController]
[Route("api/cotizaciones")]
[Authorize]
public class CotizacionesController(ICotizacionService cotizacionService) : ControllerBase
{
    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CotizacionResumenDto>>> Listar(CancellationToken ct) =>
        Ok(await cotizacionService.ListarAsync(UsuarioActualId, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CotizacionDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await cotizacionService.ObtenerAsync(id, UsuarioActualId, ct));

    [HttpPost]
    public async Task<ActionResult<CotizacionDto>> Crear(GuardarCotizacionDto dto, CancellationToken ct) =>
        Ok(await cotizacionService.CrearAsync(dto, UsuarioActualId, ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CotizacionDto>> Actualizar(int id, GuardarCotizacionDto dto, CancellationToken ct) =>
        Ok(await cotizacionService.ActualizarAsync(id, dto, UsuarioActualId, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await cotizacionService.EliminarAsync(id, UsuarioActualId, ct);
        return NoContent();
    }

    /// <summary>Valida y la deja lista para que el Sincronizador la cree en WO (en menos de un minuto).</summary>
    [HttpPost("{id:int}/enviar")]
    public async Task<ActionResult<CotizacionDto>> Enviar(int id, CancellationToken ct) =>
        Ok(await cotizacionService.EnviarAsync(id, UsuarioActualId, ct));

    [HttpPost("{id:int}/copiar")]
    public async Task<ActionResult<CotizacionDto>> Copiar(int id, CancellationToken ct) =>
        Ok(await cotizacionService.CopiarAsync(id, UsuarioActualId, ct));
}
