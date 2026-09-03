using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class NotificacionesController(INotificacionService notificacionService) : ControllerBase
{
    public record PruebaDto(string DestinatarioEmail);

    /// <summary>Envía un correo suelto de prueba, sin asociarlo a ningún documento — para validar la configuración SMTP.</summary>
    [HttpPost("prueba")]
    public async Task<IActionResult> EnviarPrueba(PruebaDto dto, CancellationToken ct)
    {
        await notificacionService.EnviarPruebaAsync(dto.DestinatarioEmail, ct);
        return Ok(new { mensaje = $"Correo de prueba enviado a {dto.DestinatarioEmail}." });
    }
}
