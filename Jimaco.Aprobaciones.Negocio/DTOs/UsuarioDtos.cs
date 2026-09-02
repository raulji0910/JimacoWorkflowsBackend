namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record UsuarioDto(int Id, string Nombre, string Email, string? Telefono, bool Activo, IReadOnlyList<RolDto> Roles);

public record CrearUsuarioDto(string Nombre, string Email, string Password, string? Telefono, IReadOnlyList<int> RolesIds);

public record ActualizarUsuarioDto(string Nombre, string? Telefono, bool Activo, IReadOnlyList<int> RolesIds);

public record CambiarPasswordDto(string PasswordActual, string PasswordNueva);
