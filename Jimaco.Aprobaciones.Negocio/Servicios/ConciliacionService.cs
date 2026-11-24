using System.IO.Compression;
using System.Text;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.Conciliacion;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Conciliación XML de proveedores ↔ OC de World Office (2026-11-24). Copia del módulo de
/// Ferrealiados Cotizaciones (CONTEXTO-MODULO-CONCILIACION.md) con las adiciones de Jimaco: emisor de
/// cada OC, observaciones + marca "Revisada" en facturas Parciales / Con diferencia y descarga de los
/// XML a demanda. Los datos los sube el Sincronizador (red de Jimaco, junto a WO); el cruce se
/// calcula al vuelo con <see cref="MotorConciliacion"/> en cada consulta.
/// </summary>
public class ConciliacionService(AppDbContext db, TimeProvider reloj) : IConciliacionService
{
    private static readonly ReglasConciliacion Reglas = new();

    /// <summary>NIT de Jimaco (900.632.353-1, sin DV), el receptor de las facturas de compra.</summary>
    public const string NitJimaco = "900632353";

    /// <summary>Tope de facturas por descarga de XML (un ZIP de 500 facturas ronda unos pocos MB).</summary>
    public const int MaximoXmlPorDescarga = 500;

    // ======================== Lo que sube el Sincronizador ========================

    /// <summary>Correo de facturación o --carpeta. No valida receptor: ese buzón solo recibe facturas de compra.</summary>
    public async Task<RegistroFacturasResultadoDto> RegistrarFacturasAsync(IReadOnlyList<FacturaXmlEntradaDto> facturas, CancellationToken ct = default)
    {
        var detalle = await RegistrarAsync(facturas, origenForzado: null, validarReceptor: false, ct);
        return new RegistroFacturasResultadoDto(
            detalle.Count(d => d.Estado == EstadoCarga.Nueva),
            detalle.Count(d => d.Estado == EstadoCarga.Duplicada),
            detalle.Count(d => d.Estado == EstadoCarga.NoReconocida));
    }

    /// <summary>
    /// ZIP/XML descargados de la DIAN y subidos desde la pantalla. Aquí sí se valida que la factura
    /// sea para Jimaco: la persona puede equivocarse de archivo.
    /// </summary>
    public async Task<CargaArchivosResultadoDto> CargarArchivosAsync(IReadOnlyList<ArchivoCargado> archivos, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        var xmls = new List<FacturaXmlEntradaDto>();
        var avisos = new List<string>();
        var ahora = reloj.GetUtcNow().UtcDateTime;

        foreach (var archivo in archivos)
        {
            if (!ExtractorXmlFacturas.EsArchivoSoportado(archivo.Nombre))
            {
                avisos.Add($"{archivo.Nombre}: solo se aceptan archivos .zip o .xml, se omite.");
                continue;
            }

            var antes = xmls.Count;
            var avisosAntes = avisos.Count;
            ExtractorXmlFacturas.Extraer(archivo.Nombre, archivo.Contenido, $"Carga DIAN: {archivo.Nombre}", ahora, xmls, avisos);
            if (xmls.Count == antes && avisos.Count == avisosAntes)
                avisos.Add($"{archivo.Nombre}: no trae ningún XML.");
        }

        var detalle = await RegistrarAsync(xmls, OrigenFactura.Dian, validarReceptor: true, ct);
        return new CargaArchivosResultadoDto(
            archivos.Count,
            detalle.Count(d => d.Estado == EstadoCarga.Nueva),
            detalle.Count(d => d.Estado == EstadoCarga.Duplicada),
            detalle.Count(d => d.Estado == EstadoCarga.NoReconocida),
            detalle.Count(d => d.Estado == EstadoCarga.OtroReceptor),
            detalle,
            avisos);
    }

