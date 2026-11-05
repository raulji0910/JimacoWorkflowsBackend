using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class CuentasPorPagarService(AppDbContext db, IInstanciaDocumentoService instanciaService, TimeProvider timeProvider) : ICuentasPorPagarService
{
    /// <summary>
    /// "Prefijo de World Office" del tipo de documento "Pago a Proveedor" (sembrado en la migración
    /// AgregarCuentasPorPagar). No existe en WO a propósito: este documento nace acá, no en WO — el
    /// campo es obligatorio en TipoDocumento, y es lo que usa este servicio para encontrar el tipo
    /// sin depender de un Id distinto en cada ambiente. Al Sincronizador no le afecta: buscar un
    /// prefijo que WO no tiene simplemente no trae nada.
    /// </summary>
    public const string PrefijoPagoProveedor = "CXP";

    // Colombia no tiene horario de verano: "hoy" para comparar vencimientos (fechas de calendario
    // de WO) es siempre UTC−5.
    private DateTime Hoy => timeProvider.GetUtcNow().UtcDateTime.AddHours(-5).Date;

    public async Task GuardarFotoAsync(FotoCuentasPorPagarDto foto, CancellationToken ct = default)
    {
        db.PartidasCuentaPorPagar.RemoveRange(await db.PartidasCuentaPorPagar.ToListAsync(ct));
        db.AnticiposProveedor.RemoveRange(await db.AnticiposProveedor.ToListAsync(ct));

        var partidas = foto.Partidas
            .Where(p => p.SaldoWO != 0 && !string.IsNullOrWhiteSpace(p.Identificacion))
            .Select(p => new PartidaCuentaPorPagar
            {
                IdDocumentoDetalleWO = p.IdDocumentoDetalleWO,
                Identificacion = p.Identificacion.Trim(),
                NombreTercero = (p.NombreTercero ?? "").Trim(),
                Documento = p.Documento?.Trim(),
                Prefijo = p.Prefijo?.Trim(),
                NumeroDocumento = p.NumeroDocumento?.Trim(),
                NumeroDocumentoExterno = p.NumeroDocumentoExterno?.Trim(),
                Fecha = p.Fecha,
                Vencimiento = p.Vencimiento,
                // WO lo trae con naturaleza crédito (negativo = se debe) — acá se guarda al derecho.
                Pendiente = -p.SaldoWO,
                CodigoCuenta = p.CodigoCuenta?.Trim()
            })
            .ToList();

        var anticipos = foto.Anticipos
            .Where(a => a.Saldo != 0 && !string.IsNullOrWhiteSpace(a.Identificacion))
            .Select(a => new AnticipoProveedor
            {
                Identificacion = a.Identificacion.Trim(),
                NombreTercero = (a.NombreTercero ?? "").Trim(),
                Movimientos = a.Movimientos,
                Saldo = a.Saldo
            })
            .ToList();

        db.PartidasCuentaPorPagar.AddRange(partidas);
        db.AnticiposProveedor.AddRange(anticipos);

        var sincronizacion = await db.SincronizacionesCuentasPorPagar.FirstOrDefaultAsync(ct);
        if (sincronizacion is null)
        {
            sincronizacion = new SincronizacionCuentasPorPagar();
            db.SincronizacionesCuentasPorPagar.Add(sincronizacion);
        }
        sincronizacion.FechaCorte = timeProvider.GetUtcNow().UtcDateTime;
        sincronizacion.Partidas = partidas.Count;
        sincronizacion.Anticipos = anticipos.Count;

        await db.SaveChangesAsync(ct);
    }

    public async Task<ResumenCuentasPorPagarDto> ObtenerResumenAsync(int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        var partidas = await db.PartidasCuentaPorPagar.AsNoTracking().ToListAsync(ct);
        var anticipos = await db.AnticiposProveedor.AsNoTracking().ToListAsync(ct);
        var estados = await CalcularEstadosAsync(partidas, ct);
        var hoy = Hoy;

        var identificaciones = partidas.Select(p => p.Identificacion)
            .Concat(anticipos.Select(a => a.Identificacion))
            .Distinct()
            .ToList();

        var proveedores = identificaciones
            .Select(id => ArmarProveedor(id,
                partidas.Where(p => p.Identificacion == id).ToList(),
                anticipos.Where(a => a.Identificacion == id).ToList(),
                estados, hoy))
            .OrderByDescending(p => p.Neto)
            .ToList();

        var porPagar = partidas.Where(p => p.Pendiente > 0).ToList();
        decimal SumaVencidos(int desde, int hasta) => porPagar
            .Where(p => DiasVencido(p.Vencimiento, hoy) is int d && d >= desde && d <= hasta)
            .Sum(p => p.Pendiente);

        return new ResumenCuentasPorPagarDto(
            FechaCorte: await ObtenerFechaCorteAsync(ct),
            TotalPendiente: porPagar.Sum(p => p.Pendiente),
            TotalVencido: SumaVencidos(1, int.MaxValue),
            VenceProximos7Dias: SumaVencidos(-7, 0),
            AlDia: porPagar.Where(p => (DiasVencido(p.Vencimiento, hoy) ?? 0) <= 0).Sum(p => p.Pendiente),
            Vencido1a30: SumaVencidos(1, 30),
            Vencido31a60: SumaVencidos(31, 60),
            VencidoMas60: SumaVencidos(61, int.MaxValue),
            TotalAnticipos: anticipos.Where(a => a.Saldo > 0).Sum(a => a.Saldo),
            TotalEnTramite: proveedores.Sum(p => p.EnTramite),
            Proveedores: proveedores);
    }

    public async Task<DetalleProveedorCuentaPorPagarDto> ObtenerProveedorAsync(string identificacion, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        identificacion = identificacion.Trim();
        var partidas = await db.PartidasCuentaPorPagar.AsNoTracking()
            .Where(p => p.Identificacion == identificacion)
            .ToListAsync(ct);
        var anticipos = await db.AnticiposProveedor.AsNoTracking()
            .Where(a => a.Identificacion == identificacion)
            .ToListAsync(ct);

        if (partidas.Count == 0 && anticipos.Count == 0)
            throw new KeyNotFoundException("Ese proveedor no tiene cuentas por pagar ni anticipos pendientes.");

        var estados = await CalcularEstadosAsync(partidas, ct);
        var hoy = Hoy;

        var partidasDto = partidas
            .OrderBy(p => p.Vencimiento ?? p.Fecha)
            .ThenBy(p => p.Fecha)
            .Select(p =>
            {
                var estado = estados[p];
                return new PartidaCuentaPorPagarDto(
                    p.IdDocumentoDetalleWO, p.Documento, p.Prefijo, p.NumeroDocumento, p.NumeroDocumentoExterno,
                    p.Fecha, p.Vencimiento, DiasVencido(p.Vencimiento, hoy), p.Pendiente, p.CodigoCuenta,
                    estado.Estado, estado.InstanciaId, estado.NumeroReferencia);
            })
            .ToList();

        return new DetalleProveedorCuentaPorPagarDto(
            await ObtenerFechaCorteAsync(ct),
            ArmarProveedor(identificacion, partidas, anticipos, estados, hoy),
            partidasDto,
            anticipos.Sum(a => a.Movimientos));
    }

    public async Task<int> CrearPagoAsync(CrearPagoProveedorDto dto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        var ids = (dto.IdsDocumentoDetalleWO ?? []).Distinct().ToList();
        if (ids.Count == 0)
            throw new InvalidOperationException("Elegí al menos una factura para pagar.");

        var identificacion = (dto.Identificacion ?? "").Trim();
        var partidas = await db.PartidasCuentaPorPagar
            .Where(p => p.Identificacion == identificacion && p.IdDocumentoDetalleWO != null && ids.Contains(p.IdDocumentoDetalleWO.Value))
            .ToListAsync(ct);

        // La foto se reemplaza sola cada pocos minutos: si una factura ya no está, WO la cruzó (o
        // cambió) mientras la pantalla seguía abierta — mejor frenar que pagar sobre datos viejos.
        if (partidas.Select(p => p.IdDocumentoDetalleWO).Distinct().Count() != ids.Count)
            throw new InvalidOperationException("Alguna de las facturas elegidas ya no está pendiente en World Office (la información se actualizó). Recargá la pantalla y volvé a elegir.");

        var estados = await CalcularEstadosAsync(partidas, ct);
        var noDisponibles = partidas.Where(p => estados[p].Estado != EstadoPartidaCuentaPorPagar.Disponible).ToList();
        if (noDisponibles.Count > 0)
            throw new InvalidOperationException(
                $"Estas facturas no se pueden mandar a pagar (ya están en un pago o no tienen saldo a pagar): {string.Join(", ", noDisponibles.Select(Etiqueta))}.");

        var tipo = await db.TiposDocumento.FirstOrDefaultAsync(t => t.PrefijoWorldOffice == PrefijoPagoProveedor && t.Activo, ct)
            ?? throw new InvalidOperationException("No está configurado el tipo de documento \"Pago a Proveedor\".");

        var consecutivo = await db.InstanciasDocumento.CountAsync(i => i.TipoDocumentoId == tipo.Id, ct) + 1;
        var ordenadas = partidas.OrderBy(p => p.Vencimiento ?? p.Fecha).ThenBy(p => p.Fecha).ToList();

        var datos = new Dictionary<string, string> { ["nit"] = identificacion };
        if (!string.IsNullOrWhiteSpace(dto.Observaciones))
            datos["observaciones"] = dto.Observaciones.Trim();

        // Cada factura va como un renglón (cantidad 1, sin IVA — el saldo de WO ya es el valor a
        // pagar): así el detalle, el PDF y los correos del flujo la muestran sin pantallas nuevas, y
        // el Valor del documento sale de sumarlas, igual que en una OC.
        var creado = await instanciaService.CrearAsync(new CrearInstanciaDocumentoDto(
            TipoDocumentoId: tipo.Id,
            NumeroReferencia: consecutivo.ToString(),
            Proveedor: ordenadas[0].NombreTercero,
            Valor: ordenadas.Sum(p => p.Pendiente),
            FechaDocumento: Hoy,
            Datos: datos,
            Renglones: ordenadas.Select(p => new RenglonInputDto(
                Codigo: $"{p.Prefijo} {p.NumeroDocumento}".Trim(),
                Descripcion: DescribirFactura(p),
                Cantidad: 1,
                UnidadMedida: null,
                ValorUnitario: p.Pendiente,
                PorcentajeIva: 0m)).ToList()), usuarioId, ct);

        db.FacturasPagoProveedor.AddRange(ordenadas.Select(p => new FacturaPagoProveedor
        {
            InstanciaDocumentoId = creado.Id,
            IdDocumentoDetalleWO = p.IdDocumentoDetalleWO!.Value,
            Prefijo = p.Prefijo,
            NumeroDocumento = p.NumeroDocumento,
            NumeroDocumentoExterno = p.NumeroDocumentoExterno,
            Fecha = p.Fecha,
            Vencimiento = p.Vencimiento,
            Valor = p.Pendiente,
            CodigoCuenta = p.CodigoCuenta
        }));
        await db.SaveChangesAsync(ct);

        return creado.Id;
    }

    private sealed record EstadoPartida(EstadoPartidaCuentaPorPagar Estado, int? InstanciaId, string? NumeroReferencia);

    /// <summary>
    /// Estado de cada partida frente a los pagos ya creados acá. Un pago Rechazado libera sus
    /// facturas; uno en curso (o devuelto) las bloquea; uno Completado las bloquea solo mientras WO
    /// siga mostrando el MISMO saldo (todavía no cruzó el CEJ) — si el saldo cambió, WO aplicó un
    /// abono parcial y lo que queda se puede volver a pagar.
    /// </summary>
    private async Task<Dictionary<PartidaCuentaPorPagar, EstadoPartida>> CalcularEstadosAsync(IReadOnlyList<PartidaCuentaPorPagar> partidas, CancellationToken ct)
    {
        var ids = partidas.Where(p => p.IdDocumentoDetalleWO != null).Select(p => p.IdDocumentoDetalleWO!.Value).Distinct().ToList();

        var pagos = await db.FacturasPagoProveedor.AsNoTracking()
            .Where(f => ids.Contains(f.IdDocumentoDetalleWO) && f.InstanciaDocumento.Estado != EstadoInstanciaDocumento.Rechazado)
            .Select(f => new { f.IdDocumentoDetalleWO, f.Valor, f.InstanciaDocumentoId, f.InstanciaDocumento.Estado, f.InstanciaDocumento.NumeroReferencia })
            .ToListAsync(ct);

        var resultado = new Dictionary<PartidaCuentaPorPagar, EstadoPartida>(ReferenceEqualityComparer.Instance);
        foreach (var p in partidas)
        {
            if (p.IdDocumentoDetalleWO is null || p.Pendiente <= 0)
            {
                resultado[p] = new(EstadoPartidaCuentaPorPagar.NoPagable, null, null);
                continue;
            }

            var pago = pagos
                .Where(x => x.IdDocumentoDetalleWO == p.IdDocumentoDetalleWO)
                .OrderByDescending(x => x.InstanciaDocumentoId)
                .FirstOrDefault(x => x.Estado != EstadoInstanciaDocumento.Completado || x.Valor == p.Pendiente);

            resultado[p] = pago switch
            {
                null => new(EstadoPartidaCuentaPorPagar.Disponible, null, null),
                { Estado: EstadoInstanciaDocumento.Completado } => new(EstadoPartidaCuentaPorPagar.PagadaPendienteCruce, pago.InstanciaDocumentoId, pago.NumeroReferencia),
                _ => new(EstadoPartidaCuentaPorPagar.EnTramite, pago.InstanciaDocumentoId, pago.NumeroReferencia)
            };
        }
        return resultado;
    }

    private static ProveedorCuentaPorPagarDto ArmarProveedor(string identificacion, IReadOnlyList<PartidaCuentaPorPagar> partidas,
        IReadOnlyList<AnticipoProveedor> anticipos, IReadOnlyDictionary<PartidaCuentaPorPagar, EstadoPartida> estados, DateTime hoy)
    {
        var porPagar = partidas.Where(p => p.Pendiente > 0).ToList();
        var pendiente = porPagar.Sum(p => p.Pendiente);
        var saldoAFavor = -partidas.Where(p => p.Pendiente < 0).Sum(p => p.Pendiente);
        var totalAnticipos = anticipos.Sum(a => a.Saldo);
        var vencidas = porPagar.Where(p => DiasVencido(p.Vencimiento, hoy) > 0).ToList();

        return new ProveedorCuentaPorPagarDto(
            Identificacion: identificacion,
            Nombre: partidas.Select(p => p.NombreTercero).Concat(anticipos.Select(a => a.NombreTercero))
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? identificacion,
            Facturas: porPagar.Count,
            Pendiente: pendiente,
            Vencido: vencidas.Sum(p => p.Pendiente),
            SaldoAFavor: saldoAFavor,
            Anticipos: totalAnticipos,
            Neto: pendiente - saldoAFavor - totalAnticipos,
            EnTramite: porPagar
                .Where(p => estados[p].Estado is EstadoPartidaCuentaPorPagar.EnTramite or EstadoPartidaCuentaPorPagar.PagadaPendienteCruce)
                .Sum(p => p.Pendiente),
            VencimientoMasAntiguo: porPagar.Min(p => p.Vencimiento),
            DiasMaximoVencido: vencidas.Count == 0 ? 0 : vencidas.Max(p => DiasVencido(p.Vencimiento, hoy)!.Value));
    }

    private static int? DiasVencido(DateTime? vencimiento, DateTime hoy) =>
        vencimiento is { } v ? (int)(hoy - v.Date).TotalDays : null;

    private static string Etiqueta(PartidaCuentaPorPagar p) =>
        $"{p.Prefijo} {p.NumeroDocumento}".Trim() + (string.IsNullOrWhiteSpace(p.NumeroDocumentoExterno) ? "" : $" ({p.NumeroDocumentoExterno})");

    private static string DescribirFactura(PartidaCuentaPorPagar p)
    {
        var partes = new List<string> { string.IsNullOrWhiteSpace(p.NumeroDocumentoExterno) ? (p.Documento ?? "Documento") : $"Factura {p.NumeroDocumentoExterno}" };
        partes.Add($"del {p.Fecha:dd/MM/yyyy}");
        if (p.Vencimiento is { } v) partes.Add($"vence {v:dd/MM/yyyy}");
        return string.Join(" · ", partes);
    }

    private async Task<DateTime?> ObtenerFechaCorteAsync(CancellationToken ct) =>
        await db.SincronizacionesCuentasPorPagar.Select(s => (DateTime?)s.FechaCorte).FirstOrDefaultAsync(ct);

    private async Task ExigirPermisoAsync(int usuarioId, CancellationToken ct)
    {
        var tienePermiso = await db.UsuarioRoles
            .Where(ur => ur.UsuarioId == usuarioId)
            .AnyAsync(ur => ur.Rol.Activo && ur.Rol.PuedeVerCuentasPorPagar, ct);

        if (!tienePermiso)
            throw new UnauthorizedAccessException("No tenés permiso para el módulo de Cuentas por Pagar.");
    }
}
