using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>
/// Control de alistamiento (2026-09-28): Transporte va registrando cuánto tiene disponible de cada
/// renglón de una Hoja de Alistamiento (DocumentoVinculado), independiente del flujo de aprobación
/// de la OC — ver Rol.PuedeGestionarAlistamiento/PuedeVerAlistamiento.
/// </summary>
public interface IHojaAlistamientoService
{
    /// <summary>Todas las Hojas de Alistamiento con su estado — exige que el usuario tenga PuedeGestionarAlistamiento o PuedeVerAlistamiento.</summary>
    Task<IReadOnlyList<HojaAlistamientoResumenDto>> ListarAsync(int usuarioId, CancellationToken ct = default);

    /// <summary>Una Hoja de Alistamiento puntual, con sus renglones — vista propia sin el resto de la OC.</summary>
    Task<HojaAlistamientoDetalleDto> ObtenerAsync(int documentoVinculadoId, int usuarioId, CancellationToken ct = default);

    /// <summary>Registra la cantidad disponible de uno o más renglones de una hoja puntual — exige PuedeGestionarAlistamiento.</summary>
    Task ActualizarCantidadesAsync(int documentoVinculadoId, int usuarioId, ActualizarCantidadesDisponiblesDto dto, CancellationToken ct = default);

    /// <summary>Cotizaciones con Hoja de Alistamiento activa que todavía no están marcadas como remisionadas — para el Sincronizador.</summary>
    Task<IReadOnlyList<PendienteRemisionDto>> ListarPendientesRemisionAsync(CancellationToken ct = default);

    /// <summary>
    /// Cotizaciones con Hoja de Alistamiento activa que todavía NO está Completa — independiente de
    /// si ya se remisionó (ver <see cref="PendienteRefrescoDto"/>). El Sincronizador usa esta lista,
    /// no <see cref="ListarPendientesRemisionAsync"/>, para decidir a cuáles volver a leer en WO.
    /// </summary>
    Task<IReadOnlyList<PendienteRefrescoDto>> ListarPendientesRefrescoAsync(CancellationToken ct = default);

    /// <summary>
    /// Crea una Hoja de Alistamiento SIN ligarla a ninguna OC (2026-10-21) — para Cotizaciones que el
    /// Sincronizador detecta directo en World Office, sin que nunca haya pasado una Orden de Compra
    /// por el flujo de aprobación. Idempotente por Prefijo+Número: si ya existe cualquier copia (con
    /// o sin OC) para esa Cotización, no hace nada.
    /// </summary>
    Task CrearSinOcAsync(DocumentoVinculadoInputDto dto, CancellationToken ct = default);

    /// <summary>
    /// El Sincronizador reporta que encontró el cruce con una Remisión real, con sus renglones reales
    /// (código + cantidad) — se cruza por Código contra los renglones de la Hoja en TODAS las copias
    /// que comparten esa Cotización (2026-10-26: el despacho queda a nivel de renglón, no de
    /// documento completo, porque un mismo ítem puede llegar repartido en varias Remisiones).
    /// </summary>
    Task MarcarRemisionadaAsync(MarcarRemisionadaDto dto, CancellationToken ct = default);

    /// <summary>
    /// El Sincronizador vuelve a leer una Cotización todavía activa (no remisionada) y manda su
    /// estado ACTUAL en World Office — reemplaza cabecera y renglones en TODAS las copias que
    /// comparten esa Cotización, conservando la cantidad disponible ya marcada para los ítems que
    /// sigan siendo los mismos (ver nota en DocumentoVinculado).
    /// </summary>
    Task RefrescarDesdeWOAsync(DocumentoVinculadoInputDto dto, CancellationToken ct = default);
}