    private async Task<List<FacturaCargadaDto>> RegistrarAsync(
        IReadOnlyList<FacturaXmlEntradaDto> facturas, string? origenForzado, bool validarReceptor, CancellationToken ct)
    {
        var detalle = new List<FacturaCargadaDto>();
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var origenEnLote = new Dictionary<string, string>();

        foreach (var entrada in facturas)
        {
            FacturaElectronica? leida;
            try
            {
                leida = LectorFacturaElectronica.LeerXml(entrada.Xml);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or FormatException)
            {
                leida = null;
            }

            if (leida is null || string.IsNullOrWhiteSpace(leida.Cufe))
            {
                detalle.Add(new FacturaCargadaDto(entrada.NombreArchivo, null, null, null, null, null, EstadoCarga.NoReconocida, null));
                continue;
            }

            FacturaCargadaDto Renglon(string estado, string? origenExistente = null) => new(
                entrada.NombreArchivo, leida.Numero, leida.NitEmisor, leida.NombreEmisor, leida.FechaEmision, leida.Total,
                estado, origenExistente);

            if (validarReceptor && leida.NitReceptor != NitJimaco)
            {
                detalle.Add(Renglon(EstadoCarga.OtroReceptor));
                continue;
            }

            var existente = origenEnLote.GetValueOrDefault(leida.Cufe)
                            ?? await db.FacturasProveedor.Where(f => f.Cufe == leida.Cufe).Select(f => f.Origen).FirstOrDefaultAsync(ct);
            if (existente is not null)
            {
                detalle.Add(Renglon(EstadoCarga.Duplicada, existente));
                continue;
            }

            var origen = origenForzado
                         ?? (entrada.CorreoAsunto?.StartsWith("Carga desde carpeta", StringComparison.OrdinalIgnoreCase) == true
                             ? OrigenFactura.Carpeta
                             : OrigenFactura.Correo);
            origenEnLote[leida.Cufe] = origen;

            db.FacturasProveedor.Add(new FacturaProveedor
            {
                Cufe = Truncar(leida.Cufe, 200),
                Numero = Truncar(leida.Numero, 50),
                TipoDocumento = Truncar(leida.TipoDocumento, 5),
                FechaEmision = leida.FechaEmision,
                NitEmisor = Truncar(leida.NitEmisor, 20),
                NombreEmisor = Truncar(leida.NombreEmisor, 300),
                NitReceptor = Truncar(leida.NitReceptor, 20),
                Subtotal = leida.Subtotal,
                Iva = leida.Iva,
                Total = leida.Total,
                ReferenciaOrden = leida.ReferenciaOrden is null ? null : Truncar(leida.ReferenciaOrden, 100),
                XmlGzip = XmlComprimido.Comprimir(entrada.Xml),
                NombreArchivo = entrada.NombreArchivo is null ? null : Truncar(entrada.NombreArchivo, 300),
                CorreoAsunto = entrada.CorreoAsunto is null ? null : Truncar(entrada.CorreoAsunto, 500),
                CorreoFecha = entrada.CorreoFecha,
                FechaRegistro = ahora,
                Origen = origen,
                Renglones = leida.Renglones.Select((r, i) => new FacturaProveedorRenglon
                {
                    Orden = i + 1,
                    Descripcion = Truncar(r.Descripcion, 500),
                    Cantidad = r.Cantidad,
                    Subtotal = r.Subtotal,
                    PorcentajeIva = r.PorcentajeIva,
                }).ToList(),
            });
            detalle.Add(Renglon(EstadoCarga.Nueva));
        }

        await db.SaveChangesAsync(ct);
        return detalle;
    }

