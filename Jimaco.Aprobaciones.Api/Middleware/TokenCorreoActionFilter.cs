using Jimaco.Aprobaciones.Modelo;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Api.Middleware;

/// <summary>
/// Filtro global (registrado en Program.cs) que acota lo que puede hacer el token de "acción desde
/// el correo": si el token trae el claim "purpose"="correo-accion", solo deja pasar a acciones
/// marcadas con <see cref="PermiteTokenCorreoAttribute"/>, y únicamente si el "doc" del token
/// coincide con el {id} de la ruta que se está pidiendo. Un token de sesión normal (login por
/// usuario/clave) no tiene ese claim y no se ve afectado por nada de esto.
/// </summary>
public class TokenCorreoActionFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var purpose = context.HttpContext.User.FindFirst("purpose")?.Value;
        if (purpose == "correo-accion")
        {
            var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
            var atributo = descriptor?.MethodInfo.GetCustomAttributes(typeof(PermiteTokenCorreoAttribute), inherit: false)
                .Cast<PermiteTokenCorreoAttribute>().FirstOrDefault();

            var docClaim = context.HttpContext.User.FindFirst("doc")?.Value;
            var idRuta = context.RouteData.Values.TryGetValue("id", out var idValor) ? idValor?.ToString() : null;

            if (atributo is null || docClaim is null || docClaim != idRuta)
            {
                context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
                return;
            }

            // Un link de correo queda atado al paso que estaba ACTUAL cuando se mandó — si para
            // cuando se usa el documento ya avanzó de ahí (esta misma persona ya actuó, o alguien
            // más lo hizo por otro medio), no puede volver a actuar con ese link, aunque intente
            // pegarle directo a la Api sin pasar por la pantalla. El claim "paso" puede faltar en
            // links viejos emitidos antes de este chequeo (2026-09-22) — ahí no se puede validar,
            // se deja pasar como antes en vez de invalidar de golpe todos los correos ya mandados.
            if (atributo.RequierePasoVigente && docClaim is not null
                && context.HttpContext.User.FindFirst("paso")?.Value is string pasoClaim
                && int.TryParse(idRuta, out var instanciaId) && int.TryParse(pasoClaim, out var pasoToken))
            {
                var pasoActualId = await db.InstanciasDocumento
                    .Where(i => i.Id == instanciaId)
                    .Select(i => (int?)i.PasoActualId)
                    .FirstOrDefaultAsync();

                if (pasoActualId != pasoToken)
                {
                    context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(
                        new { mensaje = "Este enlace ya fue utilizado o el documento ya avanzó a otro paso — entrá al sistema para ver su estado actual." })
                    {
                        StatusCode = 409
                    };
                    return;
                }
            }
        }

        await next();
    }
}
