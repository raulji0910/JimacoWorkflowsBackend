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

    [HttpGet("bancos")]
    public async Task<ActionResult<IReadOnlyList<CuentaBancoDto>>> Bancos(CancellationToken ct) =>
        Ok(await cuentasPorPagarService.ObtenerBancosAsync(ct));

    // ---- Relación de pagos (2026-11-09): reemplaza el viejo "POST pagos" (un correo por proveedor). ----

    [HttpGet("relacion")]
    public async Task<ActionResult<RelacionBorradorDto>> Relacion(CancellationToken ct) =>
        Ok(await cuentasPorPagarService.ObtenerBorradorAsync(UsuarioActualId, ct));

    [HttpPut("relacion/proveedores/{identificacion}")]
    public async Task<ActionResult<RelacionBorradorDto>> GuardarEnRelacion(string identificacion, GuardarBorradorProveedorDto dto, CancellationToken ct) =>
        Ok(await cuentasPorPagarService.GuardarBorradorProveedorAsync(identificacion, dto, UsuarioActualId, ct));

    [HttpPost("relacion/ratificar")]
    public async Task<ActionResult<RelacionCreadaDto>> Ratificar(RatificarRelacionDto dto) =>
        Ok(await cuentasPorPagarService.RatificarRelacionAsync(dto, UsuarioActualId, CancellationToken.None));

    [HttpGet("seguimiento")]
    public async Task<ActionResult<SeguimientoPaginaDto>> Seguimiento([FromQuery] FiltroPagosDto filtro, CancellationToken ct) =>
        Ok(await cuentasPorPagarService.ObtenerSeguimientoAsync(filtro, UsuarioActualId, ct));

    [HttpGet("seguimiento/{id:int}/lineas")]
    public async Task<ActionResult<IReadOnlyList<LineaOrdenPagoDto>>> LineasSeguimiento(int id, CancellationToken ct) =>
        Ok(await cuentasPorPagarService.ObtenerLineasSeguimientoAsync(id, UsuarioActualId, ct));

    // ---- Pago extraordinario ----

    [HttpGet("tipos-giro")]
    public async Task<ActionResult<IReadOnlyList<TipoGiroDto>>> TiposGiro(CancellationToken ct) =>
        Ok(await cuentasPorPagarService.ObtenerTiposGiroAsync(ct));

    [HttpGet("terceros")]
    public async Task<ActionResult<IReadOnlyList<TerceroWODto>>> Terceros([FromQuery] string q, CancellationToken ct) =>
        Ok(await cuentasPorPagarService.BuscarTercerosAsync(q, UsuarioActualId, ct));

    [HttpPost("extraordinarios")]
    public async Task<ActionResult<int>> CrearExtraordinario(CrearPagoExtraordinarioDto dto) =>
        Ok(await cuentasPorPagarService.CrearPagoExtraordinarioAsync(dto, UsuarioActualId, CancellationToken.None));
}