    public async Task<SincronizacionOrdenesResultadoDto> SincronizarOrdenesAsync(IReadOnlyList<OrdenCompraEntradaDto> ordenes, CancellationToken ct = default)
    {
        var ids = ordenes.Select(o => o.IdAsientoContable).ToList();
        var existentes = await db.OrdenesCompraWorldOffice
            .Include(o => o.Renglones)
            .Where(o => ids.Contains(o.IdAsientoContable))
            .ToDictionaryAsync(o => o.IdAsientoContable, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;
        int nuevas = 0, actualizadas = 0;

        foreach (var entrada in ordenes)
        {
            if (!existentes.TryGetValue(entrada.IdAsientoContable, out var oc))
            {
                oc = new OrdenCompraWorldOffice
                {
                    IdAsientoContable = entrada.IdAsientoContable,
                    NitProveedor = "",
                    NombreProveedor = "",
                };
                db.OrdenesCompraWorldOffice.Add(oc);
                existentes[entrada.IdAsientoContable] = oc;
                nuevas++;
            }

            // Solo se tocan las OC que cambiaron (en Ferrealiados reinsertar todo cada 10 minutos
            // infló el log de transacciones de SQL Server a 19 GB).
            var renglones = entrada.Renglones.Select((r, i) => new OrdenCompraWorldOfficeRenglon
            {
                Orden = i + 1,
                Codigo = r.Codigo is null ? null : Truncar(r.Codigo, 100),
                Descripcion = Truncar(r.Descripcion, 500),
                Cantidad = Math.Round(r.Cantidad, 4),
                ValorUnitario = Math.Round(r.ValorUnitario, 4),
                PorcentajeIva = Math.Round(r.PorcentajeIva, 2),
            }).ToList();
            var cambiaronRenglones = oc.Id == 0 || !MismosRenglones(oc.Renglones, renglones);
            if (cambiaronRenglones && oc.Id != 0)
            {
                db.OrdenCompraWorldOfficeRenglones.RemoveRange(oc.Renglones);
                oc.Renglones.Clear();
            }

            // Costo = Cantidad × ValorUnitario (MontoMonetarioUnitario de WO); IVA por renglón con el %
            // del producto (la línea de WO no trae su propio porcentaje).
            oc.Numero = entrada.Numero;
            oc.Fecha = entrada.Fecha;
            oc.NitProveedor = Truncar(LectorFacturaElectronica.NormalizarNit(entrada.NitProveedor), 20);
            oc.NombreProveedor = Truncar(entrada.NombreProveedor, 300);
            oc.Emisor = string.IsNullOrWhiteSpace(entrada.Emisor) ? null : Truncar(entrada.Emisor.Trim(), 300);
            oc.Anulada = entrada.Anulada;
            oc.Subtotal = Math.Round(entrada.Renglones.Sum(r => r.Cantidad * r.ValorUnitario), 2);
            oc.Iva = Math.Round(entrada.Renglones.Sum(r => r.Cantidad * r.ValorUnitario * r.PorcentajeIva / 100m), 2);
            if (cambiaronRenglones)
                foreach (var r in renglones)
                    oc.Renglones.Add(r);

            if (oc.Id == 0)
            {
                oc.FechaSincronizacion = ahora;
            }
            else if (cambiaronRenglones || db.Entry(oc).State == EntityState.Modified)
            {
                oc.FechaSincronizacion = ahora;
                actualizadas++;
            }
        }

        var marca = await db.SincronizacionConciliacion.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (marca is null)
            db.SincronizacionConciliacion.Add(new SincronizacionConciliacion { Id = 1, UltimaSincronizacionOrdenes = ahora });
        else
            marca.UltimaSincronizacionOrdenes = ahora;

        await db.SaveChangesAsync(ct);
        return new SincronizacionOrdenesResultadoDto(nuevas, actualizadas);
    }

    public async Task<SincronizacionFacturasCompraResultadoDto> SincronizarFacturasCompraAsync(
        SincronizacionFacturasCompraEntradaDto entrada, CancellationToken ct = default)
    {
        var existentes = await db.FacturasCompraWorldOffice
            .Where(f => f.Fecha >= entrada.Desde)
            .ToDictionaryAsync(f => f.IdAsientoContable, ct);
        var ids = entrada.Facturas.Select(f => f.IdAsientoContable).ToHashSet();
        var fueraDeVentana = await db.FacturasCompraWorldOffice
            .Where(f => f.Fecha < entrada.Desde && ids.Contains(f.IdAsientoContable))
            .ToListAsync(ct);
        foreach (var f in fueraDeVentana) existentes[f.IdAsientoContable] = f;

        var ahora = reloj.GetUtcNow().UtcDateTime;
        int nuevas = 0, actualizadas = 0;
        foreach (var e in entrada.Facturas)
        {
            if (!existentes.TryGetValue(e.IdAsientoContable, out var fc))
            {
                fc = new FacturaCompraWorldOffice { IdAsientoContable = e.IdAsientoContable, NitProveedor = "", NombreProveedor = "" };
                db.FacturasCompraWorldOffice.Add(fc);
                existentes[e.IdAsientoContable] = fc;
                nuevas++;
            }

            fc.Numero = e.Numero;
            fc.Fecha = e.Fecha;
            fc.NitProveedor = Truncar(LectorFacturaElectronica.NormalizarNit(e.NitProveedor), 20);
            fc.NombreProveedor = Truncar(e.NombreProveedor, 300);
            fc.DocumentoExterno = string.IsNullOrWhiteSpace(e.DocumentoExterno) ? null : Truncar(e.DocumentoExterno.Trim(), 100);
            fc.ReferenciaOrdenes = string.IsNullOrWhiteSpace(e.ReferenciaOrdenes) ? null : Truncar(e.ReferenciaOrdenes.Trim(), 200);
            fc.Anulada = e.Anulada;
            if (fc.Id == 0)
            {
                fc.FechaSincronizacion = ahora;
            }
            else if (db.Entry(fc).State == EntityState.Modified)
            {
                fc.FechaSincronizacion = ahora;
                actualizadas++;
            }
        }

        // Las de la ventana que ya no vinieron se borraron en WO.
        var borradas = existentes.Values.Where(f => f.Id != 0 && f.Fecha >= entrada.Desde && !ids.Contains(f.IdAsientoContable)).ToList();
        db.FacturasCompraWorldOffice.RemoveRange(borradas);

        await db.SaveChangesAsync(ct);
        return new SincronizacionFacturasCompraResultadoDto(nuevas, actualizadas, borradas.Count);
    }

    // ======================== Pantalla ========================

    public async Task<ConciliacionDto> ConciliarAsync(DateOnly desde, DateOnly hasta, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var calculo = await CalcularAsync(desde, hasta, ct);
        var resultado = calculo.Resultado;

        var enRango = resultado.Facturas
            .Where(r => r.Factura.FechaEmision >= desde && r.Factura.FechaEmision <= hasta)
            .ToList();
        var idsFacturas = enRango.Select(r => calculo.FacturaPorCufe[r.Factura.Cufe].Id).ToList();
        var observacionesFacturas = await HistorialesFacturaAsync(idsFacturas, ct);

        var facturas = enRango
            .Select(r =>
            {
                var factura = calculo.FacturaPorCufe[r.Factura.Cufe];
                observacionesFacturas.TryGetValue(factura.Id, out var historial);
                return MapFactura(r, factura, calculo.Emisores, historial ?? []);
            })
            .OrderByDescending(f => f.FechaEmision).ThenBy(f => f.NombreProveedor)
            .ToList();

        // Solo las OC del rango consultado: las de la ventana anterior sirven de contexto para el cruce,
        // pero mostrarlas como "sin factura" confunde (en Ferrealiados salían 773, todas falsas).
        var ordenesPendientes = resultado.OrdenesPendientes
            .Where(p => p.OrdenCompra.Fecha >= desde && p.OrdenCompra.Fecha <= hasta)
            .ToList();
        var idsPendientes = ordenesPendientes.Select(p => p.OrdenCompra.IdAsientoContable).ToList();
        var observaciones = (await db.ObservacionesOrdenCompra.AsNoTracking()
                .Where(o => idsPendientes.Contains(o.IdAsientoContable))
                .ToListAsync(ct))
            .GroupBy(o => o.IdAsientoContable)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Fecha).ThenBy(o => o.Id).ToList());

