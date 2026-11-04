using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>Un documento que World Office mandó a aprobar (correo real, con referencia extraída del cuerpo).</summary>
public record CorreoAprobacionRow(string Prefijo, decimal DocumentoNumero, UniqueId MensajeId);

/// <summary>
/// World Office (con un complemento propio, aparte del sistema nativo — no queda nada de esto
/// registrado en la base, ver CLAUDE.md "Detección de documentos nuevos: correo, no la base") manda
/// un correo cada vez que alguien aprieta "Solicitar Aprobación", a <c>sistemas@jimaco.com.co</c>.
/// Esta clase revisa esa bandeja por IMAP y extrae qué documento (prefijo + número) hay que
/// sincronizar — el correo es solo la señal, los datos reales siempre se leen de la base
/// (<see cref="WorldOfficeReader"/>), nunca se confía en el texto del correo para eso.
/// </summary>
public class CorreoAprobacionWatcher(string host, int puerto, string usuario, string password, string? carpeta = null)
{
    // Nombre completo de carpeta IMAP (ej. "INBOX.Aprobaciones") si un filtro del lado del
    // servidor mueve estos correos fuera de la Bandeja de entrada — null usa la Bandeja de
    // entrada normal. Configurable (Correo:Carpeta) porque quedó descubierto en producción que un
    // filtro server-side puede empezar a mover estos correos en cualquier momento sin que el
    // código se entere, dejando al Sincronizador mirando una carpeta que ya no recibe nada.
    private async Task<IMailFolder> AbrirCarpetaAsync(ImapClient cliente, CancellationToken ct)
    {
        var bandeja = string.IsNullOrWhiteSpace(carpeta) ? cliente.Inbox : await cliente.GetFolderAsync(carpeta, ct);
        await bandeja.OpenAsync(FolderAccess.ReadWrite, ct);
        return bandeja;
    }

    // "Documento: OC PRUEB - 2" -> el prefijo real de WO es el último token antes del " - "
    // (acá "OC" es una etiqueta fija del complemento para "Orden de Compra", no necesariamente el
    // prefijo real — con las OC reales todavía no vimos el formato exacto del correo, así que esta
    // expresión toma el ÚLTIMO token antes del separador a propósito, para no asumir que siempre
    // hay dos palabras).
    private static readonly Regex RegexDocumento = new(
        @"Documento:\s*.*?(?<prefijo>\S+)\s*-\s*(?<numero>\d+(?:[.,]\d+)?)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public async Task<IReadOnlyList<CorreoAprobacionRow>> ObtenerSolicitudesNuevasAsync(CancellationToken ct = default)
    {
        using var cliente = new ImapClient();
        await cliente.ConnectAsync(host, puerto, SecureSocketOptions.SslOnConnect, ct);
        await cliente.AuthenticateAsync(usuario, password, ct);

        var bandeja = await AbrirCarpetaAsync(cliente, ct);

        // Sin leer todavía Y con asunto "Aprobacion" — así no interfiere con el resto del correo
        // normal de esa bandeja (cotizaciones, avisos, etc.), que sigue usándose para otras cosas.
        var idsNoLeidos = await bandeja.SearchAsync(SearchQuery.NotSeen.And(SearchQuery.SubjectContains("Aprobacion")), ct);

        var resultado = new List<CorreoAprobacionRow>();
        foreach (var id in idsNoLeidos)
        {
            var mensaje = await bandeja.GetMessageAsync(id, ct);
            var cuerpo = mensaje.TextBody ?? mensaje.HtmlBody ?? "";
            var coincidencia = RegexDocumento.Match(cuerpo);

            if (!coincidencia.Success)
            {
                // No se pudo interpretar el correo — se deja SIN marcar como leído a propósito,
                // para que quede visible en la bandeja y alguien lo revise a mano, en vez de
                // perderlo silenciosamente.
                continue;
            }

            var prefijo = coincidencia.Groups["prefijo"].Value;
            var numero = decimal.Parse(coincidencia.Groups["numero"].Value.Replace(",", "."), System.Globalization.CultureInfo.InvariantCulture);
            resultado.Add(new CorreoAprobacionRow(prefijo, numero, id));
        }

        await cliente.DisconnectAsync(true, ct);
        return resultado;
    }

    /// <summary>Marca el correo como procesado (leído) — se llama recién después de confirmar que el documento ya se creó en Jimaco Aprobaciones.</summary>
    public async Task MarcarProcesadoAsync(UniqueId mensajeId, CancellationToken ct = default)
    {
        using var cliente = new ImapClient();
        await cliente.ConnectAsync(host, puerto, SecureSocketOptions.SslOnConnect, ct);
        await cliente.AuthenticateAsync(usuario, password, ct);

        var bandeja = await AbrirCarpetaAsync(cliente, ct);
        await bandeja.AddFlagsAsync(mensajeId, MessageFlags.Seen, true, ct);

        await cliente.DisconnectAsync(true, ct);
    }
}
