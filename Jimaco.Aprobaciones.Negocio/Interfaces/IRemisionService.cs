using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>
/// Entregas (2026-11-12): Remisiones contabilizadas de World Office que Logística imprime y entrega,
/// confirmando con el soporte firmado por el cliente. Ver Modelo.Entidades.Remision y los permisos
/// Rol.PuedeGestionarEntregas / Rol.PuedeVerEntregas.
/// </summary>
public interface IRemisionService
{
    // ---- Sincronizador ----
    Task<EstadoSincronizacionRemisionesDto> ObtenerEstadoSincronizacionAsync(CancellationToken ct = default);

    /// <summary>Crea las que no existan (idempotente por IdAsientoContableWO).</summary>
    Task<ResultadoSincronizacionRemisionesDto> RegistrarAsync(IReadOnlyList<RemisionInputDto> remisiones, CancellationToken ct = default);

    /// <summary>REM que se anularon en WO después de traerlas — salen del dashboard (si no se entregaron).</summary>
    Task MarcarAnuladasAsync(IReadOnlyList<int> idsAsientoContableWO, CancellationToken ct = default);

    // ---- Página ----
    /// <summary>
    /// Todas las por entregar + las entregadas de los últimos <c>Entregas:DiasEntregadasVisibles</c>
    /// días (default 60) — el dashboard filtra y pagina en el navegador, como el de Alistamiento.
    /// </summary>
    Task<IReadOnlyList<RemisionTableroDto>> ListarAsync(int usuarioId, CancellationToken ct = default);

    Task<RemisionDetalleDto> ObtenerAsync(int id, int usuarioId, CancellationToken ct = default);

    /// <summary>PDF de una o varias remisiones (una por página), con el QR de cada una.</summary>
    Task<byte[]> GenerarPdfAsync(IReadOnlyList<int> ids, int usuarioId, CancellationToken ct = default);

    /// <summary>Exige PuedeGestionarEntregas, al menos un archivo y que la remisión esté por entregar.</summary>
    Task<RemisionDetalleDto> ConfirmarEntregaAsync(int id, int usuarioId, string? observacion, IReadOnlyList<ArchivoSoporteEntrega> archivos, CancellationToken ct = default);

    Task<(Stream Contenido, string NombreArchivo, string? ContentType)> DescargarSoporteAsync(int soporteId, int usuarioId, CancellationToken ct = default);
}

public interface IRemisionPdfGenerador
{
    byte[] Generar(IReadOnlyList<RemisionDetalleDto> remisiones);
}
