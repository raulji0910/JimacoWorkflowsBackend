using System.Security.Claims;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

/// <summary>
/// Pantalla de Conciliación (2026-11-24): facturas de proveedor (XML DIAN) vs OC de World Office.
/// Todo exige <c>Rol.PuedeVerConciliacion</c> (lo valida el servicio). Lo que sube el Sincronizador
/// va por <see cref="SincronizacionController"/> (solo el Sincronizador de Jimaco).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConciliacionController(IConciliacionService conciliacionService, TimeProvider reloj) : ControllerBase
{
    private const int MaximoCargaBytes = 60_000_000;
    private const int MaximoArchivosPorCarga = 500;

    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<ConciliacionDto>> Conciliar([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
    {
        var hastaReal = hasta ?? ConciliacionService.HoyColombia(reloj);
        var desdeReal = desde ?? hastaReal.AddDays(-30);
        if (desdeReal > hastaReal)
            return BadRequest(new { mensaje = "La fecha 'desde' no puede ser posterior a 'hasta'." });
        if (hastaReal.DayNumber - desdeReal.DayNumber > 366)
            return BadRequest(new { mensaje = "El rango máximo es de un año." });

        return Ok(await conciliacionService.ConciliarAsync(desdeReal, hastaReal, UsuarioActualId, ct));
    }

    [HttpGet("facturas/{id:int}")]
    public async Task<ActionResult<DetalleFacturaConciliadaDto>> Detalle(int id, CancellationToken ct)
    {
        var detalle = await conciliacionService.ObtenerDetalleAsync(id, UsuarioActualId, ct);
        return detalle is null ? NotFound(new { mensaje = "Factura no encontrada." }) : Ok(detalle);
    }

    /// <summary>XML de una factura, tal cual llegó.</summary>
    [HttpGet("facturas/{id:int}/xml")]
    public async Task<IActionResult> DescargarXml(int id, CancellationToken ct)
        => await Descargar([id], ct);

    /// <summary>XML de varias facturas elegidas en la tabla, en un ZIP (PROVEEDOR_NUMERO.xml).</summary>
    [HttpPost("facturas/xml")]
    public async Task<IActionResult> DescargarXmls(DescargarXmlDto dto, CancellationToken ct)
        => await Descargar(dto.FacturaIds ?? [], ct);

    private async Task<IActionResult> Descargar(IReadOnlyList<int> ids, CancellationToken ct)
    {
        try
        {
            var archivo = await conciliacionService.DescargarXmlAsync(ids, UsuarioActualId, ct);
            return File(archivo.Contenido, archivo.ContentType, archivo.NombreArchivo);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpGet("facturas/{id:int}/observaciones")]
    public async Task<ActionResult<ObservacionesFacturaDto>> ObservacionesFactura(int id, CancellationToken ct)
    {
        var observaciones = await conciliacionService.ObtenerObservacionesFacturaAsync(id, UsuarioActualId, ct);
        return observaciones is null ? NotFound(new { mensaje = "Factura no encontrada." }) : Ok(observaciones);
    }

    [HttpPost("facturas/{id:int}/observaciones")]
    public async Task<ActionResult<ObservacionesFacturaDto>> AgregarObservacionFactura(int id, NuevaObservacionFacturaDto dto, CancellationToken ct)
    {
        try
        {
            var observaciones = await conciliacionService.AgregarObservacionFacturaAsync(id, dto, UsuarioActualId, ct);
            return observaciones is null ? NotFound(new { mensaje = "Factura no encontrada." }) : Ok(observaciones);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Historial de revisión de una OC sin factura (por IdAsientoContable de WO).</summary>
    [HttpGet("ordenes/{idAsientoContable:int}/observaciones")]
    public async Task<ActionResult<ObservacionesOrdenDto>> ObservacionesOrden(int idAsientoContable, CancellationToken ct)
    {
        var observaciones = await conciliacionService.ObtenerObservacionesOrdenAsync(idAsientoContable, UsuarioActualId, ct);
        return observaciones is null ? NotFound(new { mensaje = "OC no encontrada." }) : Ok(observaciones);
    }

    [HttpPost("ordenes/{idAsientoContable:int}/observaciones")]
    public async Task<ActionResult<ObservacionesOrdenDto>> AgregarObservacionOrden(int idAsientoContable, NuevaObservacionOrdenDto dto, CancellationToken ct)
    {
        try
        {
            var observaciones = await conciliacionService.AgregarObservacionOrdenAsync(idAsientoContable, dto, UsuarioActualId, ct);
            return observaciones is null ? NotFound(new { mensaje = "OC no encontrada." }) : Ok(observaciones);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// ZIP/XML descargados de la DIAN, varios a la vez. La DIAN tiene todas las facturas: esto completa
    /// las que el proveedor no mandó a facturacion@jimaco.com.co.
    /// </summary>
    [HttpPost("cargar-archivos")]
    [RequestSizeLimit(MaximoCargaBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximoCargaBytes)]
    public async Task<ActionResult<CargaArchivosResultadoDto>> CargarArchivos([FromForm] List<IFormFile> archivos, CancellationToken ct)
    {
        if (archivos.Count == 0)
            return BadRequest(new { mensaje = "Seleccioná al menos un archivo .zip o .xml." });
        if (archivos.Count > MaximoArchivosPorCarga)
            return BadRequest(new { mensaje = $"Máximo {MaximoArchivosPorCarga} archivos por carga." });

        var abiertos = new List<Stream>();
        try
        {
            var cargados = new List<ArchivoCargado>();
            foreach (var archivo in archivos)
            {
                var contenido = archivo.OpenReadStream();
                abiertos.Add(contenido);
                cargados.Add(new ArchivoCargado(Path.GetFileName(archivo.FileName), contenido));
            }
            return Ok(await conciliacionService.CargarArchivosAsync(cargados, UsuarioActualId, ct));
        }
        finally
        {
            foreach (var s in abiertos)
                await s.DisposeAsync();
        }
    }
}

public record DescargarXmlDto(IReadOnlyList<int>? FacturaIds);
