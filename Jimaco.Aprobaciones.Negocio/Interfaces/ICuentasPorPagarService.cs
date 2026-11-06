using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>
/// Módulo de Cuentas por Pagar (2026-11-05): foto de lo que se les debe a los proveedores según World
/// Office, y creación de documentos "Pago a Proveedor" con las facturas que decide pagar Gerencia
/// Comercial. Todo lo de la pantalla exige Rol.PuedeVerCuentasPorPagar. Desde 2026-11-06 el CE lo
/// crea el Sincronizador en WO cuando el gerente general confirma el pago (sin paso de Contabilidad).
/// </summary>
public interface ICuentasPorPagarService
{
    /// <summary>Reemplaza la foto completa (la llama el Sincronizador en cada corrida).</summary>
    Task GuardarFotoAsync(FotoCuentasPorPagarDto foto, CancellationToken ct = default);

    Task<ResumenCuentasPorPagarDto> ObtenerResumenAsync(int usuarioId, CancellationToken ct = default);

    Task<DetalleProveedorCuentaPorPagarDto> ObtenerProveedorAsync(string identificacion, int usuarioId, CancellationToken ct = default);

    /// <summary>Crea el documento "Pago a Proveedor" con las partidas (y anticipos) elegidos — devuelve su Id.</summary>
    Task<int> CrearPagoAsync(CrearPagoProveedorDto dto, int usuarioId, CancellationToken ct = default);

    /// <summary>Bancos/cajas de WO para elegir al confirmar un pago (no exige el permiso del módulo: lo usa el gerente general).</summary>
    Task<IReadOnlyList<CuentaBancoDto>> ObtenerBancosAsync(CancellationToken ct = default);

    // ---- Solo Sincronizador ----

    Task<IReadOnlyList<PendienteCreacionCEDto>> ListarPendientesCreacionCEAsync(CancellationToken ct = default);

    Task ConfirmarCECreadoAsync(int instanciaDocumentoId, CECreadoDto dto, CancellationToken ct = default);

    Task ReportarConflictoCreacionCEAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default);
}
