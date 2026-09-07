namespace Jimaco.Aprobaciones.Api.Middleware;

/// <summary>
/// Marca una acción como alcanzable con el token acotado de "aprobar/rechazar desde el correo"
/// (ver <see cref="Negocio.Interfaces.IJwtGenerador.GenerarTokenAccionCorreo"/>). Solo las acciones
/// marcadas aceptan ese token — todo lo demás lo rechaza <see cref="TokenCorreoActionFilter"/>, aunque
/// la firma/expiración del token sean válidas, para que un correo filtrado no sirva de llave general.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class PermiteTokenCorreoAttribute : Attribute;
