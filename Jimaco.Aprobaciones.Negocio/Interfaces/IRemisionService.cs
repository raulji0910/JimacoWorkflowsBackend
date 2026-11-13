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

    /// <summary>
    /// Revisa un archivo ANTES de confirmar (para mostrarle al operario si sirve). El resultado queda
    /// en memoria unos minutos para que confirmar no vuelva a llamar a la IA con el mismo archivo.
    /// </summary>
    Task<VerificacionSoporteDto> VerificarSoporteAsync(int id, int usuarioId, ArchivoSoporteEntrega archivo, CancellationToken ct = default);

    /// <summary>
    /// Exige PuedeGestionarEntregas y que AL MENOS UN archivo sea válido (esta remisión + firma del
    /// cliente). Si ninguno lo es, no guarda nada y la remisión sigue por entregar.
    /// </summary>
    Task<RemisionDetalleDto> ConfirmarEntregaAsync(int id, int usuarioId, string? observacion, IReadOnlyList<ArchivoSoporteEntrega> archivos, CancellationToken ct = default);

    Task<(Stream Contenido, string NombreArchivo, string? ContentType)> DescargarSoporteAsync(int soporteId, int usuarioId, CancellationToken ct = default);

    // ---- Facturación (fase 2, 2026-11-13) ----
    /// <summary>
    /// El emisor adjunta un documento de un tipo documental. Se guarda siempre; si la IA está
    /// encendida y no ve que sea el documento pedido (o no cuadra la OC/cliente), queda con un aviso
    /// que NO bloquea (decisión del usuario).
    /// </summary>
    Task<RemisionDetalleDto> AdjuntarDocumentoAsync(int id, int usuarioId, int tipoDocumentoId, ArchivoSoporteEntrega archivo, CancellationToken ct = default);

    Task<RemisionDetalleDto> EliminarDocumentoAsync(int id, int soporteId, int usuarioId, CancellationToken ct = default);

    /// <summary>Exige todos los obligatorios; pasa a "En facturación" y avisa por correo a los contables.</summary>
    Task<RemisionDetalleDto> EnviarAFacturacionAsync(int id, int usuarioId, string? comentario, CancellationToken ct = default);

    /// <summary>El contable la devuelve al emisor con un comentario (y correo); el emisor la vuelve a tramitar.</summary>
    Task<RemisionDetalleDto> DevolverAsync(int id, int usuarioId, string comentario, CancellationToken ct = default);

    Task<IReadOnlyList<TipoDocumentoFacturacionDto>> ListarTiposDocumentoAsync(CancellationToken ct = default);
    Task<TipoDocumentoFacturacionDto> GuardarTipoDocumentoAsync(int? id, GuardarTipoDocumentoFacturacionDto dto, CancellationToken ct = default);

    // ---- Administración (solo Admin, lo controla el controlador) ----
    Task<ConfiguracionEntregasDto> ObtenerConfiguracionAsync(CancellationToken ct = default);
    Task<ConfiguracionEntregasDto> ActualizarConfiguracionAsync(ActualizarConfiguracionEntregasDto dto, int usuarioId, CancellationToken ct = default);
}

public interface IRemisionPdfGenerador
{
    byte[] Generar(IReadOnlyList<RemisionDetalleDto> remisiones);
}
