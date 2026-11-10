using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Menú "Pagos" del pagador (2026-11-09, pedido real): Gerencia General ya no aprueba las órdenes de
/// pago una por una desde "Pendientes" (se mezclaban con las OC). Acá ve todo lo que le mandaron
/// (relaciones de Gerencia Comercial y pagos extraordinarios), elige lo que paga como un LOTE —
/// mismo banco, misma fecha y UN solo soporte PDF para todo — y rechaza lo que no; lo que no elige
/// simplemente queda pendiente. Por dentro cada orden sigue su flujo (un CE por tercero en WO, ver
/// InstanciaDocumentoService.ConfirmarPagoProveedorAsync) y el aviso sale uno solo por lote.
/// </summary>
public class PagosService(AppDbContext db, IInstanciaDocumentoService instanciaService, IAlmacenamientoArchivos almacenamiento,
    INotificacionService notificacionService, TimeProvider timeProvider) : IPagosService
{
    public async Task<IReadOnlyList<OrdenPagoPendienteDto>> ListarPendientesAsync(int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        var ordenes = await ConsultarOrdenes()
            .Where(i => i.Estado == EstadoInstanciaDocumento.EnProceso && i.PagoProveedor!.LotePagoId == null)
            .OrderBy(i => i.FechaCreacion)
            .ToListAsync(ct);

        var creadores = await NombresUsuariosAsync(ordenes.Select(o => o.CreadoPorUsuarioId), ct);

        return ordenes.Select(i =>
        {
            var p = i.PagoProveedor!;
            var lineas = LineasDe(i);

            return new OrdenPagoPendienteDto(
                i.Id, i.NumeroReferencia, p.EsExtraordinario, p.Identificacion,
                p.NombreTercero ?? i.Proveedor ?? p.Identificacion,
                i.FechaCreacion, creadores.GetValueOrDefault(i.CreadoPorUsuarioId),
                p.RelacionPago?.Numero, p.RelacionPago?.Observaciones,
                lineas, i.Valor ?? 0);
        }).ToList();
    }

    public async Task<LotePagoDto> PagarLoteAsync(IReadOnlyList<int> instanciasIds, int? idCuentaBancoWO, DateTime? fechaPago,
        string? nombreArchivo, string? contentType, long tamanoBytes, Stream? soporte, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        var ids = (instanciasIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            throw new InvalidOperationException("Elegí al menos una orden de pago.");

        var instancias = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual)
            .Include(i => i.PagoProveedor)
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(ct);

        if (instancias.Count != ids.Count)
            throw new InvalidOperationException("Alguna de las órdenes elegidas ya no existe. Recargá la pantalla.");

        foreach (var i in instancias)
        {
            var desc = $"orden de pago {i.NumeroReferencia} ({i.PagoProveedor?.NombreTercero ?? i.Proveedor})";
            if (i.TipoDocumento.PrefijoWorldOffice != CuentasPorPagarService.PrefijoPagoProveedor || i.PagoProveedor is null)
                throw new InvalidOperationException($"El documento {i.Id} no es una orden de pago.");
            if (i.Estado != EstadoInstanciaDocumento.EnProceso || i.PagoProveedor.LotePagoId is not null)
                throw new InvalidOperationException($"La {desc} ya no está pendiente de pago (alguien la pagó o la rechazó). Recargá la pantalla.");
            if (i.PasoActual is not { RequiereSoportePagoAlAprobar: true })
                throw new InvalidOperationException($"La {desc} todavía no está en el paso de pago de su flujo.");
        }

        var hoy = timeProvider.GetUtcNow().UtcDateTime.AddHours(-5).Date;
        var fecha = (fechaPago ?? hoy).Date;
        if (fecha > hoy)
            throw new InvalidOperationException("La fecha de pago no puede ser posterior a hoy.");

        var total = instancias.Sum(i => i.Valor ?? 0);
        CuentaBancoWO? banco = null;
        if (total > 0)
        {
            if (idCuentaBancoWO is not int idBanco)
                throw new InvalidOperationException("Elegí el banco desde donde se hizo el pago.");
            banco = await db.CuentasBancoWO.AsNoTracking().FirstOrDefaultAsync(b => b.IdCuentaContableWO == idBanco, ct)
                ?? throw new InvalidOperationException("Ese banco no está en la lista de cuentas de World Office.");

            if (soporte is null || string.IsNullOrWhiteSpace(nombreArchivo))
                throw new InvalidOperationException("Adjuntá el soporte del pago (PDF).");
            if (contentType != "application/pdf" && !nombreArchivo.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El soporte del pago tiene que ser un PDF.");
        }

        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        // El PDF se guarda UNA vez; cada orden lo referencia con su propio Adjunto (misma ruta).
        var ruta = total > 0 ? await almacenamiento.GuardarAsync(nombreArchivo!, soporte!, ct) : null;
        var ahora = timeProvider.GetUtcNow().UtcDateTime;

        var lote = new LotePago
        {
            Numero = (await db.LotesPago.MaxAsync(l => (int?)l.Numero, ct) ?? 0) + 1,
            PagadoPorUsuarioId = usuarioId,
            Fecha = ahora,
            FechaPago = fecha,
            IdCuentaBancoWO = banco?.IdCuentaContableWO,
            NombreBanco = banco is null ? null : $"{banco.Codigo} {banco.Nombre}",
            Total = total,
            NombreArchivoSoporte = ruta is null ? null : nombreArchivo,
            RutaArchivoSoporte = ruta
        };
        db.LotesPago.Add(lote);
        await db.SaveChangesAsync(ct);

        foreach (var i in instancias.OrderBy(i => i.Id))
        {
            i.PagoProveedor!.LotePagoId = lote.Id;

            int? adjuntoId = null;
            if (ruta is not null && (i.Valor ?? 0) > 0)
            {
                var adjunto = new Adjunto
                {
                    InstanciaDocumentoId = i.Id,
                    NombreArchivo = nombreArchivo!,
                    RutaArchivo = ruta,
                    ContentType = contentType,
                    TamanoBytes = tamanoBytes,
                    SubidoPorUsuarioId = usuarioId,
                    FechaCarga = ahora
                };
                db.Adjuntos.Add(adjunto);
                await db.SaveChangesAsync(ct);
                adjuntoId = adjunto.Id;
            }
            else
            {
                await db.SaveChangesAsync(ct);
            }

            await instanciaService.EjecutarAccionAsync(i.Id, usuarioId, new EjecutarAccionDto(
                TipoAccion.Aprobado,
                $"Pagado en el lote {lote.Numero}",
                SoportePagoAdjuntoId: adjuntoId,
                IdCuentaBancoWO: banco?.IdCuentaContableWO,
                FechaPago: fecha), ct, omitirControlRol: true);
        }

        if (tx is not null) await tx.CommitAsync(ct);

        return (await ListarLotesInternoAsync(l => l.Id == lote.Id, 1, ct)).Single();
    }

    public async Task RechazarAsync(int instanciaDocumentoId, string comentario, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        if (string.IsNullOrWhiteSpace(comentario))
            throw new InvalidOperationException("Escribí el motivo del rechazo (le llega a Gerencia Comercial).");

        var instancia = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PagoProveedor)
            .FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct)
            ?? throw new KeyNotFoundException("Orden de pago no encontrada.");
        if (instancia.TipoDocumento.PrefijoWorldOffice != CuentasPorPagarService.PrefijoPagoProveedor || instancia.PagoProveedor is null)
            throw new InvalidOperationException("Ese documento no es una orden de pago.");
        if (instancia.PagoProveedor.LotePagoId is not null)
            throw new InvalidOperationException("Esa orden ya se pagó en un lote.");

        // Rechazado libera las facturas y anticipos (vuelven a "Disponible" para Gerencia Comercial).
        await instanciaService.EjecutarAccionAsync(instanciaDocumentoId, usuarioId,
            new EjecutarAccionDto(TipoAccion.Rechazado, comentario.Trim()), ct, omitirControlRol: true);

        // Aviso a quien la mandó pagar (pedido 2026-11-10): tercero, NIT, valor, facturas y el motivo,
        // con el link a "Seguimiento de pagos". El rechazo ya quedó guardado: si el correo falla no se deshace.
        try
        {
            var orden = await ConsultarOrdenes().FirstAsync(i => i.Id == instanciaDocumentoId, ct);
            var p = orden.PagoProveedor!;
            var pagador = (await NombresUsuariosAsync([usuarioId], ct)).GetValueOrDefault(usuarioId) ?? "El pagador";
            var tercero = p.NombreTercero ?? orden.Proveedor ?? p.Identificacion;
            var origen = p.EsExtraordinario ? "el pago extraordinario" : $"la orden de pago de la relación {p.RelacionPago?.Numero}";
            var filas = string.Concat(LineasDe(orden).Select(l =>
                $"<tr><td>{Html(l.Etiqueta)}</td><td>{Html(l.Detalle)}</td><td style=\"text-align:right\">{l.Valor:N0}</td></tr>"));
            var cuerpo =
                $"<p>{Html(pagador)} <b>rechazó</b> {Html(origen)}" +
                $"a <b>{Html(tercero)}</b> (NIT {Html(p.Identificacion)}) por <b>${(orden.Valor ?? 0):N0}</b>.</p>" +
                $"<p><b>Motivo:</b> {Html(comentario.Trim())}</p>" +
                "<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse\">" +
                $"<tr><th>Documento</th><th>Detalle</th><th>Valor</th></tr>{filas}</table>" +
                (p.EsExtraordinario ? "" : "<p>Las facturas quedaron otra vez disponibles en Cuentas por pagar, por si las querés incluir en otra relación.</p>");
            await notificacionService.NotificarHtmlAsync(orden.Id, orden.CreadoPorUsuarioId,
                $"[Jimaco Aprobaciones] Pago rechazado: {tercero} por ${(orden.Valor ?? 0):N0}", cuerpo, "/cuentas-por-pagar/seguimiento", ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // El rechazo ya quedó; el aviso no lo tumba (queda visible en "Seguimiento de pagos").
        }
    }

    /// <summary>Las líneas que se le muestran a quien paga / a quien hace seguimiento: facturas (y anticipos en negativo) o el giro.</summary>
    internal static List<LineaOrdenPagoDto> LineasDe(InstanciaDocumento i)
    {
        var p = i.PagoProveedor!;
        return p.EsExtraordinario
            ? [new LineaOrdenPagoDto(p.NombreTipoGiro ?? "Pago extraordinario", p.Concepto, i.Valor ?? 0)]
            : i.FacturasPagoProveedor.OrderBy(f => f.Vencimiento ?? f.Fecha)
                .Select(f => new LineaOrdenPagoDto($"{f.Prefijo} {f.NumeroDocumento}".Trim(),
                    string.Join(" · ", new[] { f.NumeroDocumentoExterno is { Length: > 0 } ext ? $"Factura {ext}" : null, f.Vencimiento is { } v ? $"vence {v:dd/MM/yyyy}" : null }.Where(x => x is not null)),
                    f.Valor))
                .Concat(i.AnticiposPagoProveedor.OrderBy(a => a.Fecha)
                    .Select(a => new LineaOrdenPagoDto($"Anticipo {a.Prefijo} {a.NumeroDocumento}".Trim(), a.Nota, -a.Valor)))
                .ToList();
    }

    private static string Html(string? texto) => System.Net.WebUtility.HtmlEncode(texto ?? "");

    public async Task<PaginaDto<SeguimientoPagoDto>> ListarRechazadosAsync(FiltroPagosDto filtro, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        var tamano = ConsultaSeguimientoPagos.TamanoPagina(filtro.TamanoPagina, 15);
        var pagina = Math.Max(1, filtro.Pagina);
        var (desde, hasta) = ConsultaSeguimientoPagos.RangoUtc(filtro.Desde, filtro.Hasta);

        // Fecha = cuándo se rechazó.
        var q = ConsultaSeguimientoPagos.PorEstado(
            ConsultaSeguimientoPagos.Buscar(ConsultaSeguimientoPagos.Ordenes(db), filtro.Buscar), ConsultaSeguimientoPagos.Rechazado);
        var conFecha = q.Select(i => new
        {
            Orden = i,
            FechaRechazo = db.HistorialAcciones.Where(h => h.InstanciaDocumentoId == i.Id && h.Accion == TipoAccion.Rechazado).Max(h => (DateTime?)h.Fecha)
        });
        if (desde is { } d) conFecha = conFecha.Where(x => x.FechaRechazo >= d);
        if (hasta is { } h) conFecha = conFecha.Where(x => x.FechaRechazo < h);

        var total = await conFecha.CountAsync(ct);
        var ids = await conFecha.OrderByDescending(x => x.FechaRechazo).ThenByDescending(x => x.Orden.Id)
            .Skip((pagina - 1) * tamano).Take(tamano).Select(x => x.Orden.Id).ToListAsync(ct);

        var items = ids.Count == 0 ? []
            : (await ConsultaSeguimientoPagos.ArmarAsync(db, ConsultaSeguimientoPagos.Ordenes(db).Where(i => ids.Contains(i.Id)), ct))
                .OrderBy(o => ids.IndexOf(o.InstanciaDocumentoId)).ToList();
        return new PaginaDto<SeguimientoPagoDto>(items, total, pagina, tamano);
    }

    public async Task<PaginaDto<LotePagoDto>> ListarLotesAsync(FiltroPagosDto filtro, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        var tamano = ConsultaSeguimientoPagos.TamanoPagina(filtro.TamanoPagina, 10);
        var pagina = Math.Max(1, filtro.Pagina);

        // Fecha = fecha del pago (es un día, sin hora).
        var q = db.LotesPago.AsNoTracking();
        if (filtro.Desde is { } d) q = q.Where(l => l.FechaPago >= d.Date);
        if (filtro.Hasta is { } h) q = q.Where(l => l.FechaPago < h.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var t = filtro.Buscar.Trim();
            q = q.Where(l => l.Pagos.Any(p => p.Identificacion.Contains(t) || (p.NombreTercero != null && p.NombreTercero.Contains(t))));
        }

        var total = await q.CountAsync(ct);
        var ids = await q.OrderByDescending(l => l.Numero).Skip((pagina - 1) * tamano).Take(tamano).Select(l => l.Id).ToListAsync(ct);
        var items = ids.Count == 0 ? [] : await ListarLotesInternoAsync(l => ids.Contains(l.Id), tamano, ct);
        return new PaginaDto<LotePagoDto>(items, total, pagina, tamano);
    }

    public async Task<(Stream Contenido, string NombreArchivo)> DescargarSoporteLoteAsync(int lotePagoId, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var lote = await db.LotesPago.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotePagoId, ct)
            ?? throw new KeyNotFoundException("Lote no encontrado.");
        if (lote.RutaArchivoSoporte is null)
            throw new KeyNotFoundException("Ese lote no tiene soporte (no movió plata).");
        return (await almacenamiento.AbrirAsync(lote.RutaArchivoSoporte, ct), lote.NombreArchivoSoporte ?? "soporte.pdf");
    }

    private async Task<List<LotePagoDto>> ListarLotesInternoAsync(System.Linq.Expressions.Expression<Func<LotePago, bool>> filtro, int maximo, CancellationToken ct)
    {
        var lotes = await db.LotesPago.AsNoTracking()
            .Where(filtro)
            .Include(l => l.Pagos).ThenInclude(p => p.InstanciaDocumento)
            .OrderByDescending(l => l.Numero)
            .Take(maximo)
            .AsSplitQuery()
            .ToListAsync(ct);

        var pagadores = await NombresUsuariosAsync(lotes.Select(l => l.PagadoPorUsuarioId), ct);

        return lotes.Select(l => new LotePagoDto(
            l.Id, l.Numero, l.Fecha, l.FechaPago, l.NombreBanco, l.Total, pagadores.GetValueOrDefault(l.PagadoPorUsuarioId),
            l.RutaArchivoSoporte is not null,
            l.Pagos.OrderBy(p => p.NombreTercero).Select(p => new LoteOrdenDto(
                p.InstanciaDocumentoId, p.InstanciaDocumento.NumeroReferencia, p.NombreTercero ?? p.InstanciaDocumento.Proveedor ?? p.Identificacion,
                p.EsExtraordinario, p.InstanciaDocumento.Valor ?? 0, p.PrefijoCE, p.NumeroCE, p.PendienteCreacionCE, p.ConflictoCreacionCE)).ToList()))
            .ToList();
    }

    internal IQueryable<InstanciaDocumento> ConsultarOrdenes() => ConsultarOrdenes(db);

    internal static IQueryable<InstanciaDocumento> ConsultarOrdenes(AppDbContext db) =>
        db.InstanciasDocumento.AsNoTracking()
            .Include(i => i.PagoProveedor).ThenInclude(p => p!.RelacionPago)
            .Include(i => i.FacturasPagoProveedor)
            .Include(i => i.AnticiposPagoProveedor)
            .Where(i => i.TipoDocumento.PrefijoWorldOffice == CuentasPorPagarService.PrefijoPagoProveedor && i.PagoProveedor != null)
            .AsSplitQuery();

    private async Task<Dictionary<int, string>> NombresUsuariosAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await db.Usuarios.AsNoTracking().Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nombre, ct);
    }

    private async Task ExigirPermisoAsync(int usuarioId, CancellationToken ct)
    {
        var tienePermiso = await db.UsuarioRoles
            .Where(ur => ur.UsuarioId == usuarioId)
            .AnyAsync(ur => ur.Rol.Activo && ur.Rol.PuedePagarCuentas, ct);

        if (!tienePermiso)
            throw new UnauthorizedAccessException("No tenés permiso para el menú de Pagos.");
    }
}
