using Jimaco.Aprobaciones.Negocio.Conciliacion;
using Jimaco.Aprobaciones.Negocio.DTOs;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>Un correo de la bandeja con los XML que traía adjuntos (uno por ZIP normalmente).</summary>
public record CorreoConFacturas(UniqueId Uid, IReadOnlyList<FacturaXmlEntradaDto> Xmls);

/// <summary>
/// Lee la bandeja de facturacion@jimaco.com.co por IMAP y saca los XML de los ZIP adjuntos.
/// NO marca nada como leído: esa bandeja la usa
/// Contabilidad a mano todos los días (y el estado leído/no leído es suyo). En vez de eso se lleva
/// la marca del último UID procesado (<see cref="MarcaCorreoStore"/>) y la API descarta duplicados
/// por CUFE. La carpeta se abre en solo lectura y MailKit descarga con BODY.PEEK, así que leer no
/// cambia nada en la bandeja.
///
/// 2026-11-24 (carga inicial de Jimaco, ~10.000 correos): se recorre por tandas. De cada tanda se
/// baja primero solo la ESTRUCTURA de los correos y después únicamente los adjuntos ZIP/XML (no los
/// PDF sueltos ni los correos sin adjuntos); al terminar cada tanda se llama a <c>alTerminarTanda</c>
/// (que sube y guarda la marca), así un corte del servidor IMAP no hace perder lo ya leído. Si el
/// servidor corta, se reconecta y se sigue desde el último correo procesado.
/// </summary>
public class CorreoFacturasWatcher(string host, int puerto, string usuario, string password, string? carpeta = null)
{
    private const int CorreosPorTanda = 50;
    private const int ReintentosPorCorte = 3;

    public async Task<int> RecorrerAsync(
        uint? uidValidityAnterior, uint ultimoUid, DateOnly desde, int maximo,
        Func<uint, IReadOnlyList<CorreoConFacturas>, Task> alTerminarTanda, Action<string> log, CancellationToken ct = default)
    {
        var procesados = 0;
        var cortes = 0;
        while (true)
        {
            using var cliente = new ImapClient();
            try
            {
                var bandeja = await AbrirAsync(cliente, ct);

                // Si el servidor renumeró la carpeta (UIDVALIDITY cambió) la marca no sirve: se
                // arranca de cero, siempre filtrando por fecha. Repetir correos no es problema (la
                // API deduplica por CUFE).
                if (uidValidityAnterior != bandeja.UidValidity)
                    ultimoUid = 0;
                uidValidityAnterior = bandeja.UidValidity;

                // Fecha del correo (encabezado Date, SENTSINCE): los correos
                // viejos importados o movidos tienen UID alto pero fecha vieja y no interesan.
                var desdeFecha = desde.ToDateTime(TimeOnly.MinValue).AddDays(-1);
                SearchQuery filtro = SearchQuery.SentSince(desdeFecha);
                if (ultimoUid > 0)
                    filtro = filtro.And(SearchQuery.Uids(new UniqueIdRange(new UniqueId(ultimoUid + 1), UniqueId.MaxValue)));

                var uids = (await bandeja.SearchAsync(filtro, ct))
                    .Where(u => u.Id > ultimoUid).OrderBy(u => u.Id).Take(maximo - procesados).ToList();
                if (uids.Count == 0)
                {
                    await cliente.DisconnectAsync(true, ct);
                    return procesados;
                }
                log($"  {uids.Count} correo(s) por revisar (UID {uids[0].Id} a {uids[^1].Id})...");

                foreach (var tanda in uids.Chunk(CorreosPorTanda))
                {
                    var resumenes = await bandeja.FetchAsync(tanda,
                        MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.BodyStructure, ct);

                    var correos = new List<CorreoConFacturas>();
                    foreach (var resumen in resumenes.OrderBy(r => r.UniqueId.Id))
                    {
                        var xmls = new List<FacturaXmlEntradaDto>();
                        var asunto = resumen.Envelope?.Subject;
                        var fecha = resumen.Envelope?.Date?.UtcDateTime;
                        foreach (var parte in (resumen.BodyParts ?? []).Where(EsZipOXml))
                        {
                            if (await bandeja.GetBodyPartAsync(resumen.UniqueId, parte, ct) is not MimePart adjunto || adjunto.Content is null)
                                continue;
                            using var contenido = new MemoryStream();
                            adjunto.Content.DecodeTo(contenido);
                            contenido.Position = 0;
                            ExtraerDeArchivo(adjunto.FileName ?? parte.FileName ?? "", contenido, asunto, fecha, xmls);
                        }
                        correos.Add(new CorreoConFacturas(resumen.UniqueId, xmls));
                    }

                    var ultimoDeTanda = tanda.Max(u => u.Id);
                    await alTerminarTanda(bandeja.UidValidity, correos);
                    ultimoUid = ultimoDeTanda;
                    procesados += tanda.Length;
                    cortes = 0;
                }

                await cliente.DisconnectAsync(true, ct);
                return procesados;
            }
            catch (Exception ex) when (EsCorteDeConexion(ex) && ++cortes <= ReintentosPorCorte)
            {
                log($"  El servidor de correo cortó la conexión ({ex.Message.Split('\n')[0]}); reconectando ({cortes}/{ReintentosPorCorte})...");
                await Task.Delay(TimeSpan.FromSeconds(10 * cortes), ct);
            }
        }
    }

