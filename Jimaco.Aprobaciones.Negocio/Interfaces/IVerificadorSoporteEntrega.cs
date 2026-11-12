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
    string? Error)
{
    public static ResultadoVerificacionIA NoAnalizado(string error) =>
        new(false, null, false, false, false, false, "baja", null, error);
}
