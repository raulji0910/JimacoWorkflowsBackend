namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record PasoFlujoDto(
    int Id,
    string Nombre,
    int Orden,
    bool PermiteDevolver,
    bool PermiteRechazar,
    // Se combinan: si PermiteDevolverAlEmisor es true, "El emisor" es una opción más, además de
    // los pasos anteriores puntuales en DestinosDevolucionIds. Con un total de 0 opciones,
    // "Devolver" queda mal configurado (ver validación en DefinicionFlujoService).
    bool PermiteDevolverAlEmisor,
    IReadOnlyList<int> DestinosDevolucionIds,
    // Si "Aprobar" acá exige el número de un Comprobante de Egreso de WO — ver ComprobanteEgreso.
    bool RequiereComprobanteEgresoAlAprobar,
    // Si "Aprobar" acá exige un comentario obligatorio (ej. "a quién le entregó el dinero").
    bool RequiereComentarioAlAprobar,
    // Si está, "Aprobar" acá también ofrece "Redirigir a pago de contado" hacia este otro paso del
    // mismo flujo (ver PasoFlujo.PasoRedirigirAPagoContadoId). Null = esta acción no aplica acá.
    int? PasoRedirigirAPagoContadoId,
    // Si "Aprobar" acá exige adjuntar un PDF con el soporte del pago (ej. Gerencia General al
    // hacer la transferencia) — ver PasoFlujo.RequiereSoportePagoAlAprobar.
    bool RequiereSoportePagoAlAprobar,
    IReadOnlyList<int> RolesIds);

public record PasoFlujoInputDto(
    string Nombre,
    int Orden,
    bool PermiteDevolver,
    bool PermiteRechazar,
    bool PermiteDevolverAlEmisor,
    // Referenciados por Orden (no por Id, que todavía no existe al crear el flujo entero de una).
    IReadOnlyList<int> DestinosDevolucionOrdenes,
    bool RequiereComprobanteEgresoAlAprobar,
    bool RequiereComentarioAlAprobar,
    // Referenciado por Orden, igual que DestinosDevolucionOrdenes — pero tiene que ser un paso
    // POSTERIOR (Orden mayor) del mismo flujo, nunca uno anterior.
    int? RedirigirAPagoContadoOrden,
    bool RequiereSoportePagoAlAprobar,
    IReadOnlyList<int> RolesIds);

public record DefinicionFlujoDto(
    int Id,
    string Nombre,
    int TipoDocumentoId,
    bool Activo,
    // Condición de cuándo aplica este flujo — ver DefinicionFlujo. Todos null = aplica siempre.
    decimal? ValorMinimo,
    decimal? ValorMaximo,
    string? FormaDePagoRequerida,
    IReadOnlyList<PasoFlujoDto> Pasos);

public record CrearDefinicionFlujoDto(
    string Nombre,
    int TipoDocumentoId,
    decimal? ValorMinimo,
    decimal? ValorMaximo,
    string? FormaDePagoRequerida,
    IReadOnlyList<PasoFlujoInputDto> Pasos);
