using System.Text.Json;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Utilidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Arma y despacha las notificaciones. Por ahora solo el canal Email está realmente conectado
/// a un proveedor (<see cref="IEmailSender"/>); WhatsApp queda pendiente (ver CLAUDE.md) — igual
/// se deja creada la fila en <see cref="Notificacion"/> por completitud del historial, pero queda
/// en estado Pendiente sin que nada la despache.
/// </summary>
public class NotificacionService(
    AppDbContext db,
    IEmailSender emailSender,
    IJwtGenerador jwtGenerador,
    IConfiguration configuration,
    TimeProvider timeProvider,
    IAlmacenamientoArchivos almacenamiento,
    ILogger<NotificacionService> logger)
    : INotificacionService
{
    public async Task NotificarPasoAsync(int instanciaDocumentoId, int pasoFlujoId, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct);
        if (instancia is null) return;

        var paso = await db.PasosFlujo.FirstOrDefaultAsync(p => p.Id == pasoFlujoId, ct);
        if (paso is null) return;

        var destinatarios = await db.UsuarioRoles
            .Where(ur => db.PasoFlujoRoles.Any(pr => pr.PasoFlujoId == pasoFlujoId && pr.RolId == ur.RolId))
            .Select(ur => ur.Usuario)
            .Where(u => u.Activo)
            .Distinct()
            .ToListAsync(ct);

        var frontendBaseUrl = (configuration["App:FrontendBaseUrl"] ?? "").TrimEnd('/');
        var vigenciaDias = int.TryParse(configuration["Jwt:VigenciaAccionCorreoDias"], out var d) ? d : 7;

        var asunto = $"[Jimaco Aprobaciones] {instancia.TipoDocumento.Nombre} {instancia.NumeroReferencia} pendiente de tu aprobación";

        // Campo libre de WO (observacionesAdicionales, ver Sincronizador/Program.cs) — se muestra en
        // el correo solo si el documento lo trae, igual criterio que en el PDF (ver
        // DocumentoPdfGenerador, que no pinta la caja si no hay nada).
        var observacionesAdicionales = ObtenerObservacionesAdicionales(instancia.DatosJson);
        var filaObservaciones = string.IsNullOrWhiteSpace(observacionesAdicionales)
            ? ""
            : $"<li>Observaciones adicionales: {observacionesAdicionales}</li>";

        foreach (var usuario in destinatarios)
        {
            // Token acotado a este usuario + este documento puntual (ver GenerarTokenAccionCorreo) —
            // permite ver el PDF y aprobar/rechazar con comentario directamente desde el correo, sin
            // loguearse, pero no sirve para nada más si el correo se reenvía o se filtra.
            var token = jwtGenerador.GenerarTokenAccionCorreo(usuario.Id, instancia.Id, pasoFlujoId, TimeSpan.FromDays(vigenciaDias));
            var linkAccion = $"{frontendBaseUrl}/accion-correo/{instancia.Id}?token={Uri.EscapeDataString(token)}";

            var cuerpo = $"""
                <p>Tenés un documento pendiente en el paso <strong>{paso.Nombre}</strong>:</p>
                <ul>
                  <li>Tipo: {instancia.TipoDocumento.Nombre}</li>
                  <li>Referencia: {instancia.NumeroReferencia}</li>
                  <li>Proveedor: {instancia.Proveedor}</li>
                  <li>Valor: {instancia.Valor}</li>
                  {filaObservaciones}
                </ul>
                <p><a href="{linkAccion}">Ver el documento y aprobar/rechazar</a></p>
                <p style="color:#666;font-size:12px">Este link es personal, vence en {vigenciaDias} días y no
                requiere iniciar sesión. Si preferís, también podés entrar al sistema normalmente.</p>
                """;

            await CrearYEnviarAsync(instancia.Id, usuario, asunto, cuerpo, ct);
        }
    }

    /// <summary>
    /// Extrae "observacionesAdicionales" del DatosJson del documento — campo libre de WO
    /// (Personalizado4 en una OC, ver esquema-worldoffice-oc.md), null si el documento no lo trae
    /// (la mayoría de los históricos, antes de este campo) o si DatosJson es null.
    /// </summary>
    private static string? ObtenerObservacionesAdicionales(string? datosJson)
    {
        if (string.IsNullOrWhiteSpace(datosJson)) return null;
        var datos = JsonSerializer.Deserialize<Dictionary<string, string>>(datosJson);
        return datos?.GetValueOrDefault("observacionesAdicionales");
    }

    public async Task NotificarConflictoComprobanteEgresoAsync(int instanciaDocumentoId, int pasoFlujoId, string numeroSolicitado, string mensajeConflicto, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct);
        if (instancia is null) return;

        var paso = await db.PasosFlujo.FirstOrDefaultAsync(p => p.Id == pasoFlujoId, ct);
        if (paso is null) return;

        var destinatarios = await db.UsuarioRoles
            .Where(ur => db.PasoFlujoRoles.Any(pr => pr.PasoFlujoId == pasoFlujoId && pr.RolId == ur.RolId))
            .Select(ur => ur.Usuario)
            .Where(u => u.Activo)
            .Distinct()
            .ToListAsync(ct);

        var frontendBaseUrl = (configuration["App:FrontendBaseUrl"] ?? "").TrimEnd('/');
        var vigenciaDias = int.TryParse(configuration["Jwt:VigenciaAccionCorreoDias"], out var d) ? d : 7;
        var desc = $"{instancia.TipoDocumento.Nombre} {instancia.NumeroReferencia}";

        var asunto = $"[Jimaco Aprobaciones] No se encontró el Comprobante de Egreso de {desc}";

        foreach (var usuario in destinatarios)
        {
            // Mismo token de siempre, acotado a ESTE paso (el de quien recibe el aviso) — así puede
            // entrar y devolver el documento a Contabilidad directamente desde el correo si hace
            // falta corregir el número.
            var token = jwtGenerador.GenerarTokenAccionCorreo(usuario.Id, instancia.Id, pasoFlujoId, TimeSpan.FromDays(vigenciaDias));
            var linkAccion = $"{frontendBaseUrl}/accion-correo/{instancia.Id}?token={Uri.EscapeDataString(token)}";

            var cuerpo = $"""
                <p>Contabilidad ingresó el número de Comprobante de Egreso <strong>{numeroSolicitado}</strong> para
                {desc}, pero el Sincronizador no lo pudo encontrar en World Office:</p>
                <p style="color:#a33">{mensajeConflicto}</p>
                <p>El documento sigue esperando en tu paso (<strong>{paso.Nombre}</strong>) sin el comprobante
                adjunto. Si hace falta, podés devolverlo a Contabilidad para que corrija el número.</p>
                <p><a href="{linkAccion}">Ver el documento</a></p>
                <p style="color:#666;font-size:12px">Este link es personal, vence en {vigenciaDias} días y no
                requiere iniciar sesión. Si preferís, también podés entrar al sistema normalmente.</p>
                """;

            await CrearYEnviarAsync(instancia.Id, usuario, asunto, cuerpo, ct);
        }
    }

    public async Task NotificarComprobanteEgresoNoUtilizadoAsync(int instanciaDocumentoId, int pasoFlujoContabilidadId, string numeroComprobante, string motivoRedireccion, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct);
        if (instancia is null) return;

        var destinatarios = await db.UsuarioRoles
            .Where(ur => db.PasoFlujoRoles.Any(pr => pr.PasoFlujoId == pasoFlujoContabilidadId && pr.RolId == ur.RolId))
            .Select(ur => ur.Usuario)
            .Where(u => u.Activo)
            .Distinct()
            .ToListAsync(ct);

        var desc = $"{instancia.TipoDocumento.Nombre} {instancia.NumeroReferencia}";
        var asunto = $"[Jimaco Aprobaciones] El Comprobante de Egreso {numeroComprobante} de {desc} no se va a utilizar";

        var cuerpo = $"""
            <p>{desc} fue redirigida a pago en efectivo — el Comprobante de Egreso
            <strong>{numeroComprobante}</strong> que ya generaste para la transferencia NO se va a
            utilizar.</p>
            <p>Motivo: {motivoRedireccion}</p>
            <p style="color:#666;font-size:12px">Este es solo un aviso informativo, no requiere ninguna acción de tu parte.</p>
            """;

        foreach (var usuario in destinatarios)
            await CrearYEnviarAsync(instancia.Id, usuario, asunto, cuerpo, ct);
    }

    public async Task NotificarHojaAlistamientoAtrasadaAsync(int documentoVinculadoId, CancellationToken ct = default)
    {
        var hoja = await db.DocumentosVinculados
            .Include(d => d.InstanciaDocumento).ThenInclude(i => i!.TipoDocumento)
            .FirstOrDefaultAsync(d => d.Id == documentoVinculadoId, ct);
        if (hoja is null) return;

        var instanciaIdsDelGrupo = await db.DocumentosVinculados
            .Where(d => d.PrefijoOrigen == hoja.PrefijoOrigen && d.NumeroReferencia == hoja.NumeroReferencia && d.InstanciaDocumentoId != null)
            .Select(d => d.InstanciaDocumentoId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var destinatarios = await ObtenerDestinatariosHojaAsync(hoja, instanciaIdsDelGrupo, ct);

        var frontendBaseUrl = (configuration["App:FrontendBaseUrl"] ?? "").TrimEnd('/');
        var desc = hoja.InstanciaDocumento is { } instanciaAtrasada
            ? $"{instanciaAtrasada.TipoDocumento?.Nombre ?? "Orden de Compra"} {instanciaAtrasada.NumeroReferencia}"
            : $"Cotización {hoja.NumeroReferencia}";
        var horas = (int)(timeProvider.GetUtcNow().UtcDateTime - hoja.FechaCreacion).TotalHours;

        var asunto = $"[Jimaco Aprobaciones] Hoja de Alistamiento {hoja.NumeroReferencia} de {desc} sigue incompleta";
        var cuerpo = $"""
            <p>La Hoja de Alistamiento <strong>{hoja.NumeroReferencia}</strong> (de {desc}
            {(hoja.Cliente is null ? "" : $", cliente {hoja.Cliente}")}) lleva <strong>{horas} horas</strong>
            generada sin completarse.</p>
            <p><a href="{frontendBaseUrl}/alistamiento">Ver el dashboard de alistamiento</a></p>
            """;

        foreach (var usuario in destinatarios)
            await CrearYEnviarAsync(hoja.InstanciaDocumentoId, usuario, asunto, cuerpo, ct);
    }

    public async Task NotificarHojaCompletaYRemisionadaAsync(int documentoVinculadoId, IReadOnlyList<int> instanciaDocumentoIdsDelGrupo, CancellationToken ct = default)
    {
        var hoja = await db.DocumentosVinculados
            .Include(d => d.InstanciaDocumento).ThenInclude(i => i!.TipoDocumento)
            .Include(d => d.Renglones).ThenInclude(r => r.DespachosRemision)
            .FirstOrDefaultAsync(d => d.Id == documentoVinculadoId, ct);
        if (hoja is null) return;

        var destinatarios = await ObtenerDestinatariosHojaAsync(hoja, instanciaDocumentoIdsDelGrupo, ct);

        var frontendBaseUrl = (configuration["App:FrontendBaseUrl"] ?? "").TrimEnd('/');
        var desc = hoja.InstanciaDocumento is { } instanciaCompleta
            ? $"{instanciaCompleta.TipoDocumento?.Nombre ?? "Orden de Compra"} {instanciaCompleta.NumeroReferencia}"
            : $"Cotización {hoja.NumeroReferencia}";

        // Un mismo ítem puede llegar repartido en varias Remisiones parciales (2026-10-26) — se
        // listan todas las que aportaron a esta hoja, no un solo número.
        var numerosRemision = hoja.Renglones.SelectMany(r => r.DespachosRemision).Select(x => x.NumeroRemision).Distinct().ToList();
        var textoRemision = numerosRemision.Count == 0
            ? "y fue remisionada"
            : $"y fue remisionada (Remisión{(numerosRemision.Count > 1 ? "es" : "")} {string.Join(", ", numerosRemision)})";

        var asunto = $"[Jimaco Aprobaciones] Hoja de Alistamiento {hoja.NumeroReferencia} completa y remisionada";
        var cuerpo = $"""
            <p>La Hoja de Alistamiento <strong>{hoja.NumeroReferencia}</strong> (de {desc}
            {(hoja.Cliente is null ? "" : $", cliente {hoja.Cliente}")}) ya está completa en Alistamiento
            {textoRemision} en World Office.</p>
            <p><a href="{frontendBaseUrl}/alistamiento">Ver el dashboard de alistamiento</a></p>
            """;

        foreach (var usuario in destinatarios)
            await CrearYEnviarAsync(hoja.InstanciaDocumentoId, usuario, asunto, cuerpo, ct);
    }

    /// <summary>
    /// Destinatarios de los dos avisos de Hoja de Alistamiento (2026-11-04, pedido del usuario: antes
    /// iban a todos los roles con acceso al tablero, y cada vendedora recibía las hojas de todas):
    /// Logística (roles con PuedeGestionarAlistamiento) + quien elaboró la Cotización, resuelta por
    /// <see cref="Usuario.NombresCompletosWO"/>. Si nadie tiene ese nombre configurado, se usa a quien
    /// mandó cada OC del grupo a aprobar (SolicitanteWO). PuedeVerAlistamiento ya no da correos, solo
    /// acceso al tablero.
    /// </summary>
    private async Task<List<Usuario>> ObtenerDestinatariosHojaAsync(DocumentoVinculado hoja, IReadOnlyCollection<int> instanciaIdsDelGrupo, CancellationToken ct)
    {
        var logistica = await db.UsuarioRoles
            .Where(ur => ur.Rol.Activo && ur.Rol.PuedeGestionarAlistamiento && ur.Usuario.Activo)
            .Select(ur => ur.Usuario)
            .Distinct()
            .ToListAsync(ct);

        var conNombreWO = await db.Usuarios.Where(u => u.Activo && u.NombresCompletosWO != null).ToListAsync(ct);
        var emisores = conNombreWO.Where(u => NombresCompletosWO.Contiene(u.NombresCompletosWO, hoja.ElaboradoPor)).ToList();

        if (emisores.Count == 0)
        {
            var solicitantes = await db.InstanciasDocumento
                .Where(i => instanciaIdsDelGrupo.Contains(i.Id) && i.SolicitanteWO != null)
                .Select(i => i.SolicitanteWO)
                .Distinct()
                .ToListAsync(ct);
            emisores = await db.Usuarios.Where(u => u.Activo && solicitantes.Contains(u.UsuarioWO)).ToListAsync(ct);

            if (emisores.Count == 0)
                logger.LogWarning("Hoja de Alistamiento {Hoja}: ningún usuario corresponde a \"{ElaboradoPor}\" — el aviso solo le llega a Logística.",
                    hoja.NumeroReferencia, hoja.ElaboradoPor);
        }

        return logistica.Concat(emisores).DistinctBy(u => u.Id).ToList();
    }

    public async Task NotificarPagoRealizadoAsync(int instanciaDocumentoId, int pasoFlujoContabilidadId, int soportePagoAdjuntoId, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct);
        if (instancia is null) return;

        var adjunto = await db.Adjuntos.FirstOrDefaultAsync(a => a.Id == soportePagoAdjuntoId, ct);

        IReadOnlyList<AdjuntoCorreo>? adjuntosCorreo = null;
        if (adjunto is not null)
        {
            try
            {
                await using var stream = await almacenamiento.AbrirAsync(adjunto.RutaArchivo, ct);
                using var memoria = new MemoryStream();
                await stream.CopyToAsync(memoria, ct);
                adjuntosCorreo = [new AdjuntoCorreo(adjunto.NombreArchivo, memoria.ToArray(), adjunto.ContentType)];
            }
            catch (Exception ex)
            {
                // Si por algo no se puede leer el archivo del disco, se manda el aviso igual, sin
                // adjunto — mejor que no avisar nada.
                logger.LogWarning(ex, "No se pudo leer el soporte de pago {AdjuntoId} para adjuntarlo al correo.", soportePagoAdjuntoId);
            }
        }

        var desc = $"{instancia.TipoDocumento.Nombre} {instancia.NumeroReferencia}";
        var asunto = $"[Jimaco Aprobaciones] {desc} fue pagada";
        var cuerpo = $"""
            <p>Gerencia General aprobó el pago de {desc} — la transferencia ya se realizó.</p>
            <p>Se adjunta el soporte de pago.</p>
            """;

        var contables = await db.UsuarioRoles
            .Where(ur => db.PasoFlujoRoles.Any(pr => pr.PasoFlujoId == pasoFlujoContabilidadId && pr.RolId == ur.RolId))
            .Select(ur => ur.Usuario)
            .Where(u => u.Activo)
            .Distinct()
            .ToListAsync(ct);
        foreach (var usuario in contables)
            await CrearYEnviarAsync(instancia.Id, usuario, asunto, cuerpo, ct, adjuntosCorreo);

        // Emisor real — mismo criterio que InstanciaDocumentoService.NotificarSolicitanteAsync:
        // requiere un Usuario registrado con el mismo UsuarioWO que mandó a aprobar en World Office.
        if (!string.IsNullOrWhiteSpace(instancia.SolicitanteWO))
        {
            var emisor = await db.Usuarios.FirstOrDefaultAsync(u => u.UsuarioWO == instancia.SolicitanteWO && u.Activo, ct);
            if (emisor is not null)
                await CrearYEnviarAsync(instancia.Id, emisor, asunto, cuerpo, ct, adjuntosCorreo);
        }
    }

    public async Task NotificarUsuarioAsync(int instanciaDocumentoId, int usuarioId, string asunto, string mensaje, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId && u.Activo, ct);
        if (usuario is null) return;

        await CrearYEnviarAsync(instanciaDocumentoId, usuario, asunto, $"<p>{mensaje}</p>", ct);
    }

    public async Task EnviarPruebaAsync(string destinatarioEmail, CancellationToken ct = default)
    {
        await emailSender.EnviarAsync(
            destinatarioEmail,
            "[Jimaco Aprobaciones] Correo de prueba",
            "<p>Si ves este correo, la configuración SMTP de Jimaco Aprobaciones funciona correctamente.</p>",
            ct: ct);
    }

    private async Task CrearYEnviarAsync(int? instanciaDocumentoId, Usuario usuario, string asunto, string cuerpoHtml, CancellationToken ct, IReadOnlyList<AdjuntoCorreo>? adjuntos = null)
    {
        var notificacion = new Notificacion
        {
            InstanciaDocumentoId = instanciaDocumentoId,
            UsuarioId = usuario.Id,
            Canal = CanalNotificacion.Email,
            Mensaje = asunto,
            Estado = EstadoNotificacion.Pendiente,
            FechaCreacion = timeProvider.GetUtcNow().UtcDateTime
        };
        db.Notificaciones.Add(notificacion);
        await db.SaveChangesAsync(ct);

        try
        {
            // Límite propio de 15s, NUNCA el "ct" de la acción que disparó este aviso (bug real
            // 2026-10-19: una cuenta de prueba con dominio inexistente colgó el envío SMTP hasta
            // que algo canceló el "ct" de la petición HTTP completa — eso tumbó TODO lo que venía
            // después en esa misma petición, incluida la respuesta de la acción de negocio que ya
            // había guardado bien sus cambios, y además impidió que se intentara notificar a los
            // demás destinatarios de ese mismo aviso). Enlazado a "ct" solo para cortar si el
            // llamador de verdad se cancela — un timeout acá nunca cancela "ct" de vuelta.
            using var limiteEnvio = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limiteEnvio.CancelAfter(TimeSpan.FromSeconds(15));
            await emailSender.EnviarAsync(usuario.Email, asunto, cuerpoHtml, adjuntos, limiteEnvio.Token);
            notificacion.Estado = EstadoNotificacion.Enviada;
        }
        catch (Exception ex)
        {
            // Un fallo de envío no debe tumbar la acción de negocio que lo disparó (aprobar,
            // devolver, etc.) — se registra como Fallida y sigue.
            notificacion.Estado = EstadoNotificacion.Fallida;
            logger.LogWarning(ex, "No se pudo enviar la notificación por correo a {Email}", usuario.Email);
        }
        finally
        {
            notificacion.FechaEnvio = timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
    }
}