        var pendientes = ordenesPendientes
            .Select(p =>
            {
                observaciones.TryGetValue(p.OrdenCompra.IdAsientoContable, out var historial);
                historial ??= [];
                return new OrdenPendienteDto(p.OrdenCompra.Numero, p.OrdenCompra.Fecha, p.OrdenCompra.NitProveedor,
                    p.OrdenCompra.NombreProveedor, p.OrdenCompra.Subtotal, p.OrdenCompra.Iva, p.SaldoSubtotal,
                    p.OrdenCompra.IdAsientoContable,
                    historial.Count,
                    historial.LastOrDefault()?.Texto,
                    NoRequiereFactura(historial),
                    calculo.Emisores.GetValueOrDefault(p.OrdenCompra.IdAsientoContable));
            })
            .OrderByDescending(p => p.Fecha)
            .ToList();

        var facturadasSinXml = resultado.OrdenesFacturadasSinXml
            .Where(p => p.OrdenCompra.Fecha >= desde && p.OrdenCompra.Fecha <= hasta)
            .Select(p => new OrdenFacturadaSinXmlDto(p.OrdenCompra.Numero, p.OrdenCompra.Fecha, p.OrdenCompra.NitProveedor,
                p.OrdenCompra.NombreProveedor, p.OrdenCompra.Subtotal, p.OrdenCompra.Iva,
                p.FacturaCompra.Numero, p.FacturaCompra.Fecha, p.FacturaCompra.DocumentoExterno,
                calculo.Emisores.GetValueOrDefault(p.OrdenCompra.IdAsientoContable)))
            .OrderByDescending(p => p.Fecha)
            .ToList();

        var invalidas = resultado.ReferenciasInvalidas
            .Where(r => r.FacturaCompra.Fecha >= desde && r.FacturaCompra.Fecha <= hasta)
            .Select(r => new ReferenciaOrdenInvalidaDto(r.FacturaCompra.Numero, r.FacturaCompra.Fecha, r.FacturaCompra.NitProveedor,
                r.FacturaCompra.NombreProveedor, r.FacturaCompra.DocumentoExterno, r.FacturaCompra.ReferenciaOrdenes,
                r.NumeroOrdenCitada, r.Motivo))
            .OrderByDescending(r => r.Fecha).ThenBy(r => r.FacturaCompraNumero)
            .ToList();

        var resumen = new ResumenConciliacionDto(
            facturas.Count(f => f.Estado == EstadoConciliacion.Cuadra),
            facturas.Count(f => f.Estado == EstadoConciliacion.Diferencia),
            facturas.Count(f => f.Estado == EstadoConciliacion.Parcial),
            facturas.Count(f => f.Estado == EstadoConciliacion.SinOrdenCompra),
            pendientes.Count,
            facturadasSinXml.Count,
            invalidas.Count);

        var ultimaSincronizacion = await db.SincronizacionConciliacion.AsNoTracking()
            .Where(s => s.Id == 1).Select(s => s.UltimaSincronizacionOrdenes).FirstOrDefaultAsync(ct);
        var ultimaFactura = await db.FacturasProveedor.MaxAsync(f => (DateTime?)f.FechaRegistro, ct);

