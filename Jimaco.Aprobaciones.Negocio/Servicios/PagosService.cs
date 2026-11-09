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
    TimeProvider timeProvider) : IPagosService
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
            var lineas = p.EsExtraordinario
                ? [new LineaOrdenPagoDto(p.NombreTipoGiro ?? "Pago extraordinario", p.Concepto, i.Valor ?? 0)]
                : i.FacturasPagoProveedor.OrderBy(f => f.Vencimiento ?? f.Fecha)
                    .Select(f => new LineaOrdenPagoDto($"{f.Prefijo} {f.NumeroDocumento}".Trim(),
                        string.Join(" · ", new[] { f.NumeroDocumentoExterno is { Length: > 0 } ext ? $"Factura {ext}" : null, f.Vencimiento is { } v ? $"vence {v:dd/MM/yyyy}" : null }.Where(x => x is not null)),
                        f.Valor))
                    .Concat(i.AnticiposPagoProveedor.OrderBy(a => a.Fecha)
                        .Select(a => new LineaOrdenPagoDto($"Anticipo {a.Prefijo} {a.NumeroDocumento}".Trim(), a.Nota, -a.Valor)))
                    .ToList();

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

        // Rechazado libera las facturas y anticipos (vuelven a "Disponible" para Gerencia Comercial)
        // y le avisa a quien la creó con el motivo — el flujo normal ya hace las dos cosas.
        await instanciaService.EjecutarAccionAsync(instanciaDocumentoId, usuarioId,
            new EjecutarAccionDto(TipoAccion.Rechazado, comentario.Trim()), ct, omitirControlRol: true);
    }

    public async Task<IReadOnlyList<LotePagoDto>> ListarLotesAsync(int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        return await ListarLotesInternoAsync(_ => true, 30, ct);
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

    private IQueryable<InstanciaDocumento> ConsultarOrdenes() =>
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
