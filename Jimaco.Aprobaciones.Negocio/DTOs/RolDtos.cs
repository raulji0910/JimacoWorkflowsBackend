namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record RolDto(int Id, string Nombre, string? Descripcion, bool Activo);

public record CrearRolDto(string Nombre, string? Descripcion);

public record ActualizarRolDto(string Nombre, string? Descripcion, bool Activo);
