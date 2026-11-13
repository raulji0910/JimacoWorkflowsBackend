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
    /// <summary>
    /// La página pide <c>?empresa=todas</c>. Un Sincronizador manda el encabezado <c>X-Empresa</c> y
    /// recibe solo los tipos de su empresa; SIN parámetro ni encabezado = solo Jimaco, para que el
    /// Sincronizador de Jimaco ya instalado (que arma un mapa por prefijo) no vea dos tipos "OC".
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TipoDocumentoDto>>> Listar([FromQuery] string? empresa, CancellationToken ct)
    {
        if (string.Equals(empresa, "todas", StringComparison.OrdinalIgnoreCase))
            return Ok(await tipoDocumentoService.ListarAsync(ct));
        var pedida = empresa ?? Request.Headers["X-Empresa"].FirstOrDefault();
        return Ok(await tipoDocumentoService.ListarAsync(ct, Jimaco.Aprobaciones.Modelo.Entidades.EmpresaExtensions.Parsear(pedida)));
    }

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
