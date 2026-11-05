using System.Security.Claims;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CuentasPorPagarController(ICuentasPorPagarService cuentasPorPagarService) : ControllerBase
{
    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<ResumenCuentasPorPagarDto>> Resumen(CancellationToken ct) =>
        Ok(await cuentasPorPagarService.ObtenerResumenAsync(UsuarioActualId, ct));

    [HttpGet("proveedores/{identificacion}")]
    public async Task<ActionResult<DetalleProveedorCuentaPorPagarDto>> Proveedor(string identificacion, CancellationToken ct) =>
        Ok(await cuentasPorPagarService.ObtenerProveedorAsync(identificacion, UsuarioActualId, ct));

    [HttpPost("pagos")]
    public async Task<ActionResult<int>> CrearPago(CrearPagoProveedorDto dto, CancellationToken ct) =>
        Ok(await cuentasPorPagarService.CrearPagoAsync(dto, UsuarioActualId, ct));
}
