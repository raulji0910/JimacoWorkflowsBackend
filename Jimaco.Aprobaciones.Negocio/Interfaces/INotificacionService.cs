namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface INotificacionService
{
    /// <summary>Notifica a todos los usuarios con un rol habilitado en ese paso que tienen un documento nuevo para revisar.</summary>
    Task NotificarPasoAsync(int instanciaDocumentoId, int pasoFlujoId, CancellationToken ct = default);

    /// <summary>Notifica a un usuario puntual (ej. al emisor cuando su documento fue devuelto/rechazado/completado).</summary>
    Task NotificarUsuarioAsync(int instanciaDocumentoId, int usuarioId, string asunto, string mensaje, CancellationToken ct = default);

    /// <summary>Envía un correo de prueba suelto, sin asociarlo a ningún documento — para validar que la configuración SMTP funciona.</summary>
    Task EnviarPruebaAsync(string destinatarioEmail, CancellationToken ct = default);
}
