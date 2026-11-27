using System.Security.Claims;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

/// <summary>
/// Cajas menores de efectivo (2026-11-27). Los permisos (administrador / girador de cada caja) los
/// valida el servicio según los roles del usuario.
/// </summary>
[ApiController]
[Route("api/cajas")]
[Authorize]
public class CajasController(ICajaService cajaService) : ControllerBase
{
    private int UsuarioActualId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CajaResumenDto>>> Listar(CancellationToken ct) =>
        Ok(await cajaService.ListarAsync(UsuarioActualId, ct));

    [HttpGet("{cajaId:int}")]
    public async Task<ActionResult<CajaResumenDto>> Obtener(int cajaId, CancellationToken ct) =>
        Ok(await cajaService.ObtenerAsync(cajaId, UsuarioActualId, ct));

    [HttpPut("{cajaId:int}")]
    public async Task<ActionResult<CajaResumenDto>> Actualizar(int cajaId, ActualizarCajaDto dto, CancellationToken ct) =>
        Ok(await cajaService.ActualizarAsync(cajaId, dto, UsuarioActualId, ct));

    // ---- Pasos de OC que se pagan con la caja ----

    [HttpGet("{cajaId:int}/pasos-pago")]
    public async Task<ActionResult<IReadOnlyList<PasoPagoCajaDto>>> PasosPago(int cajaId, CancellationToken ct) =>
        Ok(await cajaService.ListarPasosPagoAsync(cajaId, UsuarioActualId, ct));

    [HttpPut("{cajaId:int}/pasos-pago/{pasoId:int}")]
    public async Task<ActionResult<PasoPagoCajaDto>> MarcarPasoPago(int cajaId, int pasoId, MarcarPasoPagoCajaDto dto, CancellationToken ct) =>
        Ok(await cajaService.MarcarPasoPagoAsync(cajaId, pasoId, dto, UsuarioActualId, ct));

    // ---- Conceptos ----

    [HttpGet("{cajaId:int}/conceptos")]
    public async Task<ActionResult<IReadOnlyList<ConceptoCajaDto>>> Conceptos(int cajaId, CancellationToken ct) =>
        Ok(await cajaService.ListarConceptosAsync(cajaId, UsuarioActualId, ct));

    [HttpPost("{cajaId:int}/conceptos")]
    public async Task<ActionResult<ConceptoCajaDto>> CrearConcepto(int cajaId, GuardarConceptoCajaDto dto, CancellationToken ct) =>
        Ok(await cajaService.CrearConceptoAsync(cajaId, dto, UsuarioActualId, ct));

    [HttpPut("{cajaId:int}/conceptos/{conceptoId:int}")]
    public async Task<ActionResult<ConceptoCajaDto>> ActualizarConcepto(int cajaId, int conceptoId, GuardarConceptoCajaDto dto, CancellationToken ct) =>
        Ok(await cajaService.ActualizarConceptoAsync(cajaId, conceptoId, dto, UsuarioActualId, ct));

    // ---- Personas (compartidas entre las dos cajas) ----

    [HttpGet("beneficiarios")]
    public async Task<ActionResult<IReadOnlyList<BeneficiarioCajaDto>>> BuscarBeneficiarios([FromQuery] string? texto, CancellationToken ct) =>
        Ok(await cajaService.BuscarBeneficiariosAsync(texto, UsuarioActualId, ct));

    [HttpPost("beneficiarios")]
    public async Task<ActionResult<BeneficiarioCajaDto>> CrearBeneficiario(GuardarBeneficiarioCajaDto dto, CancellationToken ct) =>
        Ok(await cajaService.CrearBeneficiarioAsync(dto, UsuarioActualId, ct));

    [HttpPut("beneficiarios/{beneficiarioId:int}")]
    public async Task<ActionResult<BeneficiarioCajaDto>> ActualizarBeneficiario(int beneficiarioId, GuardarBeneficiarioCajaDto dto, CancellationToken ct) =>
        Ok(await cajaService.ActualizarBeneficiarioAsync(beneficiarioId, dto, UsuarioActualId, ct));

    // ---- Movimientos ----

    [HttpPost("{cajaId:int}/recargas")]
    public async Task<ActionResult<MovimientoCajaDto>> Recargar(int cajaId, RegistrarRecargaDto dto, CancellationToken ct) =>
        Ok(await cajaService.RegistrarRecargaAsync(cajaId, dto, UsuarioActualId, ct));

    [HttpPost("{cajaId:int}/arqueos")]
    public async Task<ActionResult<ResultadoArqueoDto>> Arqueo(int cajaId, RegistrarArqueoDto dto, CancellationToken ct) =>
        Ok(await cajaService.RegistrarArqueoAsync(cajaId, dto, UsuarioActualId, ct));

