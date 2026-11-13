namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Empresa del grupo dueña de un documento (2026-11-13). Jimaco maneja la logística y las
/// aprobaciones de FERREALIADOS JV en esta misma aplicación (mismos aprobadores, un solo acceso),
/// pero cada empresa tiene su propia base de World Office — así que una OC 1503 de Ferrealiados y
/// una OC 1503 de Jimaco son documentos distintos. Todo lo anterior a esta fecha es Jimaco (0).
/// Cada Sincronizador declara su empresa con el encabezado HTTP <c>X-Empresa</c> (sin encabezado =
/// Jimaco, para que el exe viejo siga funcionando igual).
/// </summary>
public enum Empresa
{
    Jimaco = 0,
    Ferrealiados = 1
}

public static class EmpresaExtensions
{
    public static string Nombre(this Empresa empresa) => empresa switch
    {
        Empresa.Ferrealiados => "Ferrealiados",
        _ => "Jimaco"
    };

    /// <summary>"Ferrealiados"/"1" → Ferrealiados; vacío o cualquier otra cosa → Jimaco.</summary>
    public static Empresa Parsear(string? texto) =>
        Enum.TryParse<Empresa>(texto?.Trim(), ignoreCase: true, out var e) && Enum.IsDefined(e) ? e : Empresa.Jimaco;
}
