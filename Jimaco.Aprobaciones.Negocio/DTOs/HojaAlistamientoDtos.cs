namespace Jimaco.Aprobaciones.Negocio.DTOs;

/// <summary>Una Remisión real de World Office que aportó cantidad a un renglón puntual (2026-10-26) — un mismo renglón puede tener varias si se despachó en partes.</summary>
public record DespachoRemisionDto(string NumeroRemision, DateTime Fecha, decimal Cantidad);

/// <summary>Un renglón de una Hoja de Alistamiento, con lo pedido, lo que Transporte ya tiene disponible, y lo que World Office ya remisionó.</summary>
public record RenglonHojaAlistamientoDto(
    int Id,
    string? Codigo,
    string Descripcion,
    decimal Cantidad,
    string? UnidadMedida,
    decimal CantidadDisponible,
    // Suma de DespachosRemision — informativo, ya está implícito en la lista, pero evita que el
    // frontend tenga que sumarlo.
    decimal CantidadRemisionada,
    IReadOnlyList<DespachoRemisionDto> DespachosRemision);

/// <summary>
/// Una OC que comparte una Hoja de Alistamiento con otras (misma Cotización real de WO ligada a más
/// de una OC) — ver la nota de agrupación en HojaAlistamientoResumenDto.
/// </summary>
public record OcVinculadaAlistamientoDto(int InstanciaDocumentoId, string? NumeroReferencia);

/// <summary>Una Hoja de Alistamiento completa (cabecera + renglones) — vista propia, sin el resto de la OC.</summary>
public record HojaAlistamientoDetalleDto(
    int Id,
    IReadOnlyList<OcVinculadaAlistamientoDto> Ocs,
    string NumeroReferencia,
    string? Cliente,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? CampoEntrega,
    // Momento en que Gerencia Comercial aprobó el primer paso de la OC (no cuándo se generó la
    // hoja) — es el reloj real de Transporte, ver HojaAlistamientoService. Si la hoja es compartida
    // por varias OC, es la aprobación MÁS ANTIGUA entre ellas.
    DateTime FechaAprobacionComercial,
    bool Completa,
    // Informativo para Logística (pedido explícito del usuario, 2026-10-09) — misma fórmula que usa
    // el PDF real de la Hoja de Alistamiento (DocumentoPdfGenerador.AgregarHojaAlistamiento):
    // Subtotal = Σ Total de cada renglón, TotalIva = Σ Total×%IVA, Total = Subtotal + TotalIva.
    decimal Subtotal,
    decimal TotalIva,
    decimal Total,
    // Remisionado (2026-10-26, llevado a nivel de renglón — ver RenglonHojaAlistamientoDto):
    // Remisionado = TODOS los renglones están 100% remisionados; RemisionadoParcial = al menos uno
    // tiene algo remisionado pero no todos llegaron a su cantidad pedida. Independiente de Completa:
    // una hoja puede estar incompleta/atrasada en nuestro control Y remisionada en WO al mismo tiempo.
    bool Remisionado,
    bool RemisionadoParcial,
    IReadOnlyList<RenglonHojaAlistamientoDto> Renglones);

/// <summary>Cantidad disponible que Transporte registra para un renglón puntual de una Hoja de Alistamiento.</summary>
public record ActualizarCantidadRenglonDto(int RenglonId, decimal CantidadDisponible);

public record ActualizarCantidadesDisponiblesDto(IReadOnlyList<ActualizarCantidadRenglonDto> Renglones);

/// <summary>
/// Una Cotización con Hoja de Alistamiento activa que todavía no está marcada como remisionada —
/// el Sincronizador pide esta lista periódicamente y busca cada una en las Remisiones reales de WO
/// (patrón "pedir y recoger", igual que el Comprobante de Egreso).
/// </summary>
public record PendienteRemisionDto(string PrefijoOrigen, string NumeroReferencia);

/// <summary>
/// Una Cotización con Hoja de Alistamiento activa que todavía NO está Completa (le falta disponible
/// en al menos un renglón) — 2026-10-14, independiente de si ya está Remisionada o no. Antes el
/// Sincronizador reusaba <see cref="PendienteRemisionDto"/> para decidir qué refrescar, pero eso
/// corta el refresco en cuanto se marca Remisionada aunque Transporte todavía no haya terminado de
/// alistarla (caso real: Hoja 16159, Remisionada pero con ítems sin completar, que dejó de
/// refrescarse) — por eso esta lista es independiente, con su propio criterio de corte.
/// </summary>
public record PendienteRefrescoDto(string PrefijoOrigen, string NumeroReferencia);

/// <summary>Un renglón real de la Remisión (código + cantidad), para cruzar contra los renglones de la Hoja.</summary>
public record RenglonRemisionadoDto(string? Codigo, decimal Cantidad);

/// <summary>
/// Lo que el Sincronizador reporta cuando encuentra, en una Remisión real, el cruce con una
/// Cotización pendiente — ahora con los renglones reales de esa Remisión (2026-10-26), para aplicar
/// la cantidad solo a los ítems que de verdad coinciden por Código, no al documento completo.
/// </summary>
public record MarcarRemisionadaDto(string PrefijoOrigen, string NumeroReferencia, string NumeroRemision, DateTime Fecha, IReadOnlyList<RenglonRemisionadoDto> Renglones);

/// <summary>
/// Una fila del dashboard de alistamiento — una Hoja de Alistamiento física (una Cotización real de
/// WO), para Transporte (gestiona) y Gerencia Comercial (supervisa). Una misma Cotización puede
/// venir ligada a más de una OC (World Office genera una copia de DocumentoVinculado por cada una),
/// pero acá se agrupan por Prefijo+Número real y se muestran UNA sola vez — pedido explícito del
/// usuario (2026-09-29): "el alistamiento acá solo debe aparecer una vez", aunque el control de OC
/// en el resto del sistema siga siendo por separado. <see cref="DocumentoVinculadoId"/> es una fila
/// representante del grupo (cualquiera de las copias sirve para entrar al detalle, que ya devuelve
/// la info fusionada).
/// </summary>
public record HojaAlistamientoResumenDto(
    int DocumentoVinculadoId,
    IReadOnlyList<OcVinculadaAlistamientoDto> Ocs,
    string NumeroReferenciaHoja,
    string? Cliente,
    // "CAMPO ENTREGA - CIUDAD - DIRECCION" de la Hoja de Alistamiento real (Personalizado2 en WO) —
    // para filtrar/ubicar en el dashboard (pedido explícito del usuario, 2026-09-30).
    string? CampoEntrega,
    // Quien elaboró la Cotización en WO — mismo pedido, para filtrar por emisor.
    string? ElaboradoPor,
    // Aprobación MÁS ANTIGUA entre las OC del grupo — apenas una se aprueba, Transporte ya puede
    // empezar a alistar esta Cotización.
    DateTime FechaAprobacionComercial,
    bool Completa,
    // true cuando sigue incompleta y ya pasó el umbral configurado (Alistamiento:HorasUmbralAlerta) desde FechaAprobacionComercial.
    bool EnAlerta,
    int TotalRenglones,
    int RenglonesCompletos,
    // Informativo para Logística — mismo cálculo que el PDF real (ver nota en HojaAlistamientoDetalleDto).
    decimal Total,
    bool Remisionado,
    bool RemisionadoParcial,
    // Cuándo pasó a completa (UTC) — para subir las recién completadas en el dashboard (2026-11-11).
    DateTime? FechaCompletada = null);
