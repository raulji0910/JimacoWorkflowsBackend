using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TiposDocumentoController(ITipoDocumentoService tipoDocumentoService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TipoDocumentoDto>>> Listar(CancellationToken ct) =>
        Ok(await tipoDocumentoService.ListarAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TipoDocumentoDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await tipoDocumentoService.ObtenerAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TipoDocumentoDto>> Crear(CrearTipoDocumentoDto dto, CancellationToken ct) =>
        Ok(await tipoDocumentoService.CrearAsync(dto, ct));

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TipoDocumentoDto>> Actualizar(int id, ActualizarTipoDocumentoDto dto, CancellationToken ct) =>
        Ok(await tipoDocumentoService.ActualizarAsync(id, dto, ct));
}
