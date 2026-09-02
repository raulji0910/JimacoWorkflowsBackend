namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record LoginRequestDto(string Email, string Password);

public record LoginResponseDto(string Token, string Nombre, string Email, IReadOnlyList<string> Roles);
