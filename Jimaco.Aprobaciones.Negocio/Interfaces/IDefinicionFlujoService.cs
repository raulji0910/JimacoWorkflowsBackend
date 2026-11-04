using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface IDefinicionFlujoService
{
    Task<IReadOnlyList<DefinicionFlujoDto>> ListarPorTipoDocumentoAsync(int tipoDocumentoId, CancellationToken ct = default);
    Task<DefinicionFlujoDto> ObtenerAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Crea una nueva definición y la marca Activa. Desde 2026-09-21 puede haber varias
    /// definiciones activas a la vez para el mismo tipo de documento (ver
    /// ValorMinimo/ValorMaximo/FormaDePagoRequerida) — se rechaza si su condición se pisa con la de
    /// otra definición ya activa del mismo tipo de documento, para no dejar casos ambiguos.
    /// </summary>
    Task<DefinicionFlujoDto> CrearAsync(CrearDefinicionFlujoDto dto, CancellationToken ct = default);

    /// <summary>Desactiva una definición — deja de poder elegirse para documentos nuevos (no afecta los que ya la estén usando).</summary>
    Task DesactivarAsync(int id, CancellationToken ct = default);

    /// <summary>Reactiva una definición — falla si su condición se pisa con la de otra ya activa del mismo tipo de documento.</summary>
    Task ActivarAsync(int id, CancellationToken ct = default);
}
