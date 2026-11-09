using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class CuentasPorPagarService(AppDbContext db, IInstanciaDocumentoService instanciaService, INotificacionService notificacionService,
    TimeProvider timeProvider) : ICuentasPorPagarService
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
                IdDocumentoDetalleWO = a.IdDocumentoDetalleWO,
                Identificacion = a.Identificacion.Trim(),
                NombreTercero = (a.NombreTercero ?? "").Trim(),
                Prefijo = a.Prefijo?.Trim(),
                NumeroDocumento = a.NumeroDocumento?.Trim(),
                Fecha = a.Fecha,
                Nota = Recortar(a.Nota?.Trim(), 300),
                Movimientos = a.Movimientos,
                Saldo = a.Saldo
            })
            .ToList();

        db.PartidasCuentaPorPagar.AddRange(partidas);
        db.AnticiposProveedor.AddRange(anticipos);

        // Bancos: null = Sincronizador viejo que no los manda → se conserva la lista anterior.
        if (foto.Bancos is { Count: > 0 } bancos)
        {
            db.CuentasBancoWO.RemoveRange(await db.CuentasBancoWO.ToListAsync(ct));
            db.CuentasBancoWO.AddRange(bancos
                .GroupBy(b => b.IdCuentaContableWO)
                .Select(g => g.First())
                .Select(b => new CuentaBancoWO
                {
                    IdCuentaContableWO = b.IdCuentaContableWO,
                    Codigo = b.Codigo.Trim(),
                    Nombre = b.Nombre.Trim(),
                    PorDefecto = b.PorDefecto
                }));
        }

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
        var estadosAnticipos = await CalcularEstadosAnticiposAsync(anticipos, ct);
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

        var anticiposDto = anticipos
            .OrderBy(a => a.Fecha ?? DateTime.MinValue)
            .ThenBy(a => a.IdDocumentoDetalleWO)
            .Select(a =>
            {
                var estado = estadosAnticipos[a];
                return new AnticipoPartidaDto(a.IdDocumentoDetalleWO, a.Prefijo, a.NumeroDocumento, a.Fecha, a.Nota, a.Saldo,
                    estado.Estado, estado.InstanciaId, estado.NumeroReferencia);
            })
            .ToList();

        return new DetalleProveedorCuentaPorPagarDto(
            await ObtenerFechaCorteAsync(ct),
            ArmarProveedor(identificacion, partidas, anticipos, estados, hoy),
            partidasDto,
            anticipos.Sum(a => a.Movimientos),
            anticiposDto);
    }

    public async Task<int> CrearPagoAsync(CrearPagoProveedorDto dto, int usuarioId, CancellationToken ct = default, int? relacionPagoId = null)
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

        var ordenadas = partidas.OrderBy(p => p.Vencimiento ?? p.Fecha).ThenBy(p => p.Fecha).ToList();
        var totalFacturas = ordenadas.Sum(p => p.Pendiente);

        var aplicados = await AplicarAnticiposAsync(identificacion, dto.IdsAnticiposWO ?? [], totalFacturas, ct);
        var totalAnticipos = aplicados.Sum(a => a.Valor);

        var tipo = await db.TiposDocumento.FirstOrDefaultAsync(t => t.PrefijoWorldOffice == PrefijoPagoProveedor && t.Activo, ct)
            ?? throw new InvalidOperationException("No está configurado el tipo de documento \"Pago a Proveedor\".");

        var consecutivo = await db.InstanciasDocumento.CountAsync(i => i.TipoDocumentoId == tipo.Id, ct) + 1;

        var datos = new Dictionary<string, string> { ["nit"] = identificacion };
        if (!string.IsNullOrWhiteSpace(dto.Observaciones))
            datos["observaciones"] = dto.Observaciones.Trim();

        // Cada factura va como un renglón (cantidad 1, sin IVA — el saldo de WO ya es el valor a
        // pagar) y cada anticipo como un renglón en negativo: así el detalle, el PDF y los correos
        // del flujo muestran la orden de pago completa sin pantallas nuevas, y el Valor del
        // documento es el neto que de verdad sale del banco.
        var renglones = ordenadas.Select(p => new RenglonInputDto(
                Codigo: $"{p.Prefijo} {p.NumeroDocumento}".Trim(),
                Descripcion: DescribirFactura(p),
                Cantidad: 1,
                UnidadMedida: null,
                ValorUnitario: p.Pendiente,
                PorcentajeIva: 0m))
            .Concat(aplicados.Select(a => new RenglonInputDto(
                Codigo: $"{a.Anticipo.Prefijo} {a.Anticipo.NumeroDocumento}".Trim(),
                Descripcion: $"Anticipo descontado · {a.Anticipo.Nota}".Trim(' ', '·'),
                Cantidad: 1,
                UnidadMedida: null,
                ValorUnitario: -a.Valor,
                PorcentajeIva: 0m)))
            .ToList();

        var creado = await instanciaService.CrearAsync(new CrearInstanciaDocumentoDto(
            TipoDocumentoId: tipo.Id,
            NumeroReferencia: consecutivo.ToString(),
            Proveedor: ordenadas[0].NombreTercero,
            Valor: totalFacturas - totalAnticipos,
            FechaDocumento: Hoy,
            Datos: datos,
            Renglones: renglones), usuarioId, ct, notificar: false);

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
        db.AnticiposPagoProveedor.AddRange(aplicados.Select(a => new AnticipoPagoProveedor
        {
            InstanciaDocumentoId = creado.Id,
            IdDocumentoDetalleWO = a.Anticipo.IdDocumentoDetalleWO!.Value,
            Prefijo = a.Anticipo.Prefijo,
            NumeroDocumento = a.Anticipo.NumeroDocumento,
            Fecha = a.Anticipo.Fecha,
            Nota = a.Anticipo.Nota,
            SaldoAnterior = a.Anticipo.Saldo,
            Valor = a.Valor
        }));
        db.PagosProveedor.Add(new PagoProveedor
        {
            InstanciaDocumentoId = creado.Id,
            Identificacion = identificacion,
            NombreTercero = ordenadas[0].NombreTercero,
            RelacionPagoId = relacionPagoId
        });
        await db.SaveChangesAsync(ct);

        return creado.Id;
    }

    private sealed record AnticipoAplicado(AnticipoProveedor Anticipo, decimal Valor);

    /// <summary>
    /// Reparte los anticipos elegidos contra el total de facturas, del más viejo al más nuevo (como
    /// lo cruza Contabilidad): cada uno se aplica completo mientras alcance, el último puede quedar
    /// aplicado en parte. Un anticipo elegido que ya no alcanza a aplicarse es un error (la pantalla
    /// hace la misma cuenta y no debería mandarlo).
    /// </summary>
    private async Task<List<AnticipoAplicado>> AplicarAnticiposAsync(string identificacion, IReadOnlyList<int> idsAnticipos, decimal totalFacturas, CancellationToken ct)
    {
        var ids = idsAnticipos.Distinct().ToList();
        if (ids.Count == 0) return [];

        var anticipos = await db.AnticiposProveedor
            .Where(a => a.Identificacion == identificacion && a.IdDocumentoDetalleWO != null && ids.Contains(a.IdDocumentoDetalleWO.Value))
            .ToListAsync(ct);

        if (anticipos.Count != ids.Count)
            throw new InvalidOperationException("Alguno de los anticipos elegidos ya no está abierto en World Office (la información se actualizó). Recargá la pantalla y volvé a elegir.");

        var estados = await CalcularEstadosAnticiposAsync(anticipos, ct);
        var noDisponibles = anticipos.Where(a => estados[a].Estado != EstadoPartidaCuentaPorPagar.Disponible).ToList();
        if (noDisponibles.Count > 0)
            throw new InvalidOperationException(
                $"Estos anticipos ya están en otro pago: {string.Join(", ", noDisponibles.Select(a => $"{a.Prefijo} {a.NumeroDocumento}".Trim()))}.");

        var resultado = new List<AnticipoAplicado>();
        var restante = totalFacturas;
        foreach (var a in anticipos.OrderBy(a => a.Fecha ?? DateTime.MinValue).ThenBy(a => a.IdDocumentoDetalleWO))
        {
            if (restante <= 0)
                throw new InvalidOperationException(
                    $"El anticipo {a.Prefijo} {a.NumeroDocumento} ya no se alcanza a aplicar: los anteriores cubren todas las facturas elegidas. Desmarcalo.".Replace("  ", " "));

            var valor = Math.Min(a.Saldo, restante);
            resultado.Add(new AnticipoAplicado(a, valor));
            restante -= valor;
        }
        return resultado;
    }

    public async Task<IReadOnlyList<CuentaBancoDto>> ObtenerBancosAsync(CancellationToken ct = default) =>
        await db.CuentasBancoWO.AsNoTracking()
            .OrderByDescending(b => b.PorDefecto).ThenBy(b => b.Codigo)
            .Select(b => new CuentaBancoDto(b.IdCuentaContableWO, b.Codigo, b.Nombre, b.PorDefecto))
            .ToListAsync(ct);

    // ---- Creación del CE en WO (2026-11-06) — "pedir y recoger" como el resto de las colas del
    // Sincronizador: el gerente general confirma el pago (InstanciaDocumentoService.ConfirmarPagoProveedorAsync),
    // el Sincronizador lista lo pendiente, crea el CE en WO y avisa el número (o el conflicto). ----

    public async Task<IReadOnlyList<PendienteCreacionCEDto>> ListarPendientesCreacionCEAsync(CancellationToken ct = default)
    {
        var pendientes = await db.PagosProveedor.AsNoTracking()
            .Where(p => p.PendienteCreacionCE && p.FechaPago != null)
            .Select(p => new
            {
                p.InstanciaDocumentoId,
                p.EsExtraordinario,
                p.IdTerceroWO,
                p.CodigoCuentaGiro,
                p.GiroEsAnticipo,
                p.NombreTipoGiro,
                p.Concepto,
                ValorInstancia = p.InstanciaDocumento.Valor,
                p.InstanciaDocumento.NumeroReferencia,
                p.Identificacion,
                p.FechaPago,
                p.IdCuentaBancoWO,
                Facturas = p.InstanciaDocumento.FacturasPagoProveedor.OrderBy(f => f.Vencimiento ?? f.Fecha).ThenBy(f => f.Fecha)
                    .Select(f => new FacturaCreacionCEDto(f.IdDocumentoDetalleWO, f.Prefijo, f.NumeroDocumento, f.NumeroDocumentoExterno, f.Valor))
                    .ToList(),
                Anticipos = p.InstanciaDocumento.AnticiposPagoProveedor.OrderBy(a => a.Fecha).ThenBy(a => a.IdDocumentoDetalleWO)
                    .Select(a => new AnticipoCreacionCEDto(a.IdDocumentoDetalleWO, a.Prefijo, a.NumeroDocumento, a.Valor))
                    .ToList()
            })
            .ToListAsync(ct);

        return pendientes.Select(p => p.EsExtraordinario
            ? new PendienteCreacionCEDto(
                p.InstanciaDocumentoId, p.NumeroReferencia, p.Identificacion, p.FechaPago!.Value, p.IdCuentaBancoWO,
                p.ValorInstancia ?? 0, [], [],
                new GiroCreacionCEDto(p.IdTerceroWO ?? 0, p.Identificacion, p.CodigoCuentaGiro ?? "", p.GiroEsAnticipo,
                    p.NombreTipoGiro ?? "", p.Concepto ?? "", p.ValorInstancia ?? 0))
            : new PendienteCreacionCEDto(
                p.InstanciaDocumentoId, p.NumeroReferencia, p.Identificacion, p.FechaPago!.Value, p.IdCuentaBancoWO,
                p.Facturas.Sum(f => f.Valor) - p.Anticipos.Sum(a => a.Valor), p.Facturas, p.Anticipos)).ToList();
    }

    public async Task ConfirmarCECreadoAsync(int instanciaDocumentoId, CECreadoDto dto, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.PagoProveedor)
            .FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");
        var pago = instancia.PagoProveedor ?? throw new InvalidOperationException("El documento no es un pago a proveedor.");

        pago.PendienteCreacionCE = false;
        pago.ConflictoCreacionCE = null;
        pago.PrefijoCE = dto.Prefijo;
        pago.NumeroCE = dto.Numero;
        pago.IdAsientoContableCE = dto.IdAsientoContable;
        pago.FechaCreacionCE = timeProvider.GetUtcNow().UtcDateTime;

        // Se reusa la cola de búsqueda de CE que ya existe (la de Contabilidad): trae una copia del
        // comprobante recién creado, la adjunta al documento y AHÍ sale el aviso de "pago
        // completado" (ver InstanciaDocumentoService.AdjuntarComprobanteEgresoAsync). El prefijo va
        // en el texto porque en pruebas es CEJ-2, no el CEJ de siempre.
        instancia.NumeroComprobanteEgresoSolicitado = $"{dto.Prefijo} {dto.Numero:0}";
        instancia.PendienteBusquedaComprobanteEgresoWO = true;
        instancia.ConflictoComprobanteEgresoWO = null;

        await db.SaveChangesAsync(ct);
    }

    public async Task ReportarConflictoCreacionCEAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.PagoProveedor)
            .Include(i => i.TipoDocumento)
            .Include(i => i.Historial)
            .FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");
        var pago = instancia.PagoProveedor ?? throw new InvalidOperationException("El documento no es un pago a proveedor.");

        // Sale de la cola (no reintentar a ciegas algo que ya sabemos que no cuadra con WO). El pago
        // real ya se hizo — lo que falta es el CE: avisarle a quien creó el pago y a quien lo
        // confirmó, para que Contabilidad lo haga a mano o se revise qué cambió.
        pago.PendienteCreacionCE = false;
        pago.ConflictoCreacionCE = Recortar(mensaje, 1000);
        await db.SaveChangesAsync(ct);

        if (pago.LotePagoId is int lotePagoId)
        {
            try { await instanciaService.NotificarLotePagoSiTerminoAsync(lotePagoId, ct); }
            catch { /* el aviso del lote no debe frenar el reporte del conflicto */ }
        }

        var desc = string.IsNullOrWhiteSpace(instancia.NumeroReferencia)
            ? instancia.TipoDocumento.Nombre
            : $"{instancia.TipoDocumento.Nombre} {instancia.NumeroReferencia}";
        var asunto = $"[Jimaco Aprobaciones] No se pudo crear el comprobante de egreso del {desc}";
        var cuerpo = $"El pago {desc} ya fue confirmado, pero el sistema NO creó el comprobante de egreso en World Office: {mensaje} " +
                     "Hay que revisarlo y, si corresponde, hacer el comprobante a mano en World Office.";

        var destinatarios = instancia.Historial
            .Where(h => h.Accion == TipoAccion.Aprobado)
            .OrderByDescending(h => h.Fecha)
            .Select(h => h.UsuarioId)
            .Take(1)
            .Append(instancia.CreadoPorUsuarioId)
            .Distinct()
            .ToList();

        foreach (var usuarioId in destinatarios)
        {
            try
            {
                await notificacionService.NotificarUsuarioAsync(instancia.Id, usuarioId, asunto, cuerpo, ct);
            }
            catch
            {
                // Mismo criterio que el resto de las notificaciones: un correo que falla no rompe la
                // cola (el conflicto ya quedó guardado y visible en el detalle del documento).
            }
        }
    }

    // =====================================================================================
    // Relación de pagos (2026-11-09) — pedido real: ya no un correo por proveedor. Gerencia
    // Comercial va marcando facturas proveedor por proveedor (borrador guardado acá), revisa todo
    // junto y ratifica: se crea una orden por proveedor (un CE por tercero en WO) y sale UN solo
    // correo al pagador con la relación completa.
    // =====================================================================================

    public async Task<RelacionBorradorDto> ObtenerBorradorAsync(int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var proveedores = await ArmarBorradorAsync(usuarioId, ct);
        return new RelacionBorradorDto(proveedores, proveedores.Sum(p => p.Facturas.Count), proveedores.Sum(p => p.Neto));
    }

    public async Task<RelacionBorradorDto> GuardarBorradorProveedorAsync(string identificacion, GuardarBorradorProveedorDto dto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        identificacion = identificacion.Trim();

        var borrador = await db.BorradoresPagoProveedor
            .FirstOrDefaultAsync(b => b.UsuarioId == usuarioId && b.Identificacion == identificacion, ct);

        var facturas = (dto.IdsFacturas ?? []).Distinct().ToList();
        if (facturas.Count == 0)
        {
            if (borrador is not null) db.BorradoresPagoProveedor.Remove(borrador);
        }
        else
        {
            var partidas = await db.PartidasCuentaPorPagar.AsNoTracking()
                .Where(p => p.Identificacion == identificacion && p.IdDocumentoDetalleWO != null && facturas.Contains(p.IdDocumentoDetalleWO.Value))
                .ToListAsync(ct);
            if (partidas.Select(p => p.IdDocumentoDetalleWO).Distinct().Count() != facturas.Count)
                throw new InvalidOperationException("Alguna de las facturas elegidas ya no está pendiente en World Office (la información se actualizó). Recargá la pantalla y volvé a elegir.");

            var estados = await CalcularEstadosAsync(partidas, ct);
            var noDisponibles = partidas.Where(p => estados[p].Estado != EstadoPartidaCuentaPorPagar.Disponible).ToList();
            if (noDisponibles.Count > 0)
                throw new InvalidOperationException(
                    $"Estas facturas no se pueden incluir (ya están en un pago o no tienen saldo a pagar): {string.Join(", ", noDisponibles.Select(Etiqueta))}.");

            if (borrador is null)
            {
                borrador = new BorradorPagoProveedor { UsuarioId = usuarioId, Identificacion = identificacion };
                db.BorradoresPagoProveedor.Add(borrador);
            }
            borrador.NombreTercero = partidas[0].NombreTercero;
            borrador.FacturasIds = string.Join(',', facturas);
            borrador.AnticiposIds = string.Join(',', (dto.IdsAnticipos ?? []).Distinct());
            borrador.FechaActualizacion = timeProvider.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(ct);
        return await ObtenerBorradorAsync(usuarioId, ct);
    }

    public async Task<RelacionCreadaDto> RatificarRelacionAsync(RatificarRelacionDto dto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        var proveedores = await ArmarBorradorAsync(usuarioId, ct);
        if (proveedores.Count == 0)
            throw new InvalidOperationException("La relación está vacía: marcá primero las facturas a pagar.");

        var conProblema = proveedores.Where(p => p.Problema is not null).ToList();
        if (conProblema.Count > 0)
            throw new InvalidOperationException(
                "Hay que corregir la relación antes de enviarla: " + string.Join(" ", conProblema.Select(p => $"{p.Nombre}: {p.Problema}")));

        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        var relacion = new RelacionPago
        {
            Numero = (await db.RelacionesPago.MaxAsync(r => (int?)r.Numero, ct) ?? 0) + 1,
            CreadoPorUsuarioId = usuarioId,
            Fecha = timeProvider.GetUtcNow().UtcDateTime,
            Observaciones = Recortar(dto.Observaciones?.Trim() is { Length: > 0 } o ? o : null, 500)
        };
        db.RelacionesPago.Add(relacion);
        await db.SaveChangesAsync(ct);

        var ordenes = new List<(int Id, BorradorProveedorDto Proveedor)>();
        foreach (var p in proveedores)
        {
            var id = await CrearPagoAsync(new CrearPagoProveedorDto(
                p.Identificacion,
                p.Facturas.Select(f => f.IdDocumentoDetalleWO).ToList(),
                relacion.Observaciones,
                // Solo los anticipos que de verdad alcanzan a aplicarse (el resto el borrador ya los mostró en 0).
                p.Anticipos.Where(a => a.Valor > 0).Select(a => a.IdDocumentoDetalleWO).ToList()), usuarioId, ct, relacion.Id);
            ordenes.Add((id, p));
        }

        db.BorradoresPagoProveedor.RemoveRange(await db.BorradoresPagoProveedor.Where(b => b.UsuarioId == usuarioId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);

        var total = proveedores.Sum(p => p.Neto);
        var creador = await db.Usuarios.Where(u => u.Id == usuarioId).Select(u => u.Nombre).FirstOrDefaultAsync(ct);
        var filas = string.Concat(proveedores.Select(p =>
            $"<tr><td>{Html(p.Nombre)}</td><td style=\"text-align:center\">{p.Facturas.Count}</td>" +
            $"<td style=\"text-align:right\">{p.TotalFacturas:N0}</td><td style=\"text-align:right\">{(p.TotalAnticipos > 0 ? "-" + p.TotalAnticipos.ToString("N0") : "")}</td>" +
            $"<td style=\"text-align:right\"><b>{p.Neto:N0}</b></td></tr>"));
        var cuerpo = $"<p>{Html(creador ?? "Gerencia Comercial")} envió la <b>relación de pagos {relacion.Numero}</b>: " +
                     $"{proveedores.Count} proveedor(es), {proveedores.Sum(p => p.Facturas.Count)} factura(s), por <b>${total:N0}</b>.</p>" +
                     (relacion.Observaciones is null ? "" : $"<p><i>{Html(relacion.Observaciones)}</i></p>") +
                     "<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse\">" +
                     "<tr><th>Proveedor</th><th>Facturas</th><th>Valor facturas</th><th>Anticipos</th><th>A pagar</th></tr>" +
                     $"{filas}<tr><td colspan=\"4\"><b>Total</b></td><td style=\"text-align:right\"><b>{total:N0}</b></td></tr></table>" +
                     "<p>Desde el menú <b>Pagos</b> elegís qué pagar (como un lote, con un solo soporte) y qué rechazar.</p>";
        await AvisarPagadoresAsync(ordenes[0].Id, $"[Jimaco Aprobaciones] Relación de pagos {relacion.Numero} por ${total:N0}", cuerpo, ct);

        return new RelacionCreadaDto(relacion.Numero, ordenes.Count, total);
    }

    /// <summary>
    /// El borrador del usuario contra la foto vigente de WO: cada factura con su saldo de hoy (o
    /// marcada como no disponible), los anticipos repartidos igual que al crear el pago (del más viejo
    /// al más nuevo, sin pasarse del total de facturas) y el neto por proveedor.
    /// </summary>
    private async Task<List<BorradorProveedorDto>> ArmarBorradorAsync(int usuarioId, CancellationToken ct)
    {
        var borradores = await db.BorradoresPagoProveedor.AsNoTracking()
            .Where(b => b.UsuarioId == usuarioId)
            .OrderBy(b => b.NombreTercero)
            .ToListAsync(ct);

        var resultado = new List<BorradorProveedorDto>();
        foreach (var b in borradores)
        {
            var idsFacturas = ParsearIds(b.FacturasIds);
            var idsAnticipos = ParsearIds(b.AnticiposIds);

            var partidas = await db.PartidasCuentaPorPagar.AsNoTracking()
                .Where(p => p.Identificacion == b.Identificacion && p.IdDocumentoDetalleWO != null && idsFacturas.Contains(p.IdDocumentoDetalleWO.Value))
                .ToListAsync(ct);
            var estados = await CalcularEstadosAsync(partidas, ct);

            var problemas = new List<string>();
            var faltantes = idsFacturas.Count - partidas.Select(p => p.IdDocumentoDetalleWO).Distinct().Count();
            if (faltantes > 0) problemas.Add($"{faltantes} factura(s) ya no están pendientes en World Office.");

            var facturas = partidas
                .OrderBy(p => p.Vencimiento ?? p.Fecha).ThenBy(p => p.Fecha)
                .Select(p => new BorradorFacturaDto(p.IdDocumentoDetalleWO!.Value, $"{p.Prefijo} {p.NumeroDocumento}".Trim(), p.NumeroDocumentoExterno,
                    p.Vencimiento, p.Pendiente, estados[p].Estado == EstadoPartidaCuentaPorPagar.Disponible))
                .ToList();
            if (facturas.Any(f => !f.Disponible))
                problemas.Add("Hay facturas que ya están en otro pago: " + string.Join(", ", facturas.Where(f => !f.Disponible).Select(f => f.Etiqueta)) + ".");

            var totalFacturas = facturas.Where(f => f.Disponible).Sum(f => f.Valor);

            var anticiposWO = await db.AnticiposProveedor.AsNoTracking()
                .Where(a => a.Identificacion == b.Identificacion && a.IdDocumentoDetalleWO != null && idsAnticipos.Contains(a.IdDocumentoDetalleWO.Value))
                .ToListAsync(ct);
            var estadosAnticipos = await CalcularEstadosAnticiposAsync(anticiposWO, ct);

            var anticipos = new List<BorradorAnticipoDto>();
            var restante = totalFacturas;
            foreach (var a in anticiposWO.OrderBy(a => a.Fecha ?? DateTime.MinValue).ThenBy(a => a.IdDocumentoDetalleWO))
            {
                var disponible = estadosAnticipos[a].Estado == EstadoPartidaCuentaPorPagar.Disponible;
                var valor = disponible ? Math.Min(a.Saldo, Math.Max(0, restante)) : 0;
                restante -= valor;
                anticipos.Add(new BorradorAnticipoDto(a.IdDocumentoDetalleWO!.Value, $"{a.Prefijo} {a.NumeroDocumento}".Trim(), a.Nota, valor, disponible));
            }

            var totalAnticipos = anticipos.Sum(a => a.Valor);
            var nombre = b.NombreTercero ?? partidas.FirstOrDefault()?.NombreTercero ?? b.Identificacion;
            resultado.Add(new BorradorProveedorDto(b.Identificacion, nombre, facturas, anticipos, totalFacturas, totalAnticipos,
                totalFacturas - totalAnticipos, problemas.Count == 0 ? null : string.Join(" ", problemas)));
        }
        return resultado;
    }

    private static List<int> ParsearIds(string ids) =>
        ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var n) ? n : (int?)null)
            .Where(n => n is not null).Select(n => n!.Value).Distinct().ToList();

    // =====================================================================================
    // Pago extraordinario (2026-11-09): giro a un tercero sin factura (lo pide Gerencia
    // Comercial cuando lo necesita). El tipo de giro decide la cuenta que se debita en el CE.
    // =====================================================================================

    public async Task<IReadOnlyList<TipoGiroDto>> ObtenerTiposGiroAsync(CancellationToken ct = default) =>
        await db.TiposGiro.AsNoTracking().Where(t => t.Activo).OrderBy(t => t.Orden).ThenBy(t => t.Nombre)
            .Select(t => new TipoGiroDto(t.Id, t.Nombre, t.EsAnticipo))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TerceroWODto>> BuscarTercerosAsync(string texto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        texto = (texto ?? "").Trim();
        if (texto.Length < 2) return [];

        return await db.TercerosWO.AsNoTracking()
            .Where(t => t.Identificacion.StartsWith(texto) || t.Nombre.Contains(texto))
            .OrderBy(t => t.Nombre)
            .Take(20)
            .Select(t => new TerceroWODto(t.IdTerceroWO, t.Identificacion, t.Nombre))
            .ToListAsync(ct);
    }

    public async Task<int> CrearPagoExtraordinarioAsync(CrearPagoExtraordinarioDto dto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        if (dto.Valor <= 0)
            throw new InvalidOperationException("El monto tiene que ser mayor que 0.");
        var concepto = (dto.Concepto ?? "").Trim();
        if (concepto.Length == 0)
            throw new InvalidOperationException("Escribí el concepto del pago.");
        if (concepto.Length > 300)
            throw new InvalidOperationException("El concepto no puede pasar de 300 caracteres.");

        var tercero = await db.TercerosWO.AsNoTracking().FirstOrDefaultAsync(t => t.IdTerceroWO == dto.IdTerceroWO, ct)
            ?? throw new InvalidOperationException("Ese tercero no está en la lista de World Office.");
        var tipoGiro = await db.TiposGiro.AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TipoGiroId && t.Activo, ct)
            ?? throw new InvalidOperationException("Elegí el tipo de giro.");

        var tipo = await db.TiposDocumento.FirstOrDefaultAsync(t => t.PrefijoWorldOffice == PrefijoPagoProveedor && t.Activo, ct)
            ?? throw new InvalidOperationException("No está configurado el tipo de documento \"Pago a Proveedor\".");
        var consecutivo = await db.InstanciasDocumento.CountAsync(i => i.TipoDocumentoId == tipo.Id, ct) + 1;

        var creado = await instanciaService.CrearAsync(new CrearInstanciaDocumentoDto(
            TipoDocumentoId: tipo.Id,
            NumeroReferencia: consecutivo.ToString(),
            Proveedor: tercero.Nombre,
            Valor: dto.Valor,
            FechaDocumento: Hoy,
            Datos: new Dictionary<string, string> { ["nit"] = tercero.Identificacion, ["observaciones"] = concepto },
            Renglones: [new RenglonInputDto(Codigo: tipoGiro.Nombre, Descripcion: concepto, Cantidad: 1, UnidadMedida: null, ValorUnitario: dto.Valor, PorcentajeIva: 0m)]),
            usuarioId, ct, notificar: false);

        db.PagosProveedor.Add(new PagoProveedor
        {
            InstanciaDocumentoId = creado.Id,
            Identificacion = tercero.Identificacion,
            EsExtraordinario = true,
            IdTerceroWO = tercero.IdTerceroWO,
            NombreTercero = tercero.Nombre,
            TipoGiroId = tipoGiro.Id,
            NombreTipoGiro = tipoGiro.Nombre,
            CodigoCuentaGiro = tipoGiro.CodigoCuenta,
            GiroEsAnticipo = tipoGiro.EsAnticipo,
            Concepto = concepto
        });
        await db.SaveChangesAsync(ct);

        var creador = await db.Usuarios.Where(u => u.Id == usuarioId).Select(u => u.Nombre).FirstOrDefaultAsync(ct);
        var cuerpo = $"<p>{Html(creador ?? "Gerencia Comercial")} pidió un <b>pago extraordinario</b>:</p>" +
                     "<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse\">" +
                     $"<tr><th style=\"text-align:left\">Tercero</th><td>{Html(tercero.Nombre)} ({Html(tercero.Identificacion)})</td></tr>" +
                     $"<tr><th style=\"text-align:left\">Valor</th><td><b>${dto.Valor:N0}</b></td></tr>" +
                     $"<tr><th style=\"text-align:left\">Tipo de giro</th><td>{Html(tipoGiro.Nombre)}</td></tr>" +
                     $"<tr><th style=\"text-align:left\">Concepto</th><td>{Html(concepto)}</td></tr></table>" +
                     "<p>Lo encontrás en el menú <b>Pagos</b>.</p>";
        await AvisarPagadoresAsync(creado.Id, $"[Jimaco Aprobaciones] Pago extraordinario a {tercero.Nombre} por ${dto.Valor:N0}", cuerpo, ct);

        return creado.Id;
    }

    // ---- Catálogo de terceros de WO (lo manda el Sincronizador cada pocas horas) ----

    public async Task<DateTime?> ObtenerFechaCorteTercerosAsync(CancellationToken ct = default) =>
        await db.SincronizacionesCuentasPorPagar.Select(s => s.FechaCorteTerceros).FirstOrDefaultAsync(ct);

    public async Task GuardarTercerosAsync(IReadOnlyList<TerceroWOInputDto> terceros, CancellationToken ct = default)
    {
        var limpios = terceros
            .Where(t => !string.IsNullOrWhiteSpace(t.Identificacion))
            .GroupBy(t => t.IdTerceroWO)
            .Select(g => g.First())
            .Select(t => new TerceroWO
            {
                IdTerceroWO = t.IdTerceroWO,
                Identificacion = Recortar(t.Identificacion.Trim(), 30)!,
                Nombre = Recortar(string.IsNullOrWhiteSpace(t.Nombre) ? t.Identificacion.Trim() : t.Nombre.Trim(), 300)!
            })
            .ToList();

        if (db.Database.IsRelational())
            await db.TercerosWO.ExecuteDeleteAsync(ct);
        else
            db.TercerosWO.RemoveRange(await db.TercerosWO.ToListAsync(ct));
        db.TercerosWO.AddRange(limpios);

        var sincronizacion = await db.SincronizacionesCuentasPorPagar.FirstOrDefaultAsync(ct);
        if (sincronizacion is null)
        {
            sincronizacion = new SincronizacionCuentasPorPagar { FechaCorte = timeProvider.GetUtcNow().UtcDateTime };
            db.SincronizacionesCuentasPorPagar.Add(sincronizacion);
        }
        sincronizacion.FechaCorteTerceros = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Un correo a cada usuario activo con un rol activo que tenga Rol.PuedePagarCuentas.</summary>
    private async Task AvisarPagadoresAsync(int instanciaDocumentoId, string asunto, string cuerpoHtml, CancellationToken ct)
    {
        var pagadores = await db.UsuarioRoles
            .Where(ur => ur.Rol.Activo && ur.Rol.PuedePagarCuentas && ur.Usuario.Activo)
            .Select(ur => ur.UsuarioId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var pagadorId in pagadores)
        {
            try
            {
                await notificacionService.NotificarHtmlAsync(instanciaDocumentoId, pagadorId, asunto, cuerpoHtml, "/pagos", ct);
            }
            catch
            {
                // Un correo que falla no deshace la relación (ya quedó creada y visible en "Pagos").
            }
        }
    }

    private static string Html(string? texto) => System.Net.WebUtility.HtmlEncode(texto ?? "");

    private sealed record EstadoPartida(EstadoPartidaCuentaPorPagar Estado, int? InstanciaId, string? NumeroReferencia);

    /// <summary>
    /// Estado de cada partida frente a los pagos ya creados acá. Un pago Rechazado libera sus
    /// facturas; uno en curso (o devuelto) las bloquea; uno Completado las bloquea solo mientras WO
    /// siga mostrando el MISMO saldo (todavía no cruzó el CEJ) — si el saldo cambió, WO aplicó un
    /// abono parcial y lo que queda se puede volver a pagar. Excepción (2026-11-06): si el CE lo
    /// creó el sistema y la foto vigente es POSTERIOR a esa creación, WO ya tuvo que reflejarlo — si
    /// la factura sigue ahí con el mismo saldo, el CE se anuló o se borró en WO y vuelve a estar
    /// disponible (caso real: Contabilidad borra un CE de prueba CEJ-2 para repetir la prueba).
    /// </summary>
    private async Task<Dictionary<PartidaCuentaPorPagar, EstadoPartida>> CalcularEstadosAsync(IReadOnlyList<PartidaCuentaPorPagar> partidas, CancellationToken ct)
    {
        var ids = partidas.Where(p => p.IdDocumentoDetalleWO != null).Select(p => p.IdDocumentoDetalleWO!.Value).Distinct().ToList();

        var fechaCorte = await ObtenerFechaCorteAsync(ct);
        var pagos = await db.FacturasPagoProveedor.AsNoTracking()
            .Where(f => ids.Contains(f.IdDocumentoDetalleWO) && f.InstanciaDocumento.Estado != EstadoInstanciaDocumento.Rechazado)
            .Select(f => new
            {
                f.IdDocumentoDetalleWO, f.Valor, f.InstanciaDocumentoId, f.InstanciaDocumento.Estado, f.InstanciaDocumento.NumeroReferencia,
                FechaCreacionCE = f.InstanciaDocumento.PagoProveedor != null ? f.InstanciaDocumento.PagoProveedor.FechaCreacionCE : null
            })
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
                .FirstOrDefault(x => x.Estado != EstadoInstanciaDocumento.Completado
                                     || (x.Valor == p.Pendiente && !CEYaReflejadoEnFoto(x.FechaCreacionCE, fechaCorte)));

            resultado[p] = pago switch
            {
                null => new(EstadoPartidaCuentaPorPagar.Disponible, null, null),
                { Estado: EstadoInstanciaDocumento.Completado } => new(EstadoPartidaCuentaPorPagar.PagadaPendienteCruce, pago.InstanciaDocumentoId, pago.NumeroReferencia),
                _ => new(EstadoPartidaCuentaPorPagar.EnTramite, pago.InstanciaDocumentoId, pago.NumeroReferencia)
            };
        }
        return resultado;
    }

    /// <summary>
    /// Mismo criterio que <see cref="CalcularEstadosAsync"/> para los anticipos: bloqueado mientras
    /// esté en un pago en curso, o en uno completado cuyo CE WO todavía no reflejó (el anticipo
    /// sigue con el mismo saldo que tenía al decidir el pago).
    /// </summary>
    private async Task<Dictionary<AnticipoProveedor, EstadoPartida>> CalcularEstadosAnticiposAsync(IReadOnlyList<AnticipoProveedor> anticipos, CancellationToken ct)
    {
        var ids = anticipos.Where(a => a.IdDocumentoDetalleWO != null).Select(a => a.IdDocumentoDetalleWO!.Value).Distinct().ToList();

        var fechaCorte = await ObtenerFechaCorteAsync(ct);
        var usos = await db.AnticiposPagoProveedor.AsNoTracking()
            .Where(a => ids.Contains(a.IdDocumentoDetalleWO) && a.InstanciaDocumento.Estado != EstadoInstanciaDocumento.Rechazado)
            .Select(a => new
            {
                a.IdDocumentoDetalleWO, a.SaldoAnterior, a.InstanciaDocumentoId, a.InstanciaDocumento.Estado, a.InstanciaDocumento.NumeroReferencia,
                FechaCreacionCE = a.InstanciaDocumento.PagoProveedor != null ? a.InstanciaDocumento.PagoProveedor.FechaCreacionCE : null
            })
            .ToListAsync(ct);

        var resultado = new Dictionary<AnticipoProveedor, EstadoPartida>(ReferenceEqualityComparer.Instance);
        foreach (var a in anticipos)
        {
            if (a.IdDocumentoDetalleWO is null || a.Saldo <= 0)
            {
                resultado[a] = new(EstadoPartidaCuentaPorPagar.NoPagable, null, null);
                continue;
            }

            var uso = usos
                .Where(x => x.IdDocumentoDetalleWO == a.IdDocumentoDetalleWO)
                .OrderByDescending(x => x.InstanciaDocumentoId)
                .FirstOrDefault(x => x.Estado != EstadoInstanciaDocumento.Completado
                                     || (x.SaldoAnterior == a.Saldo && !CEYaReflejadoEnFoto(x.FechaCreacionCE, fechaCorte)));

            resultado[a] = uso switch
            {
                null => new(EstadoPartidaCuentaPorPagar.Disponible, null, null),
                { Estado: EstadoInstanciaDocumento.Completado } => new(EstadoPartidaCuentaPorPagar.PagadaPendienteCruce, uso.InstanciaDocumentoId, uso.NumeroReferencia),
                _ => new(EstadoPartidaCuentaPorPagar.EnTramite, uso.InstanciaDocumentoId, uso.NumeroReferencia)
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

    /// <summary>true si el sistema creó el CE y la foto vigente de WO se tomó después — o sea, WO ya debería mostrarlo cruzado.</summary>
    private static bool CEYaReflejadoEnFoto(DateTime? fechaCreacionCE, DateTime? fechaCorte) =>
        fechaCreacionCE is { } creado && fechaCorte is { } corte && corte > creado;

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

    private static string? Recortar(string? texto, int maximo) =>
        texto is null || texto.Length <= maximo ? texto : texto[..maximo];

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
