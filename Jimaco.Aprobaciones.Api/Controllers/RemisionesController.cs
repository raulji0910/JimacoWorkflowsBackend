using System.Security.Claims;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jimaco.Aprobaciones.Api.Controllers;

/// <summary>Entregas (2026-11-12) — dashboard de Remisiones para Logística. Permisos en IRemisionService.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RemisionesController(IRemisionService remisionService) : ControllerBase
{
    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ---- Configuración (2026-11-12): interruptor de la revisión con IA — solo administradores. ----

    [HttpGet("configuracion")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ConfiguracionEntregasDto>> Configuracion(CancellationToken ct) =>
        Ok(await remisionService.ObtenerConfiguracionAsync(ct));

    [HttpPut("configuracion")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ConfiguracionEntregasDto>> ActualizarConfiguracion(ActualizarConfiguracionEntregasDto dto, CancellationToken ct) =>
        Ok(await remisionService.ActualizarConfiguracionAsync(dto, UsuarioActualId, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RemisionTableroDto>>> Listar(CancellationToken ct) =>
        Ok(await remisionService.ListarAsync(UsuarioActualId, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RemisionDetalleDto>> Obtener(int id, CancellationToken ct) =>
        Ok(await remisionService.ObtenerAsync(id, UsuarioActualId, ct));

    /// <summary>PDF de una o varias (ids=1,2,3) — inline, para que el navegador lo abra y se imprima.</summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> Pdf([FromQuery] string ids, CancellationToken ct)
    {
        var lista = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToList();
        var pdf = await remisionService.GenerarPdfAsync(lista, UsuarioActualId, ct);
        var nombre = lista.Count == 1 ? $"Remision {lista[0]}.pdf" : $"Remisiones ({lista.Count}).pdf";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = nombre }.ToString();
        return File(pdf, "application/pdf");
    }

    /// <summary>
    /// Confirmar la entrega con uno o varios archivos (foto o PDF escaneado). <c>qrLeidos</c> va
    /// alineado con <c>archivos</c>: el texto del QR que el navegador encontró en cada uno ("" si ninguno).
    /// Sin el CancellationToken de la petición a propósito (como las cantidades de HA): si el celular
    /// suspende el navegador a mitad de la subida, lo que ya llegó se guarda.
    /// </summary>
    [HttpPost("{id:int}/confirmar-entrega")]
    [RequestSizeLimit(60_000_000)]
    public async Task<ActionResult<RemisionDetalleDto>> ConfirmarEntrega(int id, [FromForm] List<IFormFile> archivos,
        [FromForm] List<string>? qrLeidos, [FromForm] string? observacion)
    {
        var validos = archivos.Where(a => a.Length > 0).ToList();
        var lista = new List<ArchivoSoporteEntrega>();
        try
        {
            for (var i = 0; i < validos.Count; i++)
            {
                var a = validos[i];
                var qr = qrLeidos is not null && i < qrLeidos.Count && !string.IsNullOrWhiteSpace(qrLeidos[i]) ? qrLeidos[i] : null;
                lista.Add(new ArchivoSoporteEntrega(a.FileName, a.ContentType, a.OpenReadStream(), a.Length, qr));
            }
            return Ok(await remisionService.ConfirmarEntregaAsync(id, UsuarioActualId, observacion, lista, CancellationToken.None));
        }
        finally
        {
            foreach (var a in lista) await a.Contenido.DisposeAsync();
        }
    }

    /// <summary>Revisa un archivo antes de confirmar (QR + IA) — no guarda nada.</summary>
    [HttpPost("{id:int}/verificar-soporte")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<VerificacionSoporteDto>> VerificarSoporte(int id, [FromForm] IFormFile archivo,
        [FromForm] string? qrLeido, CancellationToken ct)
    {
        await using var contenido = archivo.OpenReadStream();
        return Ok(await remisionService.VerificarSoporteAsync(id, UsuarioActualId,
            new ArchivoSoporteEntrega(archivo.FileName, archivo.ContentType, contenido, archivo.Length,
                string.IsNullOrWhiteSpace(qrLeido) ? null : qrLeido), ct));
    }

    // ---- Facturación (fase 2, 2026-11-13) ----

    /// <summary>El emisor adjunta UN archivo de un tipo documental (se revisa con IA si está encendida).</summary>
    [HttpPost("{id:int}/documentos")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<RemisionDetalleDto>> AdjuntarDocumento(int id, [FromForm] int tipoDocumentoId, [FromForm] IFormFile archivo)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "Escogé un archivo." });
        await using var contenido = archivo.OpenReadStream();
        // Sin el token de la petición (como confirmar entrega): lo que ya subió se guarda.
        return Ok(await remisionService.AdjuntarDocumentoAsync(id, UsuarioActualId, tipoDocumentoId,
            new ArchivoSoporteEntrega(archivo.FileName, archivo.ContentType, contenido, archivo.Length, null), CancellationToken.None));
    }

    [HttpDelete("{id:int}/documentos/{soporteId:int}")]
    public async Task<ActionResult<RemisionDetalleDto>> EliminarDocumento(int id, int soporteId, CancellationToken ct) =>
        Ok(await remisionService.EliminarDocumentoAsync(id, soporteId, UsuarioActualId, ct));

    [HttpPost("{id:int}/enviar-a-facturacion")]
    public async Task<ActionResult<RemisionDetalleDto>> EnviarAFacturacion(int id, EnviarAFacturacionDto dto) =>
        Ok(await remisionService.EnviarAFacturacionAsync(id, UsuarioActualId, dto.Comentario, CancellationToken.None));

    [HttpPost("{id:int}/devolver")]
    public async Task<ActionResult<RemisionDetalleDto>> Devolver(int id, DevolverRemisionDto dto) =>
        Ok(await remisionService.DevolverAsync(id, UsuarioActualId, dto.Comentario, CancellationToken.None));

    [HttpGet("tipos-documento")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<TipoDocumentoFacturacionDto>>> TiposDocumento(CancellationToken ct) =>
        Ok(await remisionService.ListarTiposDocumentoAsync(ct));

    [HttpPost("tipos-documento")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TipoDocumentoFacturacionDto>> CrearTipoDocumento(GuardarTipoDocumentoFacturacionDto dto, CancellationToken ct) =>
        Ok(await remisionService.GuardarTipoDocumentoAsync(null, dto, ct));

    [HttpPut("tipos-documento/{tipoId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<TipoDocumentoFacturacionDto>> ActualizarTipoDocumento(int tipoId, GuardarTipoDocumentoFacturacionDto dto, CancellationToken ct) =>
        Ok(await remisionService.GuardarTipoDocumentoAsync(tipoId, dto, ct));

    [HttpGet("soportes/{soporteId:int}")]
    public async Task<IActionResult> DescargarSoporte(int soporteId, CancellationToken ct)
    {
        var (contenido, nombre, contentType) = await remisionService.DescargarSoporteAsync(soporteId, UsuarioActualId, ct);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileNameStar = nombre }.ToString();
        return File(contenido, contentType ?? "application/octet-stream");
    }
}
