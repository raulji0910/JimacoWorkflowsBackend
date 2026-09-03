namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Abstrae el envío de correo, para no acoplar el motor de notificaciones a un proveedor concreto.</summary>
public interface IEmailSender
{
    Task EnviarAsync(string destinatario, string asunto, string cuerpoHtml, CancellationToken ct = default);
}
