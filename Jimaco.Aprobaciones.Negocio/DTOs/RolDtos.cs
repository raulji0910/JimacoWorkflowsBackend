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
    // Alistamiento de FERREALIADOS (2026-11-13) — dashboard aparte, permisos aparte.
    bool PuedeGestionarAlistamientoFerrealiados = false,
    bool PuedeVerAlistamientoFerrealiados = false,
    // Consulta de pagos, solo lectura + reporte por correo de cada lote (2026-11-23) — ver Rol.PuedeConsultarPagos.
    bool PuedeConsultarPagos = false,
    // Conciliación de facturas de proveedor vs OC (2026-11-24) — ver Rol.PuedeVerConciliacion.
    bool PuedeVerConciliacion = false);

public record CrearRolDto(
    string Nombre,
    string? Descripcion,
    bool PuedeDevolverCualquierDocumento = false,
    bool PuedeGestionarAlistamiento = false,
    bool PuedeVerAlistamiento = false,
    bool PuedeVerCuentasPorPagar = false,
    bool PuedePagarCuentas = false,
    // Alistamiento de FERREALIADOS (2026-11-13) — dashboard aparte, permisos aparte.
    bool PuedeGestionarAlistamientoFerrealiados = false,
    bool PuedeVerAlistamientoFerrealiados = false,
    // Consulta de pagos, solo lectura + reporte por correo de cada lote (2026-11-23) — ver Rol.PuedeConsultarPagos.
    bool PuedeConsultarPagos = false,
    // Conciliación de facturas de proveedor vs OC (2026-11-24) — ver Rol.PuedeVerConciliacion.
    bool PuedeVerConciliacion = false);

public record ActualizarRolDto(
    string Nombre,
    string? Descripcion,
    bool Activo,
    bool PuedeDevolverCualquierDocumento,
    bool PuedeGestionarAlistamiento,
    bool PuedeVerAlistamiento,
    bool PuedeVerCuentasPorPagar = false,
    bool PuedePagarCuentas = false,
    // Alistamiento de FERREALIADOS (2026-11-13) — dashboard aparte, permisos aparte.
    bool PuedeGestionarAlistamientoFerrealiados = false,
    bool PuedeVerAlistamientoFerrealiados = false,
    // Consulta de pagos, solo lectura + reporte por correo de cada lote (2026-11-23) — ver Rol.PuedeConsultarPagos.
    bool PuedeConsultarPagos = false,
    // Conciliación de facturas de proveedor vs OC (2026-11-24) — ver Rol.PuedeVerConciliacion.
    bool PuedeVerConciliacion = false);
