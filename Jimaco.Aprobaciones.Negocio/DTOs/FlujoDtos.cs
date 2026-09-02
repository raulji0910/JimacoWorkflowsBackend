namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record PasoFlujoDto(
    int Id,
    string Nombre,
    int Orden,
    bool PermiteDevolver,
    bool PermiteRechazar,
    int? PasoDestinoDevolucionId,
    IReadOnlyList<int> RolesIds);

public record PasoFlujoInputDto(
    string Nombre,
    int Orden,
    bool PermiteDevolver,
    bool PermiteRechazar,
    int? PasoDestinoDevolucionOrden,
    IReadOnlyList<int> RolesIds);

public record DefinicionFlujoDto(int Id, string Nombre, int TipoDocumentoId, bool Activo, IReadOnlyList<PasoFlujoDto> Pasos);

public record CrearDefinicionFlujoDto(string Nombre, int TipoDocumentoId, IReadOnlyList<PasoFlujoInputDto> Pasos);
