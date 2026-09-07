using Jimaco.Aprobaciones.Modelo.Entidades;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface IJwtGenerador
{
    string GenerarToken(Usuario usuario, IReadOnlyList<string> roles);

    /// <summary>
    /// Token acotado para los links de "aprobar/rechazar desde el correo": autentica a
    /// <paramref name="usuarioId"/> pero SOLO para actuar sobre <paramref name="instanciaDocumentoId"/>
    /// puntual (lleva un claim "purpose"="correo-accion" + "doc"=id que el filtro de autorización de
    /// la Api verifica contra la ruta) — a diferencia del token de sesión normal, no sirve para nada
    /// más aunque se filtre o se reenvíe el correo.
    /// </summary>
    string GenerarTokenAccionCorreo(int usuarioId, int instanciaDocumentoId, TimeSpan vigencia);
}
