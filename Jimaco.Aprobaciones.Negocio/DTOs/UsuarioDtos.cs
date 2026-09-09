namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record UsuarioDto(int Id, string Nombre, string Email, string? Telefono, string? UsuarioWO, bool Activo, IReadOnlyList<RolDto> Roles);

public record CrearUsuarioDto(string Nombre, string Email, string Password, string? Telefono, string? UsuarioWO, IReadOnlyList<int> RolesIds);

public record ActualizarUsuarioDto(string Nombre, string Email, string? Telefono, string? UsuarioWO, bool Activo, IReadOnlyList<int> RolesIds);

public record CambiarPasswordDto(string PasswordActual, string PasswordNueva);
