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
    /// <paramref name="pasoFlujoId"/> queda grabado como claim "paso" — es el paso que estaba
    /// ACTUAL en el momento de mandar el correo. Si para cuando alguien usa el link el documento ya
    /// avanzó de ese paso (esta misma persona ya actuó, o alguien más lo hizo por otro medio), el
    /// filtro de autorización lo rechaza (ver TokenCorreoActionFilter) — así un link de correo
    /// nunca puede reusarse para actuar dos veces ni actuar "tarde" sobre un paso que ya no es el
    /// suyo.
    /// </summary>
    string GenerarTokenAccionCorreo(int usuarioId, int instanciaDocumentoId, int pasoFlujoId, TimeSpan vigencia);
}
