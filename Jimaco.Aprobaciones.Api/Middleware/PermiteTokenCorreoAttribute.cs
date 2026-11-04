namespace Jimaco.Aprobaciones.Api.Middleware;

/// <summary>
/// Marca una acción como alcanzable con el token acotado de "aprobar/rechazar desde el correo"
/// (ver <see cref="Negocio.Interfaces.IJwtGenerador.GenerarTokenAccionCorreo"/>). Solo las acciones
/// marcadas aceptan ese token — todo lo demás lo rechaza <see cref="TokenCorreoActionFilter"/>, aunque
/// la firma/expiración del token sean válidas, para que un correo filtrado no sirva de llave general.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class PermiteTokenCorreoAttribute : Attribute
{
    /// <summary>
    /// true en las acciones que CAMBIAN el documento (ejecutar acción) — ahí <see
    /// cref="TokenCorreoActionFilter"/> exige además que el claim "paso" del token coincida con el
    /// paso ACTUAL del documento, para que un link de correo viejo (la persona ya actuó, o el
    /// documento avanzó por otro medio) no pueda reusarse. En las de solo lectura (ver el documento,
    /// el PDF) queda en false a propósito — la persona tiene que poder seguir viendo el documento
    /// aunque el link ya haya quedado obsoleto para actuar.
    /// </summary>
    public bool RequierePasoVigente { get; init; }
}
