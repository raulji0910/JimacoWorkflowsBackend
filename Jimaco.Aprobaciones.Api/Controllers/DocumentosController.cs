using System.Security.Claims;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentosController(IInstanciaDocumentoService instanciaService) : ControllerBase
{
    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    public async Task<ActionResult<InstanciaDocumentoDetalleDto>> Crear(CrearInstanciaDocumentoDto dto, CancellationToken ct) =>
        Ok(await instanciaService.CrearAsync(dto, UsuarioActualId, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InstanciaDocumentoDetalleDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await instanciaService.ObtenerAsync(id, ct));

    [HttpGet("pendientes")]
    public async Task<ActionResult<IReadOnlyList<InstanciaDocumentoResumenDto>>> Pendientes(CancellationToken ct) =>
        Ok(await instanciaService.ListarPendientesAsync(UsuarioActualId, ct));

    [HttpGet("mios")]
    public async Task<ActionResult<IReadOnlyList<InstanciaDocumentoResumenDto>>> Mios(CancellationToken ct) =>
        Ok(await instanciaService.ListarMisDocumentosAsync(UsuarioActualId, ct));

    [HttpPost("{id:int}/acciones")]
    public async Task<ActionResult<InstanciaDocumentoDetalleDto>> EjecutarAccion(int id, EjecutarAccionDto dto, CancellationToken ct) =>
        Ok(await instanciaService.EjecutarAccionAsync(id, UsuarioActualId, dto, ct));

    [HttpPost("{id:int}/reenviar")]
    public async Task<ActionResult<InstanciaDocumentoDetalleDto>> Reenviar(int id, CancellationToken ct) =>
        Ok(await instanciaService.ReenviarAsync(id, UsuarioActualId, ct));

    [HttpPost("{id:int}/adjuntos")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<AdjuntoDto>> SubirAdjunto(int id, IFormFile archivo, CancellationToken ct)
    {
        if (archivo.Length == 0)
            return BadRequest(new { mensaje = "El archivo está vacío." });

        await using var stream = archivo.OpenReadStream();
        var resultado = await instanciaService.AgregarAdjuntoAsync(id, archivo.FileName, archivo.ContentType, stream, UsuarioActualId, ct);
        return Ok(resultado);
    }

    [HttpGet("adjuntos/{adjuntoId:int}")]
    public async Task<IActionResult> DescargarAdjunto(int adjuntoId, CancellationToken ct)
    {
        var (contenido, nombreArchivo, contentType) = await instanciaService.DescargarAdjuntoAsync(adjuntoId, ct);
        return File(contenido, contentType ?? "application/octet-stream", nombreArchivo);
    }
}
