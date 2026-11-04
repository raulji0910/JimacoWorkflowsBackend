namespace Jimaco.Aprobaciones.Negocio.Utilidades;

/// <summary>Lectura de <see cref="Modelo.Entidades.Usuario.NombresCompletosWO"/> (lista separada por ";").</summary>
public static class NombresCompletosWO
{
    public static IReadOnlyList<string> Separar(string? valor) =>
        string.IsNullOrWhiteSpace(valor)
            ? []
            : valor.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public static bool Contiene(string? valor, string? nombre) =>
        !string.IsNullOrWhiteSpace(nombre)
        && Separar(valor).Contains(nombre.Trim(), StringComparer.OrdinalIgnoreCase);
}
