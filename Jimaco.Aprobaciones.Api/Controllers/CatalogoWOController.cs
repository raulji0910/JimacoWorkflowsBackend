using System.Security.Claims;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

/// <summary>
/// Catálogo de World Office para armar documentos en el portal (2026-11-25): productos con
/// existencias, clientes, sucursales, bodegas, formas de pago y "Más Datos". Exige
/// <c>Rol.PuedeCrearCotizaciones</c> (lo valida el servicio). Lo sube el Sincronizador --documentos.
/// </summary>
[ApiController]
[Route("api/catalogo-wo")]
[Authorize]
public class CatalogoWOController(ICatalogoWOService catalogoWOService) : ControllerBase
{
    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("cotizacion")]
    public async Task<ActionResult<CatalogoCotizacionDto>> CatalogoCotizacion(CancellationToken ct) =>
        Ok(await catalogoWOService.ObtenerCatalogoCotizacionAsync(UsuarioActualId, ct));

    [HttpGet("productos")]
    public async Task<ActionResult<IReadOnlyList<ProductoCatalogoDto>>> BuscarProductos([FromQuery] string? buscar, CancellationToken ct) =>
        Ok(await catalogoWOService.BuscarProductosAsync(buscar ?? "", UsuarioActualId, ct));

    [HttpGet("clientes")]
    public async Task<ActionResult<IReadOnlyList<ClienteCatalogoDto>>> BuscarClientes([FromQuery] string? buscar, CancellationToken ct) =>
        Ok(await catalogoWOService.BuscarClientesAsync(buscar ?? "", UsuarioActualId, ct));

    [HttpGet("clientes/{idTercero:int}/direcciones")]
    public async Task<ActionResult<IReadOnlyList<DireccionClienteDto>>> Direcciones(int idTercero, CancellationToken ct) =>
        Ok(await catalogoWOService.ListarDireccionesAsync(idTercero, UsuarioActualId, ct));
}
