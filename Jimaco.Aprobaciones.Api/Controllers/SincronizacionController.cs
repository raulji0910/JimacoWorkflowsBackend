using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

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
    ICuentasPorPagarService cuentasPorPagarService, IConfiguracionCuentasPorPagarService configuracionCuentasPorPagarService) : ControllerBase, IActionFilter
{
    /// <summary>
    /// Lo único que puede usar un Sincronizador que NO es el de Jimaco (2026-11-13): aprobaciones de OC
    /// y Hojas de Alistamiento. Cuentas por pagar, CE, terceros y Entregas son solo de Jimaco — si a la
    /// copia de Ferrealiados le quedara uno de esos módulos encendido por error, mezclaría datos de su
    /// World Office con los de Jimaco (p. ej. reemplazaría la foto de cuentas por pagar). Lista blanca:
    /// cualquier endpoint nuevo queda cerrado para otras empresas hasta que se agregue acá a propósito.
    /// </summary>
    private static readonly HashSet<string> PermitidosOtrasEmpresas =
    [
        nameof(ListarPendientesWO), nameof(ConfirmarWO), nameof(ReportarConflictoWO),
        nameof(ListarPendientesRemision), nameof(MarcarRemisionada),
        nameof(ListarPendientesRefresco), nameof(RefrescarHojaAlistamiento), nameof(CrearHojaAlistamientoSinOc)
    ];

    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (EmpresaSincronizador == Empresa.Jimaco) return;
        var accion = (context.ActionDescriptor as ControllerActionDescriptor)?.ActionName;
        if (accion is null || !PermitidosOtrasEmpresas.Contains(accion))
            context.Result = BadRequest(new { mensaje = $"El Sincronizador de {EmpresaSincronizador.Nombre()} no puede usar \"{accion}\": ese módulo es solo de Jimaco. Apagalo en su appsettings." });
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context) { }

    /// <summary>
    /// Empresa del Sincronizador que llama (2026-11-13): encabezado <c>X-Empresa</c>. Sin encabezado
    /// = Jimaco, así el Sincronizador de Jimaco que ya está instalado sigue funcionando sin cambios.
    /// Cada Sincronizador solo ve y toca lo de SU empresa (su World Office).
    /// </summary>
    private Empresa EmpresaSincronizador => EmpresaExtensions.Parsear(Request.Headers["X-Empresa"].FirstOrDefault());

    [HttpGet("pendientes-wo")]
    public async Task<ActionResult<IReadOnlyList<PendienteEscrituraWODto>>> ListarPendientesWO(CancellationToken ct) =>
        Ok(await instanciaService.ListarPendientesEscrituraWOAsync(EmpresaSincronizador, ct));

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
        Ok(await instanciaService.ListarPendientesComprobanteEgresoAsync(EmpresaSincronizador, ct));

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
        Ok(await hojaAlistamientoService.ListarPendientesRemisionAsync(ct, EmpresaSincronizador));

    [HttpPost("pendientes-remision/marcar")]
    public async Task<IActionResult> MarcarRemisionada(MarcarRemisionadaDto dto, CancellationToken ct)
    {
        await hojaAlistamientoService.MarcarRemisionadaAsync(dto, ct, EmpresaSincronizador);
        return Ok();
    }

    // ---- Refrescar Hojas de Alistamiento activas contra WO (2026-10-14) ----

    [HttpGet("hojas-alistamiento/pendientes-refresco")]
    public async Task<ActionResult<IReadOnlyList<PendienteRefrescoDto>>> ListarPendientesRefresco(CancellationToken ct) =>
        Ok(await hojaAlistamientoService.ListarPendientesRefrescoAsync(ct, EmpresaSincronizador));

    [HttpPost("hojas-alistamiento/refrescar")]
    public async Task<IActionResult> RefrescarHojaAlistamiento(DocumentoVinculadoInputDto dto, CancellationToken ct)
    {
        await hojaAlistamientoService.RefrescarDesdeWOAsync(dto, ct, EmpresaSincronizador);
        return Ok();
    }

    // ---- Hojas de Alistamiento sin OC asociada (2026-10-21) ----

    [HttpPost("hojas-alistamiento/crear-sin-oc")]
    public async Task<IActionResult> CrearHojaAlistamientoSinOc(DocumentoVinculadoInputDto dto, CancellationToken ct)
    {
        await hojaAlistamientoService.CrearSinOcAsync(dto, ct, EmpresaSincronizador);
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

    // Catálogo de terceros de WO (2026-11-09) — para el beneficiario de un pago extraordinario. El
    // Sincronizador pregunta cuándo fue la última vez y lo manda solo cada pocas horas.
    [HttpGet("terceros/estado")]
    public async Task<ActionResult<DateTime?>> EstadoTerceros(CancellationToken ct) =>
        Ok(await cuentasPorPagarService.ObtenerFechaCorteTercerosAsync(ct));

    [HttpPost("terceros")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> GuardarTerceros(IReadOnlyList<TerceroWOInputDto> dto, CancellationToken ct)
    {
        await cuentasPorPagarService.GuardarTercerosAsync(dto, ct);
        return Ok();
    }

    // Prefijo del CE y si se crean o no (2026-11-11) — lo maneja un administrador desde la página.
    [HttpGet("pagos-proveedor/configuracion-ce")]
    public async Task<ActionResult<ConfiguracionCuentasPorPagarDto>> ConfiguracionCE(CancellationToken ct) =>
        Ok(await configuracionCuentasPorPagarService.ObtenerAsync(ct));

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
