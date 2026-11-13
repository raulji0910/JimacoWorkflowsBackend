namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record UsuarioDto(int Id, string Nombre, string Email, string? Telefono, string? UsuarioWO, bool Activo, IReadOnlyList<RolDto> Roles, string? NombresCompletosWO, string? UsuarioWOFerrealiados = null);

public record CrearUsuarioDto(string Nombre, string Email, string Password, string? Telefono, string? UsuarioWO, IReadOnlyList<int> RolesIds, string? NombresCompletosWO = null, string? UsuarioWOFerrealiados = null);

public record ActualizarUsuarioDto(string Nombre, string Email, string? Telefono, string? UsuarioWO, bool Activo, IReadOnlyList<int> RolesIds, string? NombresCompletosWO = null, string? UsuarioWOFerrealiados = null);

public record CambiarPasswordDto(string PasswordActual, string PasswordNueva);

/// <summary>Restablecimiento de contraseña por un Administrador — a diferencia de <see cref="CambiarPasswordDto"/>, no exige la contraseña actual (para eso está restringido a rol Admin en el controller).</summary>
public record RestablecerPasswordDto(string PasswordNueva);
