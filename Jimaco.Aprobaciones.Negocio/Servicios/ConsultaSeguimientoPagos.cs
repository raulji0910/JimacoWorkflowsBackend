using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Consultas paginadas del seguimiento de órdenes de pago (2026-11-10): "Seguimiento de pagos" de
/// Gerencia Comercial y las pestañas Rechazados / Pagados del menú "Pagos". Todo se filtra y pagina
/// en la base — antes se traían hasta 300 órdenes de una y las más viejas se perdían sin avisar.
/// </summary>
internal static class ConsultaSeguimientoPagos
{
    public const string PorPagar = "PorPagar";
    public const string Pagado = "Pagado";
    public const string Rechazado = "Rechazado";
    public const string Otro = "Otro";

    public static int TamanoPagina(int? pedido, int porDefecto) => Math.Clamp(pedido ?? porDefecto, 1, 50);

    /// <summary>Todas las órdenes de pago (relaciones, extraordinarias y las de antes del rediseño).</summary>
    public static IQueryable<InstanciaDocumento> Ordenes(AppDbContext db) =>
        db.InstanciasDocumento.AsNoTracking()
            .Where(i => i.TipoDocumento.PrefijoWorldOffice == CuentasPorPagarService.PrefijoPagoProveedor && i.PagoProveedor != null);

    /// <summary>
    /// Fechas como días de Colombia (UTC−5): "desde" incluye todo ese día, "hasta" también.
    /// </summary>
    public static (DateTime? Desde, DateTime? Hasta) RangoUtc(DateTime? desde, DateTime? hasta) =>
        (desde?.Date.AddHours(5), hasta?.Date.AddDays(1).AddHours(5));

    public static IQueryable<InstanciaDocumento> Buscar(IQueryable<InstanciaDocumento> q, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return q;
        var t = texto.Trim();
        return q.Where(i => i.PagoProveedor!.Identificacion.Contains(t)
                            || (i.PagoProveedor.NombreTercero != null && i.PagoProveedor.NombreTercero.Contains(t))
                            || (i.Proveedor != null && i.Proveedor.Contains(t)));
    }

    public static IQueryable<InstanciaDocumento> PorEstado(IQueryable<InstanciaDocumento> q, string? estado) => estado switch
    {
        PorPagar => q.Where(i => i.Estado == EstadoInstanciaDocumento.EnProceso && i.PagoProveedor!.LotePagoId == null),
        Pagado => q.Where(i => i.Estado != EstadoInstanciaDocumento.Rechazado
                               && (i.PagoProveedor!.LotePagoId != null || i.Estado == EstadoInstanciaDocumento.Completado)),
        Rechazado => q.Where(i => i.Estado == EstadoInstanciaDocumento.Rechazado),
        _ => q
    };

    public static async Task<ConteoEstadoDto> ContarAsync(IQueryable<InstanciaDocumento> q, CancellationToken ct) =>
        new(await q.CountAsync(ct), await q.SumAsync(i => i.Valor ?? 0, ct));

    /// <summary>Arma los DTO de las órdenes pedidas (sin las líneas: se piden al abrir cada una).</summary>
    public static async Task<List<SeguimientoPagoDto>> ArmarAsync(AppDbContext db, IQueryable<InstanciaDocumento> q, CancellationToken ct)
    {
        var ordenes = await q
            .Include(i => i.PagoProveedor).ThenInclude(p => p!.RelacionPago)
            .Include(i => i.PagoProveedor).ThenInclude(p => p!.LotePago)
            .ToListAsync(ct);

        var ids = ordenes.Select(o => o.Id).ToList();
        var rechazos = (await db.HistorialAcciones.AsNoTracking()
                .Where(h => ids.Contains(h.InstanciaDocumentoId) && h.Accion == TipoAccion.Rechazado)
                .Select(h => new { h.InstanciaDocumentoId, h.Id, h.Comentario, h.Fecha, Usuario = h.Usuario.Nombre })
                .ToListAsync(ct))
            .GroupBy(h => h.InstanciaDocumentoId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Id).First());

        var usuariosIds = ordenes.Select(o => o.CreadoPorUsuarioId).Distinct().ToList();
        var creadores = await db.Usuarios.AsNoTracking().Where(u => usuariosIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nombre, ct);

        return ordenes.Select(i =>
        {
            var p = i.PagoProveedor!;
            var rechazo = rechazos.GetValueOrDefault(i.Id);
            return new SeguimientoPagoDto(
                i.Id, i.NumeroReferencia, p.RelacionPago?.Numero, p.EsExtraordinario, p.Identificacion,
                p.NombreTercero ?? i.Proveedor ?? p.Identificacion, i.Valor ?? 0, i.FechaCreacion,
                creadores.GetValueOrDefault(i.CreadoPorUsuarioId), EstadoDe(i),
                p.LotePago?.Numero, p.LotePago?.FechaPago, p.LotePago?.NombreBanco,
                p.PrefijoCE, p.NumeroCE, p.PendienteCreacionCE, p.ConflictoCreacionCE,
                rechazo?.Comentario, rechazo?.Usuario, rechazo?.Fecha,
                []);
        }).ToList();
    }

    public static string EstadoDe(InstanciaDocumento i) => i.Estado switch
    {
        EstadoInstanciaDocumento.Rechazado => Rechazado,
        _ when i.PagoProveedor!.LotePagoId is not null => Pagado,
        EstadoInstanciaDocumento.EnProceso => PorPagar,
        EstadoInstanciaDocumento.Completado => Pagado,
        _ => Otro
    };

    /// <summary>Líneas (facturas, anticipos o el giro) de UNA orden — se piden al abrirla.</summary>
    public static async Task<IReadOnlyList<LineaOrdenPagoDto>> LineasAsync(AppDbContext db, int instanciaDocumentoId, CancellationToken ct)
    {
        var orden = await PagosService.ConsultarOrdenes(db).FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct)
            ?? throw new KeyNotFoundException("Orden de pago no encontrada.");
        return PagosService.LineasDe(orden);
    }
}
