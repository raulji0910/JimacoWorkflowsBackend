namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record LoginRequestDto(string Email, string Password);

public record LoginResponseDto(string Token, string Nombre, string Email, IReadOnlyList<string> Roles,
    // Si alguno de los roles del usuario tiene el permiso administrativo de buscar y devolver
    // cualquier documento (ver Rol.PuedeDevolverCualquierDocumento) — el frontend usa esto para
    // mostrar/ocultar la pantalla de búsqueda, sin tener que consultar los roles uno por uno.
    bool PuedeDevolverCualquierDocumento,
    // Ídem para el control de alistamiento — ver Rol.PuedeGestionarAlistamiento/PuedeVerAlistamiento.
    // Alcanza con cualquiera de los dos para ver el dashboard; solo el primero habilita editar.
    bool PuedeGestionarAlistamiento,
    bool PuedeVerAlistamiento,
    // Módulo de Cuentas por Pagar — ver Rol.PuedeVerCuentasPorPagar.
    bool PuedeVerCuentasPorPagar,
    // Menú "Pagos" — ver Rol.PuedePagarCuentas.
    bool PuedePagarCuentas = false,
    // Entregas (Remisiones) — ver Rol.PuedeGestionarEntregas/PuedeVerEntregas. Cualquiera de los
    // dos muestra el dashboard; solo el primero habilita imprimir/confirmar entregas.
    bool PuedeGestionarEntregas = false,
    bool PuedeVerEntregas = false);
