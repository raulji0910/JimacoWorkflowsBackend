namespace Jimaco.Aprobaciones.Negocio.DTOs;

public record ConfiguracionDocumentosWODto(string PrefijoCotizacion, bool CrearCotizacionesEnWorldOffice, DateTime? FechaActualizacion, string? ActualizadoPor,
    // Para que el administrador vea si hay algo esperando (p. ej. con la creación apagada).
    int CotizacionesEnCola = 0);

public record ActualizarConfiguracionDocumentosWODto(string PrefijoCotizacion, bool CrearCotizacionesEnWorldOffice);
