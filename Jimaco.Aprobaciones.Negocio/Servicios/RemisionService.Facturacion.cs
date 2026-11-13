using System.Text;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Entregas fase 2 (2026-11-13): de la remisión entregada a la factura de venta.
/// <list type="number">
/// <item>Logística confirma la entrega → correo al emisor (quien la elaboró en WO, por su Usuario WO).</item>
/// <item>El emisor adjunta un archivo por cada tipo documental que aplica al cliente (OC, entrada de
/// almacén y, solo para PARES/VERANO, proforma). Con la IA encendida se revisa cada archivo y, si no
/// parece el documento pedido, queda un AVISO que no bloquea (decisión del usuario).</item>
/// <item>Con todos los obligatorios, el emisor la envía a contabilidad → correo a los contables
/// (rol con PuedeFacturarEntregas).</item>
/// <item>El contable puede devolverla con un comentario → correo al emisor, que la vuelve a tramitar.</item>
/// </list>
/// El cierre ("Facturada") queda para cuando se sepa cómo WO liga la factura con las remisiones.
/// Sin recordatorios por correo: las alertas del dashboard bastan (pedido del usuario).
/// </summary>
public partial class RemisionService
{
    private static readonly TimeSpan HoraColombia = TimeSpan.FromHours(-5);

    // ---------------------------------------------------------------------------------------------
    // Documentos
    // ---------------------------------------------------------------------------------------------

