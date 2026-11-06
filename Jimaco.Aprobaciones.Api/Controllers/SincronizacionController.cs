using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

/// <summary>
/// Endpoints que usa Jimaco.Aprobaciones.Sincronizador (no la UI) para reflejar en World Office la
/// aprobación del primer paso de un documento sincronizado — ver "Escritura de vuelta a World
/// Office" en CLAUDE.md. El Sincronizador ya se autentica como un Usuario normal (usuario de
/// servicio) vía /api/auth/login, así que estos endpoints solo requieren estar logueado, igual
/// que el resto de DocumentosController.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SincronizacionController(IInstanciaDocumentoService instanciaService, IHojaAlistamientoService hojaAlistamientoService,
    ICuentasPorPagarService cuentasPorPagarService) : ControllerBase
{
    [HttpGet("pendientes-wo")]
    public async Task<ActionResult<IReadOnlyList<PendienteEscrituraWODto>>> ListarPendientesWO(CancellationToken ct) =>
        Ok(await instanciaService.ListarPendientesEscrituraWOAsync(ct));

    [HttpPost("pendientes-wo/{id:int}/confirmar")]
    public async Task<IActionResult> ConfirmarWO(int id, CancellationToken ct)
    {
        await instanciaService.ConfirmarEscrituraWOAsync(id, ct);
        return Ok();
    }

    [HttpPost("pendientes-wo/{id:int}/conflicto")]
    public async Task<IActionResult> ReportarConflictoWO(int id, ReportarConflictoWODto dto, CancellationToken ct)
    {
        await instanciaService.ReportarConflictoWOAsync(id, dto.Mensaje, ct);
        return Ok();
    }

    // ---- Comprobante de Egreso (2026-09-22) — mismo patrón "pedir y recoger" de arriba. ----

    [HttpGet("pendientes-comprobante-egreso")]
    public async Task<ActionResult<IReadOnlyList<PendienteComprobanteEgresoDto>>> ListarPendientesComprobanteEgreso(CancellationToken ct) =>
        Ok(await instanciaService.ListarPendientesComprobanteEgresoAsync(ct));

    [HttpPost("pendientes-comprobante-egreso/{id:int}/encontrado")]
    public async Task<IActionResult> AdjuntarComprobanteEgreso(int id, ComprobanteEgresoEncontradoDto dto, CancellationToken ct)
    {
        await instanciaService.AdjuntarComprobanteEgresoAsync(id, dto, ct);
        return Ok();
    }

    [HttpPost("pendientes-comprobante-egreso/{id:int}/conflicto")]
    public async Task<IActionResult> ReportarConflictoComprobanteEgreso(int id, ReportarConflictoWODto dto, CancellationToken ct)
    {
        await instanciaService.ReportarConflictoComprobanteEgresoAsync(id, dto.Mensaje, ct);
        return Ok();
    }

    // ---- Remisionado (2026-10-09) — mismo patrón "pedir y recoger" de arriba. ----

    [HttpGet("pendientes-remision")]
    public async Task<ActionResult<IReadOnlyList<PendienteRemisionDto>>> ListarPendientesRemision(CancellationToken ct) =>
        Ok(await hojaAlistamientoService.ListarPendientesRemisionAsync(ct));

    [HttpPost("pendientes-remision/marcar")]
    public async Task<IActionResult> MarcarRemisionada(MarcarRemisionadaDto dto, CancellationToken ct)
    {
        await hojaAlistamientoService.MarcarRemisionadaAsync(dto, ct);
        return Ok();
    }

    // ---- Refrescar Hojas de Alistamiento activas contra WO (2026-10-14) ----

    [HttpGet("hojas-alistamiento/pendientes-refresco")]
    public async Task<ActionResult<IReadOnlyList<PendienteRefrescoDto>>> ListarPendientesRefresco(CancellationToken ct) =>
        Ok(await hojaAlistamientoService.ListarPendientesRefrescoAsync(ct));

    [HttpPost("hojas-alistamiento/refrescar")]
    public async Task<IActionResult> RefrescarHojaAlistamiento(DocumentoVinculadoInputDto dto, CancellationToken ct)
    {
        await hojaAlistamientoService.RefrescarDesdeWOAsync(dto, ct);
        return Ok();
    }

    // ---- Hojas de Alistamiento sin OC asociada (2026-10-21) ----

    [HttpPost("hojas-alistamiento/crear-sin-oc")]
    public async Task<IActionResult> CrearHojaAlistamientoSinOc(DocumentoVinculadoInputDto dto, CancellationToken ct)
    {
        await hojaAlistamientoService.CrearSinOcAsync(dto, ct);
        return Ok();
    }

    // ---- Cuentas por pagar (2026-11-05) — foto completa de WO, reemplaza la anterior. ----

    [HttpPost("cuentas-por-pagar")]
    public async Task<IActionResult> GuardarCuentasPorPagar(FotoCuentasPorPagarDto dto, CancellationToken ct)
    {
        await cuentasPorPagarService.GuardarFotoAsync(dto, ct);
        return Ok();
    }

    // ---- Creación del CE en WO para pagos a proveedor confirmados (2026-11-06). ----

    [HttpGet("pagos-proveedor/pendientes-ce")]
    public async Task<ActionResult<IReadOnlyList<PendienteCreacionCEDto>>> ListarPendientesCreacionCE(CancellationToken ct) =>
        Ok(await cuentasPorPagarService.ListarPendientesCreacionCEAsync(ct));

    [HttpPost("pagos-proveedor/{id:int}/ce-creado")]
    public async Task<IActionResult> ConfirmarCECreado(int id, CECreadoDto dto, CancellationToken ct)
    {
        await cuentasPorPagarService.ConfirmarCECreadoAsync(id, dto, ct);
        return Ok();
    }

    [HttpPost("pagos-proveedor/{id:int}/conflicto")]
    public async Task<IActionResult> ReportarConflictoCreacionCE(int id, ReportarConflictoWODto dto, CancellationToken ct)
    {
        await cuentasPorPagarService.ReportarConflictoCreacionCEAsync(id, dto.Mensaje, ct);
        return Ok();
    }
}
