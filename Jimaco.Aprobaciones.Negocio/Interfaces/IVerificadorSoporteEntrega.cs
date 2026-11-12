namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Revisión con IA del soporte de entrega (foto/escaneo de la remisión firmada).</summary>
public interface IVerificadorSoporteEntrega
{
    /// <summary>false = no hay clave configurada o está apagado: solo se exige el QR.</summary>
    bool Activo { get; }

    Task<ResultadoVerificacionIA> VerificarAsync(byte[] contenido, string? contentType, CancellationToken ct = default);
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