    public async Task<RemisionDetalleDto> AdjuntarDocumentoAsync(int id, int usuarioId, int tipoDocumentoId,
        ArchivoSoporteEntrega archivo, CancellationToken ct = default)
    {
        var permisos = await PermisosAsync(usuarioId, ct);
        var remision = await CargarAsync(id, ct);
        ExigirDocumentar(permisos, remision);

        var tipo = (await TiposActivosAsync(ct)).FirstOrDefault(t => t.Id == tipoDocumentoId)
            ?? throw new InvalidOperationException("Ese tipo de documento no existe o está inactivo.");
        if (!TiposQueAplican([tipo], remision.NitCliente).Any())
            throw new InvalidOperationException($"\"{tipo.Nombre}\" no se pide para el cliente {remision.Cliente}.");

        var bytes = await LeerAsync(archivo.Contenido, ct);
        if (bytes.Length == 0)
            throw new InvalidOperationException("El archivo está vacío.");

        string? aviso = null, tipoDetectado = null, observacion = null;
        if (await IaEncendidaAsync(ct))
        {
            var ia = await verificadorIA.VerificarDocumentoAsync(bytes, archivo.ContentType, new(
                tipo.Nombre, tipo.Descripcion, remision.Numero, remision.Cliente, remision.NitCliente, remision.OrdenCompraCliente), ct);
            (aviso, tipoDetectado, observacion) = AvisoDocumento(ia, tipo, remision);
        }

        using var copia = new MemoryStream(bytes, writable: false);
        var ruta = await almacenamiento.GuardarAsync($"remision-{remision.Numero}-{tipo.Nombre}-{archivo.NombreArchivo}", copia, ct);
        remision.Soportes.Add(new SoporteRemision
        {
            Tipo = TipoSoporteRemision.DocumentoFacturacion,
            TipoDocumentoFacturacionId = tipo.Id,
            NombreArchivo = Recortar(archivo.NombreArchivo, 300) ?? "documento",
            RutaArchivo = ruta,
            ContentType = Recortar(archivo.ContentType, 150),
            TamanoBytes = bytes.LongLength,
            IaTipoDetectado = Recortar(tipoDetectado, 100),
            IaObservacion = Recortar(observacion, 500),
            Aviso = Recortar(aviso, 500),
            Valido = aviso is null,
            SubidoPorUsuarioId = usuarioId,
            FechaCarga = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        return await ObtenerAsync(id, usuarioId, ct);
    }

    /// <summary>
    /// Traduce lo que vio la IA a un aviso en español (null = todo cuadra). No bloquea nada.
    /// </summary>
    internal static (string? Aviso, string? TipoDetectado, string? Observacion) AvisoDocumento(
        Interfaces.ResultadoDocumentoFacturacionIA ia, TipoDocumentoFacturacion tipo, Remision remision)
    {
        if (!ia.Analizado)
            return (null, null, ia.Error is null ? null : $"Sin revisión con IA: {ia.Error}");

        string? aviso = null;
        if (!ia.Legible)
            aviso = "No se alcanza a leer bien el documento (borroso, oscuro o cortado).";
        else if (!ia.EsTipoEsperado)
            aviso = $"La IA no ve que sea {tipo.Nombre}" + (string.IsNullOrWhiteSpace(ia.TipoDetectado) ? "." : $": parece {ia.TipoDetectado}.");
        else if (!OrdenCompraCoincide(ia.NumeroOrdenCompraLeido, remision.OrdenCompraCliente))
            aviso = $"El número de orden de compra del documento ({ia.NumeroOrdenCompraLeido}) no coincide con el de la remisión ({remision.OrdenCompraCliente}).";
        else if (ia.ClienteCoincide == false)
            aviso = $"El documento no parece ser del cliente {remision.Cliente}.";

        return (aviso, ia.TipoDetectado, ia.Observacion);
    }

    /// <summary>Solo avisa si los DOS números existen y ninguno contiene al otro (formatos como "OC-2017383").</summary>
    private static bool OrdenCompraCoincide(string? leida, string? remision)
    {
        static string Limpio(string? s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var a = Limpio(leida);
        var b = Limpio(remision);
        if (a.Length == 0 || b.Length == 0) return true;
        return a.Contains(b) || b.Contains(a);
    }

    public async Task<RemisionDetalleDto> EliminarDocumentoAsync(int id, int soporteId, int usuarioId, CancellationToken ct = default)
    {
        var permisos = await PermisosAsync(usuarioId, ct);
        var remision = await CargarAsync(id, ct);
        ExigirDocumentar(permisos, remision);

        var soporte = remision.Soportes.FirstOrDefault(s => s.Id == soporteId && s.Tipo == TipoSoporteRemision.DocumentoFacturacion)
            ?? throw new KeyNotFoundException("Documento no encontrado.");
        // El archivo queda en el almacenamiento (no hay borrado físico); solo se quita de la remisión.
        db.SoportesRemision.Remove(soporte);
        await db.SaveChangesAsync(ct);

        return await ObtenerAsync(id, usuarioId, ct);
    }

    // ---------------------------------------------------------------------------------------------
    // Envío a contabilidad / devolución
    // ---------------------------------------------------------------------------------------------

    public async Task<RemisionDetalleDto> EnviarAFacturacionAsync(int id, int usuarioId, string? comentario, CancellationToken ct = default)
    {
        var permisos = await PermisosAsync(usuarioId, ct);
        var remision = await CargarAsync(id, ct);
        ExigirDocumentar(permisos, remision);

        var adjuntos = remision.Soportes.Where(s => s.Tipo == TipoSoporteRemision.DocumentoFacturacion).ToList();
        var faltan = TiposQueAplican(await TiposActivosAsync(ct), remision.NitCliente)
            .Where(t => t.Obligatorio && adjuntos.All(d => d.TipoDocumentoFacturacionId != t.Id))
            .Select(t => t.Nombre).ToList();
        if (faltan.Count > 0)
            throw new InvalidOperationException($"Faltan documentos para enviar a contabilidad: {string.Join(", ", faltan)}.");

        var texto = Recortar(comentario?.Trim(), 1000);
        remision.EstadoFacturacion = EstadoFacturacionRemision.EnFacturacion;
        remision.FechaEnvioFacturacion = DateTime.UtcNow;
        remision.EventosFacturacion.Add(new EventoFacturacionRemision
        {
            Accion = AccionFacturacionRemision.EnviadaAFacturacion,
            Comentario = texto,
            UsuarioId = usuarioId,
            Fecha = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        var contables = await db.UsuarioRoles.AsNoTracking()
            .Where(ur => ur.Rol.Activo && ur.Rol.PuedeFacturarEntregas && ur.Usuario.Activo)
            .Select(ur => ur.UsuarioId).Distinct().ToListAsync(ct);
        if (contables.Count == 0)
            logger.LogWarning("REM {Numero} enviada a facturación pero ningún usuario tiene el permiso de facturar entregas.", remision.Numero);

        var quien = await NombreUsuarioAsync(usuarioId, ct);
        var cuerpo = new StringBuilder()
            .Append($"<p>{H(quien)} envió la <strong>remisión {H(remision.Numero)}</strong> del cliente <strong>{H(remision.Cliente)}</strong> ")
            .Append("para facturar. Ya tiene adjuntos los documentos:</p><ul>");
        foreach (var d in adjuntos)
            cuerpo.Append($"<li>{H(d.TipoDocumentoFacturacion?.Nombre)}: {H(d.NombreArchivo)}{(d.Aviso is null ? "" : $" — <em>aviso: {H(d.Aviso)}</em>")}</li>");
        cuerpo.Append("</ul>");
        if (texto is not null) cuerpo.Append($"<p>Comentario: {H(texto)}</p>");

        foreach (var contable in contables)
            await NotificarSinRomperAsync(contable, $"[Jimaco Aprobaciones] REM {remision.Numero} lista para facturar ({remision.Cliente})",
                cuerpo.ToString(), remision.Id, ct);

        return await ObtenerAsync(id, usuarioId, ct);
    }

    public async Task<RemisionDetalleDto> DevolverAsync(int id, int usuarioId, string comentario, CancellationToken ct = default)
    {
        var permisos = await PermisosAsync(usuarioId, ct);
        if (!permisos.Facturar)
            throw new UnauthorizedAccessException("No tenés permiso para devolver remisiones de facturación.");
        var texto = Recortar(comentario?.Trim(), 1000)
            ?? throw new InvalidOperationException("Escribí qué falta o qué está mal para que el emisor lo corrija.");

        var remision = await CargarAsync(id, ct);
        if (remision.EstadoFacturacion != EstadoFacturacionRemision.EnFacturacion)
            throw new InvalidOperationException($"La REM {remision.Numero} no está en facturación.");

        remision.EstadoFacturacion = EstadoFacturacionRemision.Devuelta;
        remision.EventosFacturacion.Add(new EventoFacturacionRemision
        {
            Accion = AccionFacturacionRemision.Devuelta,
            Comentario = texto,
            UsuarioId = usuarioId,
            Fecha = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        var quien = await NombreUsuarioAsync(usuarioId, ct);
        var cuerpo = $"""
            <p>{H(quien)} (contabilidad) <strong>devolvió</strong> la remisión <strong>{H(remision.Numero)}</strong>
            del cliente <strong>{H(remision.Cliente)}</strong>:</p>
            <blockquote>{H(texto)}</blockquote>
            <p>Corregí o agregá los documentos y volvé a enviarla a contabilidad.</p>
            """;
        foreach (var emisorId in await EmisoresAsync(remision.ElaboradoPorWO, ct))
            await NotificarSinRomperAsync(emisorId, $"[Jimaco Aprobaciones] REM {remision.Numero} devuelta por contabilidad", cuerpo, remision.Id, ct);

        return await ObtenerAsync(id, usuarioId, ct);
    }

    /// <summary>Correo al emisor cuando Logística confirma la entrega (punto 1 del pedido).</summary>
    private async Task NotificarEntregaAlEmisorAsync(Remision remision, int entregadaPorId, CancellationToken ct)
    {
        try
        {
            var emisores = await EmisoresAsync(remision.ElaboradoPorWO, ct);
            if (emisores.Count == 0)
            {
                logger.LogWarning("REM {Numero} entregada: ningún usuario tiene Usuario WO \"{UsuarioWO}\" — no se avisó al emisor.",
                    remision.Numero, remision.ElaboradoPorWO);
                return;
            }

            var quien = await NombreUsuarioAsync(entregadaPorId, ct);
            var fecha = (remision.FechaEntrega ?? DateTime.UtcNow) + HoraColombia;
            var tipos = TiposQueAplican(await TiposActivosAsync(ct), remision.NitCliente).ToList();
            var cuerpo = new StringBuilder()
                .Append($"<p>La <strong>remisión {H(remision.Numero)}</strong> del cliente <strong>{H(remision.Cliente)}</strong> ")
                .Append($"fue <strong>entregada</strong> el {fecha:dd/MM/yyyy h:mm tt} (confirmó {H(quien)}).</p>");
            if (tipos.Count > 0)
            {
                cuerpo.Append("<p>Para que contabilidad pueda facturar, adjuntá:</p><ul>");
                foreach (var t in tipos) cuerpo.Append($"<li>{H(t.Nombre)}{(t.Obligatorio ? "" : " (opcional)")}</li>");
                cuerpo.Append("</ul>");
            }

            foreach (var emisorId in emisores)
                await NotificarSinRomperAsync(emisorId, $"[Jimaco Aprobaciones] REM {remision.Numero} entregada — adjuntá los documentos para facturar",
                    cuerpo.ToString(), remision.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La entrega ya quedó guardada: un aviso fallido no la deshace.
            logger.LogWarning(ex, "No se pudo avisar al emisor de la entrega de la REM {Numero}", remision.Numero);
        }
    }

    private async Task NotificarSinRomperAsync(int usuarioId, string asunto, string cuerpoHtml, int remisionId, CancellationToken ct)
    {
        try
        {
            await notificaciones.NotificarHtmlAsync(null, usuarioId, asunto, cuerpoHtml, $"/entregas/{remisionId}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo enviar el aviso de Entregas al usuario {UsuarioId}", usuarioId);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Tipos documentales (Administración)
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<TipoDocumentoFacturacionDto>> ListarTiposDocumentoAsync(CancellationToken ct = default) =>
        await db.TiposDocumentoFacturacion.AsNoTracking().OrderBy(t => t.Orden).ThenBy(t => t.Id)
            .Select(t => new TipoDocumentoFacturacionDto(t.Id, t.Nombre, t.Descripcion, t.Obligatorio, t.NitsClientes, t.Orden, t.Activo))
            .ToListAsync(ct);

    public async Task<TipoDocumentoFacturacionDto> GuardarTipoDocumentoAsync(int? id, GuardarTipoDocumentoFacturacionDto dto, CancellationToken ct = default)
    {
        var nombre = Recortar(dto.Nombre?.Trim(), 100) ?? throw new InvalidOperationException("Escribí el nombre del documento.");
        var nits = NormalizarNits(dto.NitsClientes);

        TipoDocumentoFacturacion tipo;
        if (id is int existente)
        {
            tipo = await db.TiposDocumentoFacturacion.FirstOrDefaultAsync(t => t.Id == existente, ct)
                ?? throw new KeyNotFoundException("Tipo de documento no encontrado.");
        }
        else
        {
            tipo = new TipoDocumentoFacturacion { Nombre = nombre };
            db.TiposDocumentoFacturacion.Add(tipo);
        }
        if (await db.TiposDocumentoFacturacion.AnyAsync(t => t.Id != tipo.Id && t.Nombre == nombre, ct))
            throw new InvalidOperationException($"Ya existe un documento llamado \"{nombre}\".");

        tipo.Nombre = nombre;
        tipo.Descripcion = Recortar(dto.Descripcion?.Trim(), 500);
        tipo.Obligatorio = dto.Obligatorio;
        tipo.NitsClientes = nits;
        tipo.Orden = dto.Orden;
        tipo.Activo = dto.Activo;
        await db.SaveChangesAsync(ct);
        return new TipoDocumentoFacturacionDto(tipo.Id, tipo.Nombre, tipo.Descripcion, tipo.Obligatorio, tipo.NitsClientes, tipo.Orden, tipo.Activo);
    }

    /// <summary>"900268747-9, 900331322" → "900268747;900331322" (sin dígito de verificación ni repetidos).</summary>
    private static string? NormalizarNits(string? texto)
    {
        var nits = (texto ?? "").Split([';', ',', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => new string(n.Split('-')[0].Where(char.IsDigit).ToArray()))
            .Where(n => n.Length >= 5)
            .Distinct()
            .ToList();
        var resultado = string.Join(";", nits);
        if (resultado.Length > 500)
            throw new InvalidOperationException("Demasiados NIT para un solo documento.");
        return resultado.Length == 0 ? null : resultado;
    }

    // ---------------------------------------------------------------------------------------------
    // Apoyo
    // ---------------------------------------------------------------------------------------------

    private async Task<List<TipoDocumentoFacturacion>> TiposActivosAsync(CancellationToken ct) =>
        await db.TiposDocumentoFacturacion.AsNoTracking().Where(t => t.Activo)
            .OrderBy(t => t.Orden).ThenBy(t => t.Id).ToListAsync(ct);

    /// <summary>
    /// Los tipos que aplican a un cliente: los que no tienen NITs (todos) + los que lo nombran. El NIT
    /// de WO puede venir con o sin dígito de verificación ("900268747", "9002687479", "900268747-9").
    /// </summary>
    internal static IEnumerable<TipoDocumentoFacturacion> TiposQueAplican(IEnumerable<TipoDocumentoFacturacion> tipos, string? nitCliente)
    {
        var nit = new string((nitCliente ?? "").Where(char.IsDigit).ToArray());
        foreach (var t in tipos)
        {
            if (string.IsNullOrWhiteSpace(t.NitsClientes)) { yield return t; continue; }
            if (nit.Length == 0) continue;
            var aplica = t.NitsClientes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(n => n == nit || (nit.Length == n.Length + 1 && nit.StartsWith(n, StringComparison.Ordinal)));
            if (aplica) yield return t;
        }
    }

    private static void ExigirDocumentar(Permisos permisos, Remision remision)
    {
        if (!permisos.PuedeDocumentar(remision))
            throw new UnauthorizedAccessException("Solo quien elaboró la remisión puede adjuntar sus documentos y enviarla a contabilidad.");
        if (remision.Estado != EstadoRemision.Entregada)
            throw new InvalidOperationException($"La REM {remision.Numero} todavía no está entregada.");
        if (remision.EstadoFacturacion == EstadoFacturacionRemision.EnFacturacion)
            throw new InvalidOperationException($"La REM {remision.Numero} ya está en contabilidad; si hay que corregir algo, contabilidad la tiene que devolver.");
        if (remision.EstadoFacturacion == EstadoFacturacionRemision.Facturada)
            throw new InvalidOperationException($"La REM {remision.Numero} ya está facturada.");
    }

    private async Task<List<int>> EmisoresAsync(string? elaboradoPorWO, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(elaboradoPorWO)) return [];
        var wo = elaboradoPorWO.Trim().ToUpper();
        return await db.Usuarios.AsNoTracking()
            .Where(u => u.Activo && u.UsuarioWO != null && u.UsuarioWO.Trim().ToUpper() == wo)
            .Select(u => u.Id).ToListAsync(ct);
    }

    private async Task<string?> NombresEmisorAsync(string? elaboradoPorWO, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(elaboradoPorWO)) return null;
        var wo = elaboradoPorWO.Trim().ToUpper();
        var nombres = await db.Usuarios.AsNoTracking()
            .Where(u => u.Activo && u.UsuarioWO != null && u.UsuarioWO.Trim().ToUpper() == wo)
            .Select(u => u.Nombre).ToListAsync(ct);
        return nombres.Count == 0 ? null : string.Join(", ", nombres);
    }

    private async Task<string> NombreUsuarioAsync(int usuarioId, CancellationToken ct) =>
        await db.Usuarios.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.Nombre).FirstOrDefaultAsync(ct) ?? "Alguien";

    /// <summary>Escapa solo lo peligroso en HTML (deja las tildes legibles en el correo).</summary>
    private static string H(string? s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
