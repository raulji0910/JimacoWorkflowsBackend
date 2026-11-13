namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>
/// Revisión con IA de lo que se adjunta en Entregas: el soporte de entrega (remisión firmada o
/// sellada) y, en la fase 2, los documentos para facturar (OC, entrada de almacén, proforma...).
/// </summary>
public interface IVerificadorSoporteEntrega
{
    /// <summary>false = no hay clave configurada o está apagado: solo se exige el QR.</summary>
    bool Activo { get; }

    Task<ResultadoVerificacionIA> VerificarAsync(byte[] contenido, string? contentType, CancellationToken ct = default);

    /// <summary>¿El archivo es el documento pedido, del cliente de la remisión? Nunca lanza.</summary>
    Task<ResultadoDocumentoFacturacionIA> VerificarDocumentoAsync(byte[] contenido, string? contentType,
        ContextoDocumentoFacturacion contexto, CancellationToken ct = default);
}

public record ResultadoVerificacionIA(
    bool Analizado,
    string? NumeroLeido,
    bool EsRemisionJimaco,
    bool HayFirma,
    bool HayNombreOCedula,
    bool Legible,
    string Confianza,
    string? Observacion,
    string? Error,
    /// <summary>Sello del cliente (de su empresa) — vale igual que la firma (pedido 2026-11-12).</summary>
    bool HaySello = false)
{
    /// <summary>La hoja trae la firma o el sello del cliente: cualquiera de los dos sirve.</summary>
    public bool HayFirmaOSello => HayFirma || HaySello;

    public static ResultadoVerificacionIA NoAnalizado(string error) =>
        new(false, null, false, false, false, false, "baja", null, error);
}

/// <summary>Lo que se le dice a la IA del documento que se espera y de la remisión.</summary>
public record ContextoDocumentoFacturacion(
    string TipoEsperado,
    string? DescripcionTipo,
    string NumeroRemision,
    string? Cliente,
    string? NitCliente,
    string? OrdenCompraCliente);

public record ResultadoDocumentoFacturacionIA(
    bool Analizado,
    bool EsTipoEsperado,
    string? TipoDetectado,
    /// <summary>null = el documento no muestra cliente/NIT legible.</summary>
    bool? ClienteCoincide,
    /// <summary>Número de orden de compra que se lee en el documento, si hay.</summary>
    string? NumeroOrdenCompraLeido,
    bool Legible,
    string? Observacion,
    string? Error)
{
    public static ResultadoDocumentoFacturacionIA NoAnalizado(string error) =>
        new(false, false, null, null, null, false, null, error);
}
