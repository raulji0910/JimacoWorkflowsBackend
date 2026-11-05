namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record RolDto(
    int Id,
    string Nombre,
    string? Descripcion,
    bool Activo,
    bool PuedeDevolverCualquierDocumento,
    bool PuedeGestionarAlistamiento,
    bool PuedeVerAlistamiento,
    bool PuedeVerCuentasPorPagar);

public record CrearRolDto(
    string Nombre,
    string? Descripcion,
    bool PuedeDevolverCualquierDocumento = false,
    bool PuedeGestionarAlistamiento = false,
    bool PuedeVerAlistamiento = false,
    bool PuedeVerCuentasPorPagar = false);

public record ActualizarRolDto(
    string Nombre,
    string? Descripcion,
    bool Activo,
    bool PuedeDevolverCualquierDocumento,
    bool PuedeGestionarAlistamiento,
    bool PuedeVerAlistamiento,
    bool PuedeVerCuentasPorPagar = false);
