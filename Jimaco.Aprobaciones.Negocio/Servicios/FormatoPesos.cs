using System.Globalization;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Montos en pesos para los correos: miles con punto (1.094.315.974), como se escribe en Colombia.
/// "N0" a secas depende de la cultura del servidor, y el contenedor corre con cultura invariante
/// (salía 1,094,315,974) — por eso un formato fijo, sin depender de que el sistema tenga "es-CO".
/// </summary>
internal static class FormatoPesos
{
    private static readonly NumberFormatInfo Colombia = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
        NegativeSign = "-"
    };

    /// <summary>1094315974.4 → "1.094.315.974" (sin decimales, redondeado).</summary>
    public static string Miles(decimal valor) => valor.ToString("N0", Colombia);
}
