using System.Security.Claims;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HojasAlistamientoController(IHojaAlistamientoService hojaAlistamientoService) : ControllerBase
{
    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>?empresa=Ferrealiados → el dashboard aparte de Ferrealiados (2026-11-13). Sin parámetro = Jimaco, como siempre.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HojaAlistamientoResumenDto>>> Listar([FromQuery] string? empresa, CancellationToken ct) =>
        Ok(await hojaAlistamientoService.ListarAsync(UsuarioActualId, ct, EmpresaExtensions.Parsear(empresa)));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HojaAlistamientoDetalleDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await hojaAlistamientoService.ObtenerAsync(id, UsuarioActualId, ct));

    // Sin el CancellationToken de la petición, a propósito (2026-11-09, Hoja 16100): si el operario
    // cierra/recarga la página o el celular suspende el navegador mientras esto corre, ASP.NET
    // cancelaba el guardado a mitad de camino. Una vez que la cantidad llegó al servidor, se guarda.
    [HttpPut("{id:int}/cantidades")]
    public async Task<IActionResult> ActualizarCantidades(int id, ActualizarCantidadesDisponiblesDto dto)
    {
        await hojaAlistamientoService.ActualizarCantidadesAsync(id, UsuarioActualId, dto, CancellationToken.None);
        return NoContent();
    }
}
