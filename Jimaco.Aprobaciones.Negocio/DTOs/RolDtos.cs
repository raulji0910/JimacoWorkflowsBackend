namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record RolDto(
    int Id,
    string Nombre,
    string? Descripcion,
    bool Activo,
    bool PuedeDevolverCualquierDocumento,
    bool PuedeGestionarAlistamiento,
    bool PuedeVerAlistamiento,
    bool PuedeVerCuentasPorPagar,
    bool PuedePagarCuentas = false,
    bool PuedeGestionarEntregas = false,
    bool PuedeVerEntregas = false,
    bool PuedeDocumentarEntregas = false,
    bool PuedeFacturarEntregas = false);

public record CrearRolDto(
    string Nombre,
    string? Descripcion,
    bool PuedeDevolverCualquierDocumento = false,
    bool PuedeGestionarAlistamiento = false,
    bool PuedeVerAlistamiento = false,
    bool PuedeVerCuentasPorPagar = false,
    bool PuedePagarCuentas = false,
    bool PuedeGestionarEntregas = false,
    bool PuedeVerEntregas = false,
    bool PuedeDocumentarEntregas = false,
    bool PuedeFacturarEntregas = false);

public record ActualizarRolDto(
    string Nombre,
    string? Descripcion,
    bool Activo,
    bool PuedeDevolverCualquierDocumento,
    bool PuedeGestionarAlistamiento,
    bool PuedeVerAlistamiento,
    bool PuedeVerCuentasPorPagar = false,
    bool PuedePagarCuentas = false,
    bool PuedeGestionarEntregas = false,
    bool PuedeVerEntregas = false,
    bool PuedeDocumentarEntregas = false,
    bool PuedeFacturarEntregas = false);
