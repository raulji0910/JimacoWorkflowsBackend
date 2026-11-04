namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Un archivo real pegado al correo (2026-09-29, ej. el soporte de pago que Gerencia General adjunta al aprobar).</summary>
public record AdjuntoCorreo(string NombreArchivo, byte[] Contenido, string? ContentType);

/// <summary>Abstrae el envío de correo, para no acoplar el motor de notificaciones a un proveedor concreto.</summary>
public interface IEmailSender
{
    Task EnviarAsync(string destinatario, string asunto, string cuerpoHtml, IReadOnlyList<AdjuntoCorreo>? adjuntos = null, CancellationToken ct = default);
}