    private async Task<IMailFolder> AbrirAsync(ImapClient cliente, CancellationToken ct)
    {
        await cliente.ConnectAsync(host, puerto, SecureSocketOptions.SslOnConnect, ct);
        await cliente.AuthenticateAsync(usuario, password, ct);
        var bandeja = string.IsNullOrWhiteSpace(carpeta) ? cliente.Inbox : await cliente.GetFolderAsync(carpeta, ct);
        await bandeja.OpenAsync(FolderAccess.ReadOnly, ct);
        return bandeja;
    }

    private static bool EsCorteDeConexion(Exception ex)
        => ex is IOException or ImapProtocolException or ServiceNotConnectedException or System.Net.Sockets.SocketException or TimeoutException
           || (ex is OperationCanceledException && ex.InnerException is TimeoutException);

    // Los proveedores mandan un ZIP con XML + PDF; algunos el XML suelto. Se decide por el nombre del
    // archivo o por el tipo, sin bajar el adjunto.
    internal static bool EsZipOXml(BodyPartBasic parte)
    {
        var nombre = parte.FileName ?? parte.ContentType?.Name ?? "";
        if (nombre.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || nombre.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return true;
        var tipo = parte.ContentType?.MimeType ?? "";
        return tipo.Contains("zip", StringComparison.OrdinalIgnoreCase)
               || tipo.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
               || (tipo.Equals("text/xml", StringComparison.OrdinalIgnoreCase) && parte.IsAttachment);
    }

    // Usado por la carga desde carpeta (.eml guardados).
    internal static List<FacturaXmlEntradaDto> ExtraerXmls(MimeMessage mensaje)
    {
        var xmls = new List<FacturaXmlEntradaDto>();
        var fecha = mensaje.Date == DateTimeOffset.MinValue ? (DateTime?)null : mensaje.Date.UtcDateTime;

        foreach (var adjunto in mensaje.Attachments.OfType<MimePart>().Where(a => a.Content is not null))
        {
            using var contenido = new MemoryStream();
            adjunto.Content!.DecodeTo(contenido);
            contenido.Position = 0;
            ExtraerDeArchivo(adjunto.FileName ?? "", contenido, mensaje.Subject, fecha, xmls);
        }

        return xmls;
    }

    // Un XML suelto o un ZIP (que puede traer otros ZIP adentro, como el "Descargar todo" de
    // Outlook, que empaca los ZIP de los proveedores en uno solo). La lectura es la misma que usa la
    // API para los ZIP de la DIAN que se suben desde la pantalla.
    internal static void ExtraerDeArchivo(string nombre, Stream contenido, string? asunto, DateTime? fecha, List<FacturaXmlEntradaDto> xmls)
    {
        var avisos = new List<string>();
        ExtractorXmlFacturas.Extraer(nombre, contenido, asunto, fecha, xmls, avisos);
        foreach (var aviso in avisos)
            Console.WriteLine($"[{DateTime.Now:s}]   {aviso} (\"{asunto}\")");
    }
}
