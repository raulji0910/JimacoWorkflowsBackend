using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jimaco.Aprobaciones.Api.Middleware;

/// <summary>
/// Filtro global (registrado en Program.cs) que acota lo que puede hacer el token de "acción desde
/// el correo": si el token trae el claim "purpose"="correo-accion", solo deja pasar a acciones
/// marcadas con <see cref="PermiteTokenCorreoAttribute"/>, y únicamente si el "doc" del token
/// coincide con el {id} de la ruta que se está pidiendo. Un token de sesión normal (login por
/// usuario/clave) no tiene ese claim y no se ve afectado por nada de esto.
/// </summary>
public class TokenCorreoActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var purpose = context.HttpContext.User.FindFirst("purpose")?.Value;
        if (purpose == "correo-accion")
        {
            var accionPermitida = context.ActionDescriptor is ControllerActionDescriptor descriptor
                && descriptor.MethodInfo.GetCustomAttributes(typeof(PermiteTokenCorreoAttribute), inherit: false).Length > 0;

            var docClaim = context.HttpContext.User.FindFirst("doc")?.Value;
            var idRuta = context.RouteData.Values.TryGetValue("id", out var idValor) ? idValor?.ToString() : null;

            if (!accionPermitida || docClaim is null || docClaim != idRuta)
            {
                context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
                return;
            }
        }

        await next();
    }
}
