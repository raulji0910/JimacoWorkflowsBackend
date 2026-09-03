using Jimaco.Aprobaciones.Negocio.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Envía correo directo contra el buzón real de la empresa (ej. sistemas@jimaco.com.co) vía SMTP,
/// no un proveedor transaccional externo. Configuración en <c>Smtp:*</c> (ver appsettings.json /
/// variables de entorno del contenedor) — <c>Smtp:UseSsl = true</c> asume conexión SSL directa
/// (típicamente puerto 465, el patrón más común en hosting compartido tipo cPanel); en <c>false</c>
/// asume STARTTLS (típicamente puerto 587).
/// </summary>
public class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task EnviarAsync(string destinatario, string asunto, string cuerpoHtml, CancellationToken ct = default)
    {
        var host = configuration["Smtp:Host"] ?? throw new InvalidOperationException("Falta configurar Smtp:Host.");
        var puerto = int.TryParse(configuration["Smtp:Port"], out var p) ? p : 587;
        var usarSsl = bool.TryParse(configuration["Smtp:UseSsl"], out var ssl) && ssl;
        var usuario = configuration["Smtp:Usuario"] ?? throw new InvalidOperationException("Falta configurar Smtp:Usuario.");
        var password = configuration["Smtp:Password"] ?? throw new InvalidOperationException("Falta configurar Smtp:Password.");
        var remitenteNombre = configuration["Smtp:RemitenteNombre"] ?? "Jimaco Aprobaciones";
        var remitenteEmail = configuration["Smtp:RemitenteEmail"] ?? usuario;

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(remitenteNombre, remitenteEmail));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));
        mensaje.Subject = asunto;
        mensaje.Body = new BodyBuilder { HtmlBody = cuerpoHtml }.ToMessageBody();

        using var cliente = new SmtpClient();
        var modoSeguridad = usarSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        await cliente.ConnectAsync(host, puerto, modoSeguridad, ct);
        await cliente.AuthenticateAsync(usuario, password, ct);
        await cliente.SendAsync(mensaje, ct);
        await cliente.DisconnectAsync(true, ct);
    }
}
