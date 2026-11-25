namespace Jimaco.Aprobaciones.Negocio.DTOs;

// ---- Lo que sube el Sincronizador (modo --documentos, 2026-11-25) ----

/// <summary>Hasta dónde llegó la última sincronización — el Sincronizador decide con esto qué mandar.</summary>
public record EstadoCatalogoWODto(long UltimaVersionProductos, DateTime? FechaProductos, DateTime? FechaExistencias, DateTime? FechaCatalogos);

/// <summary>Un producto de <c>Inventarios</c>; <paramref name="Version"/> es el <c>VersionCol</c> como número.</summary>
public record ProductoWOInputDto(int IdInventario, string Codigo, string Descripcion, string? UnidadMedida, decimal Iva,
    int? IdClasificacion, decimal Precio1, bool Activo, long Version);

public record ExistenciaWOInputDto(int IdInventario, string IdBodega, decimal Existencia);

public record BodegaWOInputDto(string IdBodega, string Nombre, bool Activa);

public record DireccionTerceroWOInputDto(int IdDireccion, int IdTercero, string? Direccion, string? Telefonos, string? Ciudad, bool Principal);

public record FormaPagoWOInputDto(int IdFormaPago, string Nombre);

public record CampoPersonalizadoWOInputDto(string TipoDocumento, int Numero, string Etiqueta, bool Activo);

/// <summary>Catálogos chicos que cambian poco: se mandan completos (cada hora) y reemplazan los anteriores.</summary>
public record CatalogosWOInputDto(
    IReadOnlyList<BodegaWOInputDto> Bodegas,
    IReadOnlyList<DireccionTerceroWOInputDto> Direcciones,
    IReadOnlyList<FormaPagoWOInputDto> FormasPago,
    IReadOnlyList<CampoPersonalizadoWOInputDto> CamposPersonalizados);

// ---- Lo que consulta el portal (rol "Crea cotizaciones") ----

public record ExistenciaBodegaDto(string IdBodega, string Bodega, decimal Existencia);

public record ProductoCatalogoDto(int IdInventario, string Codigo, string Descripcion, string UnidadMedida, decimal Iva,
    int? IdClasificacion, decimal Precio1, decimal ExistenciaTotal, IReadOnlyList<ExistenciaBodegaDto> Existencias);

public record ClienteCatalogoDto(int IdTercero, string Identificacion, string Nombre);

public record DireccionClienteDto(int IdDireccion, string? Direccion, string? Ciudad, string? Telefonos, bool Principal);

public record BodegaDto(string IdBodega, string Nombre);

public record FormaPagoDto(int IdFormaPago, string Nombre);

public record CampoMasDatosDto(int Numero, string Etiqueta);

/// <summary>Todo lo fijo que necesita la pantalla de Cotización para arrancar.</summary>
public record CatalogoCotizacionDto(
    IReadOnlyList<BodegaDto> Bodegas,
    IReadOnlyList<FormaPagoDto> FormasPago,
    IReadOnlyList<CampoMasDatosDto> CamposMasDatos,
    EstadoCatalogoWODto Estado);