        return new ConciliacionDto(desde, hasta, resumen, facturas, pendientes, facturadasSinXml, invalidas, ultimaSincronizacion, ultimaFactura);
    }

    public async Task<DetalleFacturaConciliadaDto?> ObtenerDetalleAsync(int facturaId, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var factura = await db.FacturasProveedor.AsNoTracking()
            .Include(f => f.Renglones)
            .FirstOrDefaultAsync(f => f.Id == facturaId, ct);
        if (factura is null) return null;

        // Mismo contexto que la pantalla: las facturas anteriores del proveedor afectan los saldos.
        var calculo = await CalcularAsync(factura.FechaEmision, factura.FechaEmision, ct);
        var conciliada = calculo.Resultado.Facturas.FirstOrDefault(r => r.Factura.Cufe == factura.Cufe);
        if (conciliada is null) return null;

        var idsOc = conciliada.TodasLasOrdenes.Select(o => o.IdAsientoContable).ToList();
        var renglonesOc = new List<RenglonComparacionDto>();
        if (idsOc.Count > 0)
        {
            renglonesOc = await db.OrdenCompraWorldOfficeRenglones.AsNoTracking()
                .Where(r => idsOc.Contains(r.OrdenCompraWorldOffice!.IdAsientoContable))
                .OrderBy(r => r.OrdenCompraWorldOffice!.Numero).ThenBy(r => r.Orden)
                .Select(r => new RenglonComparacionDto(r.Codigo, r.Descripcion, r.Cantidad, r.ValorUnitario,
                    Math.Round(r.Cantidad * r.ValorUnitario, 2), r.PorcentajeIva))
                .ToListAsync(ct);
        }

        var historial = (await HistorialesFacturaAsync([factura.Id], ct)).GetValueOrDefault(factura.Id) ?? [];
        return new DetalleFacturaConciliadaDto(
            MapFactura(conciliada, calculo.FacturaPorCufe[factura.Cufe], calculo.Emisores, historial),
            factura.Renglones.OrderBy(r => r.Orden)
                .Select(r => new RenglonComparacionDto(null, r.Descripcion, r.Cantidad,
                    r.Cantidad == 0 ? r.Subtotal : Math.Round(r.Subtotal / r.Cantidad, 2), r.Subtotal, r.PorcentajeIva))
                .ToList(),
            renglonesOc);
    }

    // ---------------- Descarga de XML a demanda (adición de Jimaco) ----------------

    /// <summary>
    /// Una factura → su XML tal cual llegó. Varias → un ZIP con un XML por factura, llamado
    /// PROVEEDOR_NUMERO.xml. Son los XML guardados en el sistema (correo o ZIP de la DIAN).
    /// </summary>
    public async Task<ArchivoDescargaDto> DescargarXmlAsync(IReadOnlyList<int> facturaIds, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var ids = facturaIds.Distinct().ToList();
        if (ids.Count == 0)
            throw new ArgumentException("Elegí al menos una factura.");
        if (ids.Count > MaximoXmlPorDescarga)
            throw new ArgumentException($"Se pueden descargar máximo {MaximoXmlPorDescarga} XML a la vez.");

        var facturas = await db.FacturasProveedor.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.NombreEmisor, f.Numero, f.XmlGzip, f.FechaEmision })
            .ToListAsync(ct);
        if (facturas.Count == 0)
            throw new KeyNotFoundException("No se encontraron las facturas.");

        if (facturas.Count == 1)
        {
            var f = facturas[0];
            return new ArchivoDescargaDto(NombreXml(f.NombreEmisor, f.Numero), "application/xml",
                Encoding.UTF8.GetBytes(XmlComprimido.Descomprimir(f.XmlGzip)));
        }

        using var salida = new MemoryStream();
        using (var zip = new ZipArchive(salida, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in facturas.OrderBy(f => f.NombreEmisor).ThenBy(f => f.FechaEmision).ThenBy(f => f.Numero))
            {
                var nombre = NombreXml(f.NombreEmisor, f.Numero);
                for (var i = 2; !usados.Add(nombre); i++)
                    nombre = NombreXml(f.NombreEmisor, $"{f.Numero}_{i}");

                var entrada = zip.CreateEntry(nombre, CompressionLevel.Optimal);
                await using var destino = entrada.Open();
                var bytes = Encoding.UTF8.GetBytes(XmlComprimido.Descomprimir(f.XmlGzip));
                await destino.WriteAsync(bytes, ct);
            }
        }

        var hoy = HoyColombia(reloj);
        return new ArchivoDescargaDto($"facturas-xml-{hoy:yyyy-MM-dd}-{facturas.Count}.zip", "application/zip", salida.ToArray());
    }

    public static string NombreXml(string proveedor, string numero)
    {
        var invalidos = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        string Limpiar(string texto) => new string(texto.Trim().Select(c => invalidos.Contains(c) ? ' ' : c).ToArray()).Trim();
        var nombre = Limpiar(proveedor);
        if (nombre.Length > 80) nombre = nombre[..80].Trim();
        if (nombre.Length == 0) nombre = "PROVEEDOR";
        return $"{nombre}_{Limpiar(numero)}.xml";
    }

    // ---------------- Observaciones de las OC sin factura ----------------

    public async Task<ObservacionesOrdenDto?> ObtenerObservacionesOrdenAsync(int idAsientoContable, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        return await ObservacionesOrdenAsync(idAsientoContable, ct);
    }

    private async Task<ObservacionesOrdenDto?> ObservacionesOrdenAsync(int idAsientoContable, CancellationToken ct)
    {
        var oc = await db.OrdenesCompraWorldOffice.AsNoTracking().FirstOrDefaultAsync(o => o.IdAsientoContable == idAsientoContable, ct);
        if (oc is null) return null;

        var historial = await db.ObservacionesOrdenCompra.AsNoTracking()
            .Where(o => o.IdAsientoContable == idAsientoContable)
            .OrderBy(o => o.Fecha).ThenBy(o => o.Id)
            .ToListAsync(ct);

        return new ObservacionesOrdenDto(oc.IdAsientoContable, oc.Numero, oc.Fecha, oc.NitProveedor, oc.NombreProveedor,
            oc.Subtotal, oc.Iva, NoRequiereFactura(historial),
            historial
                .OrderByDescending(o => o.Fecha).ThenByDescending(o => o.Id)
                .Select(o => new ObservacionOrdenDto(o.Id, o.Tipo.ToString(), o.Texto, o.NombreUsuario, o.Fecha))
                .ToList(),
            oc.Emisor);
    }

    public async Task<ObservacionesOrdenDto?> AgregarObservacionOrdenAsync(int idAsientoContable, NuevaObservacionOrdenDto dto,
        int usuarioId, CancellationToken ct = default)
    {
        var usuario = await ExigirPermisoAsync(usuarioId, ct);
        var texto = ValidarTexto(dto.Texto);

        var oc = await db.OrdenesCompraWorldOffice.AsNoTracking().FirstOrDefaultAsync(o => o.IdAsientoContable == idAsientoContable, ct);
        if (oc is null) return null;

        var historial = await db.ObservacionesOrdenCompra.AsNoTracking()
            .Where(o => o.IdAsientoContable == idAsientoContable)
            .ToListAsync(ct);
        var marcada = NoRequiereFactura(historial);

        var tipo = dto.Accion switch
        {
            AccionObservacionOrden.MarcarNoRequiereFactura when marcada =>
                throw new ArgumentException("Esta OC ya está marcada como que no requiere factura."),
            AccionObservacionOrden.QuitarMarcaNoRequiereFactura when !marcada =>
                throw new ArgumentException("Esta OC no está marcada como que no requiere factura."),
            AccionObservacionOrden.MarcarNoRequiereFactura => TipoObservacionOrden.MarcaNoRequiereFactura,
            AccionObservacionOrden.QuitarMarcaNoRequiereFactura => TipoObservacionOrden.QuitaMarcaNoRequiereFactura,
            _ => TipoObservacionOrden.Observacion
        };

        db.ObservacionesOrdenCompra.Add(new ObservacionOrdenCompra
        {
            IdAsientoContable = idAsientoContable,
            NumeroOrden = oc.Numero,
            Tipo = tipo,
            Texto = texto,
            UsuarioId = usuario.Id,
            NombreUsuario = Truncar(usuario.Nombre, 200),
            Fecha = reloj.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        return await ObservacionesOrdenAsync(idAsientoContable, ct);
    }

    // La marca vigente es la última acción de marcar/quitar (las observaciones simples no la cambian).
    private static bool NoRequiereFactura(IEnumerable<ObservacionOrdenCompra> historial)
        => historial
            .Where(o => o.Tipo != TipoObservacionOrden.Observacion)
            .OrderBy(o => o.Fecha).ThenBy(o => o.Id)
            .LastOrDefault()?.Tipo == TipoObservacionOrden.MarcaNoRequiereFactura;

    // ---------------- Observaciones de facturas Parciales / Con diferencia (adición de Jimaco) ----------------

    public async Task<ObservacionesFacturaDto?> ObtenerObservacionesFacturaAsync(int facturaId, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        return await ObservacionesFacturaAsync(facturaId, ct);
    }

    private async Task<ObservacionesFacturaDto?> ObservacionesFacturaAsync(int facturaId, CancellationToken ct)
    {
        var factura = await db.FacturasProveedor.AsNoTracking()
            .Where(f => f.Id == facturaId)
            .Select(f => new { f.Id, f.Numero, f.FechaEmision, f.NitEmisor, f.NombreEmisor, f.Subtotal, f.Iva })
            .FirstOrDefaultAsync(ct);
        if (factura is null) return null;

        var historial = (await HistorialesFacturaAsync([facturaId], ct)).GetValueOrDefault(facturaId) ?? [];
        return new ObservacionesFacturaDto(factura.Id, factura.Numero, factura.FechaEmision, factura.NitEmisor, factura.NombreEmisor,
            factura.Subtotal, factura.Iva, Revisada(historial),
            historial
                .OrderByDescending(o => o.Fecha).ThenByDescending(o => o.Id)
                .Select(o => new ObservacionOrdenDto(o.Id, o.Tipo.ToString(), o.Texto, o.NombreUsuario, o.Fecha))
                .ToList());
    }

    /// <summary>
    /// Agrega una observación (o marca / quita "Revisada") a una factura. No se restringe por estado
    /// en el backend: el estado se calcula al vuelo y una factura puede pasar de Diferencia a Cuadra
    /// si corrigen la OC en WO; su historial se conserva igual. La pantalla lo ofrece en Parciales y
    /// Con diferencia, que es donde tiene sentido.
    /// </summary>
    public async Task<ObservacionesFacturaDto?> AgregarObservacionFacturaAsync(int facturaId, NuevaObservacionFacturaDto dto,
        int usuarioId, CancellationToken ct = default)
    {
        var usuario = await ExigirPermisoAsync(usuarioId, ct);
        var texto = ValidarTexto(dto.Texto);

        if (!await db.FacturasProveedor.AnyAsync(f => f.Id == facturaId, ct))
            return null;

        var historial = await db.ObservacionesFacturaProveedor.AsNoTracking()
            .Where(o => o.FacturaProveedorId == facturaId)
            .ToListAsync(ct);
        var revisada = Revisada(historial);

        var tipo = dto.Accion switch
        {
            AccionObservacionFactura.MarcarRevisada when revisada =>
                throw new ArgumentException("Esta factura ya está marcada como revisada."),
            AccionObservacionFactura.QuitarMarcaRevisada when !revisada =>
                throw new ArgumentException("Esta factura no está marcada como revisada."),
            AccionObservacionFactura.MarcarRevisada => TipoObservacionFactura.MarcaRevisada,
            AccionObservacionFactura.QuitarMarcaRevisada => TipoObservacionFactura.QuitaMarcaRevisada,
            _ => TipoObservacionFactura.Observacion
        };

        db.ObservacionesFacturaProveedor.Add(new ObservacionFacturaProveedor
        {
            FacturaProveedorId = facturaId,
            Tipo = tipo,
            Texto = texto,
            UsuarioId = usuario.Id,
            NombreUsuario = Truncar(usuario.Nombre, 200),
            Fecha = reloj.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        return await ObservacionesFacturaAsync(facturaId, ct);
    }

    private static bool Revisada(IEnumerable<ObservacionFacturaProveedor> historial)
        => historial
            .Where(o => o.Tipo != TipoObservacionFactura.Observacion)
            .OrderBy(o => o.Fecha).ThenBy(o => o.Id)
            .LastOrDefault()?.Tipo == TipoObservacionFactura.MarcaRevisada;

    private async Task<Dictionary<int, List<ObservacionFacturaProveedor>>> HistorialesFacturaAsync(IReadOnlyCollection<int> facturaIds, CancellationToken ct)
    {
        if (facturaIds.Count == 0) return [];
        return (await db.ObservacionesFacturaProveedor.AsNoTracking()
                .Where(o => facturaIds.Contains(o.FacturaProveedorId))
                .ToListAsync(ct))
            .GroupBy(o => o.FacturaProveedorId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Fecha).ThenBy(o => o.Id).ToList());
    }

    private static string ValidarTexto(string? texto)
    {
        var limpio = texto?.Trim() ?? "";
        if (limpio.Length == 0)
            throw new ArgumentException("Escribí la observación: qué se encontró o por qué se marca.");
        if (limpio.Length > 2000)
            throw new ArgumentException("La observación puede tener máximo 2000 caracteres.");
        return limpio;
    }

    // ======================== Cálculo ========================

    private sealed record Calculo(
        ResultadoConciliacion Resultado,
        Dictionary<string, (int Id, string Origen)> FacturaPorCufe,
        Dictionary<int, string> Emisores);

    /// <summary>
    /// Trae las facturas del rango más las de la ventana anterior (DiasAntes): una factura de hace un
    /// mes pudo consumir ya una OC, y sin ella esa OC volvería a parecer disponible.
    /// </summary>
    private async Task<Calculo> CalcularAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var desdeContexto = desde.AddDays(-Reglas.DiasAntes);

        var facturas = await db.FacturasProveedor.AsNoTracking()
            .Where(f => f.FechaEmision >= desdeContexto && f.FechaEmision <= hasta)
            .Select(f => new
            {
                f.Id, f.Cufe, f.Numero, f.TipoDocumento, f.FechaEmision, f.NitEmisor, f.NombreEmisor,
                f.NitReceptor, f.Subtotal, f.Iva, f.Total, f.ReferenciaOrden, f.Origen,
            })
            .ToListAsync(ct);

        // FCJ de WO (último recurso del cruce). Hasta 60 días después del rango: Contabilidad puede
        // registrar en noviembre la factura de una OC de octubre.
        var facturasCompra = await db.FacturasCompraWorldOffice.AsNoTracking()
            .Where(f => !f.Anulada && f.Fecha >= desdeContexto && f.Fecha <= hasta.AddDays(60) && f.ReferenciaOrdenes != null)
            .Select(f => new FacturaCompraConciliable(f.IdAsientoContable, f.Numero, f.Fecha, f.NitProveedor,
                f.NombreProveedor, f.DocumentoExterno, f.ReferenciaOrdenes))
            .ToListAsync(ct);
        var citadas = facturasCompra.SelectMany(f => f.OrdenesCitadas).Distinct().ToList();

        var desdeOrdenes = desdeContexto.AddDays(-Reglas.DiasAntes);
        var hastaOrdenes = hasta.AddDays(Reglas.DiasDespues);
        var ordenes = await db.OrdenesCompraWorldOffice.AsNoTracking()
            .Where(o => !o.Anulada
                        && ((o.Fecha >= desdeOrdenes && o.Fecha <= hastaOrdenes)
                            // Las OC citadas en una FCJ entran aunque estén fuera de la ventana, para
                            // poder validar de qué proveedor son.
                            || citadas.Contains(o.Numero)))
            .Select(o => new { o.IdAsientoContable, o.Numero, o.Fecha, o.NitProveedor, o.NombreProveedor, o.Subtotal, o.Iva, o.Emisor })
            .ToListAsync(ct);

        var electronicas = facturas.Select(f => new FacturaElectronica(f.Cufe, f.Numero, f.TipoDocumento, f.FechaEmision,
            f.NitEmisor, f.NombreEmisor, f.NitReceptor, f.Subtotal, f.Iva, f.Total, f.ReferenciaOrden, []));
        var conciliables = ordenes.Select(o => new OrdenCompraConciliable(o.IdAsientoContable, o.Numero, o.Fecha, o.NitProveedor,
            o.NombreProveedor, o.Subtotal, o.Iva));

        return new Calculo(
            MotorConciliacion.Conciliar(electronicas, conciliables, Reglas, facturasCompra),
            facturas.ToDictionary(f => f.Cufe, f => (f.Id, f.Origen)),
            ordenes.Where(o => !string.IsNullOrWhiteSpace(o.Emisor)).ToDictionary(o => o.IdAsientoContable, o => o.Emisor!));
    }

    private static FacturaConciliadaDto MapFactura(ResultadoFactura r, (int Id, string Origen) factura,
        Dictionary<int, string> emisores, List<ObservacionFacturaProveedor> historial)
    {
        var ordenes = r.TodasLasOrdenes.ToList();
        return new(factura.Id, r.Factura.Numero, r.Factura.FechaEmision, r.Factura.NitEmisor, r.Factura.NombreEmisor,
            r.Factura.Subtotal, r.Factura.Iva, r.Factura.Total, r.Factura.ReferenciaOrden, r.Estado,
            r.OrdenCompra?.Numero, r.OrdenCompra?.Fecha,
            ordenes.Count == 0 ? null : ordenes.Sum(o => o.Subtotal),
            ordenes.Count == 0 ? null : ordenes.Sum(o => o.Iva),
            r.DiferenciaSubtotal, r.DiferenciaIva, r.Detalle,
            ordenes.Select(o => o.Numero).ToList(),
            r.FacturaCompra?.Numero,
            factura.Origen,
            ordenes.Select(o => emisores.GetValueOrDefault(o.IdAsientoContable)).OfType<string>().Distinct().ToList(),
            historial.Count,
            historial.LastOrDefault()?.Texto,
            Revisada(historial));
    }

    private static bool MismosRenglones(ICollection<OrdenCompraWorldOfficeRenglon> actuales, List<OrdenCompraWorldOfficeRenglon> nuevos)
    {
        if (actuales.Count != nuevos.Count) return false;
        return actuales.OrderBy(r => r.Orden).Zip(nuevos).All(p =>
            p.First.Orden == p.Second.Orden &&
            p.First.Codigo == p.Second.Codigo &&
            p.First.Descripcion == p.Second.Descripcion &&
            p.First.Cantidad == p.Second.Cantidad &&
            p.First.ValorUnitario == p.Second.ValorUnitario &&
            p.First.PorcentajeIva == p.Second.PorcentajeIva);
    }

    /// <summary>Rol con <see cref="Rol.PuedeVerConciliacion"/> (activo). Devuelve el usuario para firmar las observaciones.</summary>
    private async Task<Usuario> ExigirPermisoAsync(int usuarioId, CancellationToken ct)
    {
        var usuario = await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == usuarioId && u.Activo)
            .FirstOrDefaultAsync(ct);
        var tienePermiso = usuario is not null && await db.UsuarioRoles
            .Where(ur => ur.UsuarioId == usuarioId)
            .AnyAsync(ur => ur.Rol.Activo && ur.Rol.PuedeVerConciliacion, ct);

        if (!tienePermiso)
            throw new UnauthorizedAccessException("No tenés permiso para ver la conciliación de facturas.");
        return usuario!;
    }

    /// <summary>Fecha de hoy en Colombia (UTC-5, sin horario de verano): el contenedor corre en UTC.</summary>
    public static DateOnly HoyColombia(TimeProvider reloj) => DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime.AddHours(-5));

    private static string Truncar(string valor, int maximo) => valor.Length <= maximo ? valor : valor[..maximo];
}
