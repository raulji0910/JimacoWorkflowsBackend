using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Aprobaciones.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login(LoginRequestDto dto, CancellationToken ct)
    {
        var resultado = await authService.LoginAsync(dto.Email, dto.Password, ct);
        return resultado is null ? Unauthorized(new { mensaje = "Correo o contraseña incorrectos." }) : Ok(resultado);
    }
}
