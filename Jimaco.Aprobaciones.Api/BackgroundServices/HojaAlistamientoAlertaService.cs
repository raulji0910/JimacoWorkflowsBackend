using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Api.BackgroundServices;

/// <summary>
/// Chequeo periódico (2026-09-28) — primera pieza de este tipo en el sistema: todo lo demás dispara
/// notificaciones por una acción explícita de un usuario, esto revisa el tiempo transcurrido. Busca
/// Hojas de Alistamiento incompletas que ya cruzaron el umbral configurado
/// (<c>Alistamiento:HorasUmbralAlerta</c>, default 2 horas) desde que se generaron, y avisa. Mientras
/// siga incompleta, repite el aviso cada <c>Alistamiento:HorasEntreRecordatorios</c> (default 24),
/// solo hasta que la hoja cumple <c>Alistamiento:DiasMaximoRecordatorios</c> (default 7) — pedido
/// del usuario 2026-11-04. Historia: hasta 2026-10-14 se repetía cada 15 minutos (una hoja llegó a
/// 333 correos y trajo problemas con el servidor de correo); luego pasó a una sola vez; el tope de
/// días evita que las hojas abandonadas manden correo todos los días para siempre.
/// </summary>
public class HojaAlistamientoAlertaService(IServiceScopeFactory scopeFactory, IConfiguration configuration, TimeProvider timeProvider, ILogger<HojaAlistamientoAlertaService> logger)
    : BackgroundService
{
    private double HorasUmbral => double.TryParse(configuration["Alistamiento:HorasUmbralAlerta"], out var h) ? h : 2;
    private double HorasEntreRecordatorios => double.TryParse(configuration["Alistamiento:HorasEntreRecordatorios"], out var h) ? h : 24;
    private double DiasMaximoRecordatorios => double.TryParse(configuration["Alistamiento:DiasMaximoRecordatorios"], out var d) ? d : 7;
    private TimeSpan IntervaloChequeo => TimeSpan.FromMinutes(
        double.TryParse(configuration["Alistamiento:MinutosEntreChequeos"], out var m) ? m : 15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(IntervaloChequeo);
        do
        {
            try
            {
                await RevisarAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Un fallo acá (ej. la BD momentáneamente no responde) no debe tumbar el servicio —
                // se reintenta solo en la próxima corrida.
                logger.LogWarning(ex, "Falló el chequeo de alertas de alistamiento.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RevisarAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificacionService = scope.ServiceProvider.GetRequiredService<INotificacionService>();

        var ahora = timeProvider.GetUtcNow().UtcDateTime;
        var horasUmbral = HorasUmbral;

        // Igual criterio que HojaAlistamientoService: no avisar de una hoja que Transporte todavía
        // ni siquiera puede ver (la OC no pasó la aprobación comercial) — salvo que no tenga OC
        // asociada (2026-10-21), ahí no hay aprobación que esperar.
        var incompletas = await db.DocumentosVinculados
            .Include(d => d.Renglones)
            .Where(d => d.Renglones.Any(r => r.CantidadDisponible < r.Cantidad))
            .Where(d => d.InstanciaDocumentoId == null || db.HistorialAcciones.Any(h =>
                h.InstanciaDocumentoId == d.InstanciaDocumentoId &&
                ((h.Accion == TipoAccion.Aprobado && h.PasoFlujo!.Orden == 1) || h.Accion == TipoAccion.AprobadoAutomatico)))
            .ToListAsync(ct);

        var huboAvisos = false;
        // La misma Cotización real de WO puede estar ligada a más de una OC (una copia de
        // DocumentoVinculado por cada una) — para este control es una sola hoja, así que se manda
        // UN solo aviso por grupo, no uno por cada OC que la comparte (pedido explícito del usuario,
        // 2026-09-29). Ver el mismo criterio de agrupación en HojaAlistamientoService.
        foreach (var grupo in incompletas.GroupBy(d => (d.Empresa, d.PrefijoOrigen, d.NumeroReferencia)))
        {
            var representante = grupo.OrderBy(d => d.Id).First();
            var antiguedad = ahora - representante.FechaCreacion;
            if (antiguedad.TotalHours < horasUmbral) continue;
            if (representante.UltimaAlertaEnviada is { } ultima)
            {
                if ((ahora - ultima).TotalHours < HorasEntreRecordatorios) continue;
                if (antiguedad.TotalDays > DiasMaximoRecordatorios) continue;
            }

            await notificacionService.NotificarHojaAlistamientoAtrasadaAsync(representante.Id, ct);
            foreach (var copia in grupo) copia.UltimaAlertaEnviada = ahora;
            huboAvisos = true;
        }

        if (huboAvisos) await db.SaveChangesAsync(ct);
    }
}
