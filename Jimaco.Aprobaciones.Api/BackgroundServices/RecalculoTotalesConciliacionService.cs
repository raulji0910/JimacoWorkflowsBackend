using Jimaco.Aprobaciones.Negocio.Interfaces;

namespace Jimaco.Aprobaciones.Api.BackgroundServices;

/// <summary>
/// Al arrancar la Api (2026-11-24): si cambió la forma de leer los XML de las facturas de proveedor,
/// relee los XML guardados y corrige sus totales — una sola vez por versión (ver
/// ConciliacionService.VersionLecturaXml). Corre en segundo plano para no demorar el arranque; si
/// falla, se reintenta en el siguiente arranque.
/// </summary>
public class RecalculoTotalesConciliacionService(IServiceScopeFactory scopeFactory, ILogger<RecalculoTotalesConciliacionService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var servicio = scope.ServiceProvider.GetRequiredService<IConciliacionService>();
            var cambiadas = await servicio.RecalcularTotalesSiHaceFaltaAsync(stoppingToken);
            if (cambiadas is not null)
                logger.LogInformation("Conciliación: totales de las facturas releídos del XML, {Cambiadas} corregidas.", cambiadas);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falló el recálculo de totales de la conciliación; se reintenta en el próximo arranque.");
        }
    }
}
