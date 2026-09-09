using Jimaco.Aprobaciones.Modelo.Entidades;

namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record CampoTipoDocumentoDto(int Id, string Nombre, string Etiqueta, TipoCampo TipoCampo, bool Requerido, int Orden, IReadOnlyList<string>? Opciones);

public record CampoTipoDocumentoInputDto(string Nombre, string Etiqueta, TipoCampo TipoCampo, bool Requerido, int Orden, IReadOnlyList<string>? Opciones);

public record TipoDocumentoDto(int Id, string Nombre, string? Descripcion, string PrefijoWorldOffice, bool Activo, IReadOnlyList<CampoTipoDocumentoDto> Campos);

public record CrearTipoDocumentoDto(string Nombre, string? Descripcion, string PrefijoWorldOffice, IReadOnlyList<CampoTipoDocumentoInputDto> Campos);

public record ActualizarTipoDocumentoDto(string Nombre, string? Descripcion, string PrefijoWorldOffice, bool Activo, IReadOnlyList<CampoTipoDocumentoInputDto> Campos);