    [HttpPost("{cajaId:int}/movimientos/{movimientoId:int}/anular")]
    public async Task<ActionResult<MovimientoCajaDto>> AnularMovimiento(int cajaId, int movimientoId, AnularDto dto, CancellationToken ct) =>
        Ok(await cajaService.AnularMovimientoAsync(cajaId, movimientoId, dto, UsuarioActualId, ct));

    [HttpGet("{cajaId:int}/movimientos")]
    public async Task<ActionResult<HistorialCajaDto>> Historial(int cajaId, [FromQuery] FiltroHistorialCajaDto filtro, CancellationToken ct) =>
        Ok(await cajaService.HistorialAsync(cajaId, filtro, UsuarioActualId, ct));

    // ---- Giros ----

    [HttpGet("{cajaId:int}/ordenes-pendientes")]
    public async Task<ActionResult<IReadOnlyList<OrdenCompraPendientePagoDto>>> OrdenesPendientes(int cajaId, CancellationToken ct) =>
        Ok(await cajaService.OrdenesCompraPendientesAsync(cajaId, UsuarioActualId, ct));

    [HttpGet("{cajaId:int}/giros")]
    public async Task<ActionResult<IReadOnlyList<GiroCajaResumenDto>>> Giros(int cajaId, [FromQuery] bool soloPendientes, CancellationToken ct) =>
        Ok(await cajaService.ListarGirosAsync(cajaId, soloPendientes, UsuarioActualId, ct));

    [HttpGet("{cajaId:int}/giros/{giroId:int}")]
    public async Task<ActionResult<GiroCajaDto>> Giro(int cajaId, int giroId, CancellationToken ct) =>
        Ok(await cajaService.ObtenerGiroAsync(cajaId, giroId, UsuarioActualId, ct));

    [HttpPost("{cajaId:int}/giros")]
    public async Task<ActionResult<GiroCajaDto>> CrearGiro(int cajaId, CrearGiroCajaDto dto, CancellationToken ct) =>
        Ok(await cajaService.CrearGiroAsync(cajaId, dto, UsuarioActualId, ct));

    [HttpPost("{cajaId:int}/giros/{giroId:int}/legalizar")]
    public async Task<ActionResult<GiroCajaDto>> Legalizar(int cajaId, int giroId, LegalizarGiroCajaDto dto, CancellationToken ct) =>
        Ok(await cajaService.LegalizarGiroAsync(cajaId, giroId, dto, UsuarioActualId, ct));

    [HttpPost("{cajaId:int}/giros/{giroId:int}/vueltas")]
    public async Task<ActionResult<GiroCajaDto>> RecibirVuelta(int cajaId, int giroId, RecibirVueltaDto dto, CancellationToken ct) =>
        Ok(await cajaService.RecibirVueltaAsync(cajaId, giroId, dto, UsuarioActualId, ct));

    [HttpPost("{cajaId:int}/giros/{giroId:int}/anular")]
    public async Task<ActionResult<GiroCajaDto>> AnularGiro(int cajaId, int giroId, AnularDto dto, CancellationToken ct) =>
        Ok(await cajaService.AnularGiroAsync(cajaId, giroId, dto, UsuarioActualId, ct));

    [HttpPost("{cajaId:int}/giros/{giroId:int}/soporte")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<GiroCajaDto>> SubirSoporte(int cajaId, int giroId, IFormFile archivo, CancellationToken ct)
    {
        await using var stream = archivo.OpenReadStream();
        return Ok(await cajaService.SubirSoporteAsync(cajaId, giroId, archivo.FileName, archivo.ContentType, stream, UsuarioActualId, ct));
    }

    [HttpGet("{cajaId:int}/giros/{giroId:int}/soporte")]
    public async Task<IActionResult> Soporte(int cajaId, int giroId, CancellationToken ct)
    {
        var (contenido, nombre, tipo) = await cajaService.AbrirSoporteAsync(cajaId, giroId, UsuarioActualId, ct);
        Response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition { FileName = nombre, Inline = true }.ToString();
        return File(contenido, tipo);
    }

    /// <summary>Comprobante del giro para que la persona firme al recibir (inline).</summary>
    [HttpGet("{cajaId:int}/giros/{giroId:int}/pdf")]
    public async Task<IActionResult> Pdf(int cajaId, int giroId, CancellationToken ct)
    {
        var (pdf, nombre) = await cajaService.GenerarPdfGiroAsync(cajaId, giroId, UsuarioActualId, ct);
        Response.Headers.ContentDisposition = new System.Net.Mime.ContentDisposition { FileName = nombre, Inline = true }.ToString();
        return File(pdf, "application/pdf");
    }
}
