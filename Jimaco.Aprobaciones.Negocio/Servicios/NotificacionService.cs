using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Arma y despacha las notificaciones. Por ahora solo el canal Email está realmente conectado
/// a un proveedor (<see cref="IEmailSender"/>); WhatsApp queda pendiente (ver CLAUDE.md) — igual
/// se deja creada la fila en <see cref="Notificacion"/> por completitud del historial, pero queda
/// en estado Pendiente sin que nada la despache.
/// </summary>
public class NotificacionService(
    AppDbContext db,
    IEmailSender emailSender,
    IJwtGenerador jwtGenerador,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<NotificacionService> logger)
    : INotificacionService
{
    public async Task NotificarPasoAsync(int instanciaDocumentoId, int pasoFlujoId, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct);
        if (instancia is null) return;

        var paso = await db.PasosFlujo.FirstOrDefaultAsync(p => p.Id == pasoFlujoId, ct);
        if (paso is null) return;

        var destinatarios = await db.UsuarioRoles
            .Where(ur => db.PasoFlujoRoles.Any(pr => pr.PasoFlujoId == pasoFlujoId && pr.RolId == ur.RolId))
            .Select(ur => ur.Usuario)
            .Where(u => u.Activo)
            .Distinct()
            .ToListAsync(ct);

        var frontendBaseUrl = (configuration["App:FrontendBaseUrl"] ?? "").TrimEnd('/');
        var vigenciaDias = int.TryParse(configuration["Jwt:VigenciaAccionCorreoDias"], out var d) ? d : 7;

        var asunto = $"[Jimaco Aprobaciones] {instancia.TipoDocumento.Nombre} {instancia.NumeroReferencia} pendiente de tu aprobación";

        foreach (var usuario in destinatarios)
        {
            // Token acotado a este usuario + este documento puntual (ver GenerarTokenAccionCorreo) —
            // permite ver el PDF y aprobar/rechazar con comentario directamente desde el correo, sin
            // loguearse, pero no sirve para nada más si el correo se reenvía o se filtra.
            var token = jwtGenerador.GenerarTokenAccionCorreo(usuario.Id, instancia.Id, TimeSpan.FromDays(vigenciaDias));
            var linkAccion = $"{frontendBaseUrl}/accion-correo/{instancia.Id}?token={Uri.EscapeDataString(token)}";

            var cuerpo = $"""
                <p>Tenés un documento pendiente en el paso <strong>{paso.Nombre}</strong>:</p>
                <ul>
                  <li>Tipo: {instancia.TipoDocumento.Nombre}</li>
                  <li>Referencia: {instancia.NumeroReferencia}</li>
                  <li>Proveedor: {instancia.Proveedor}</li>
                  <li>Valor: {instancia.Valor}</li>
                </ul>
                <p><a href="{linkAccion}">Ver el documento y aprobar/rechazar</a></p>
                <p style="color:#666;font-size:12px">Este link es personal, vence en {vigenciaDias} días y no
                requiere iniciar sesión. Si preferís, también podés entrar al sistema normalmente.</p>
                """;

            await CrearYEnviarAsync(instancia.Id, usuario, asunto, cuerpo, ct);
        }
    }

    public async Task NotificarUsuarioAsync(int instanciaDocumentoId, int usuarioId, string asunto, string mensaje, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId && u.Activo, ct);
        if (usuario is null) return;

        await CrearYEnviarAsync(instanciaDocumentoId, usuario, asunto, $"<p>{mensaje}</p>", ct);
    }

    public async Task EnviarPruebaAsync(string destinatarioEmail, CancellationToken ct = default)
    {
        await emailSender.EnviarAsync(
            destinatarioEmail,
            "[Jimaco Aprobaciones] Correo de prueba",
            "<p>Si ves este correo, la configuración SMTP de Jimaco Aprobaciones funciona correctamente.</p>",
            ct);
    }

    private async Task CrearYEnviarAsync(int instanciaDocumentoId, Usuario usuario, string asunto, string cuerpoHtml, CancellationToken ct)
    {
        var notificacion = new Notificacion
        {
            InstanciaDocumentoId = instanciaDocumentoId,
            UsuarioId = usuario.Id,
            Canal = CanalNotificacion.Email,
            Mensaje = asunto,
            Estado = EstadoNotificacion.Pendiente,
            FechaCreacion = timeProvider.GetUtcNow().UtcDateTime
        };
        db.Notificaciones.Add(notificacion);
        await db.SaveChangesAsync(ct);

        try
        {
            await emailSender.EnviarAsync(usuario.Email, asunto, cuerpoHtml, ct);
            notificacion.Estado = EstadoNotificacion.Enviada;
        }
        catch (Exception ex)
        {
            // Un fallo de envío no debe tumbar la acción de negocio que lo disparó (aprobar,
            // devolver, etc.) — se registra como Fallida y sigue.
            notificacion.Estado = EstadoNotificacion.Fallida;
            logger.LogWarning(ex, "No se pudo enviar la notificación por correo a {Email}", usuario.Email);
        }
        finally
        {
            notificacion.FechaEnvio = timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }
}
