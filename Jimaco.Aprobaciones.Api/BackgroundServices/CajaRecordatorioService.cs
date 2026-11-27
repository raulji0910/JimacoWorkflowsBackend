using Jimaco.Aprobaciones.Negocio.Interfaces;

namespace Jimaco.Aprobaciones.Api.BackgroundServices;

/// <summary>
/// Cajas menores (2026-11-27): cada 15 minutos revisa giros sin legalizar y vueltas sin entregar con
/// más de 24 horas, y les manda a los responsables de la caja UN correo por caja con todos los
/// pendientes. Cada giro se vuelve a recordar cada 24 horas hasta que se cierre.
/// </summary>
public class CajaRecordatorioService(IServiceScopeFactory scopeFactory, ILogger<CajaRecordatorioService> logger) : BackgroundService
{
    private static readonly TimeSpan IntervaloChequeo = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(IntervaloChequeo);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var recordados = await scope.ServiceProvider.GetRequiredService<ICajaService>().EnviarRecordatoriosAsync(stoppingToken);
                if (recordados > 0)
                    logger.LogInformation("Cajas: recordatorio de {Cantidad} giro(s) pendiente(s).", recordados);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Falló el recordatorio de giros de caja.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
