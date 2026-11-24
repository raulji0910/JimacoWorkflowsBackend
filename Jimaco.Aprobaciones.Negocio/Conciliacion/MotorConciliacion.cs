namespace Jimaco.Aprobaciones.Negocio.Conciliacion;

// Orden de compra tal como sale de World Office. Subtotal = SUM(Cantidad * MontoMonetarioUnitario),
// NO SUM(TotalRenglon): en Ferrealiados TotalRenglon trae un valor con margen (1.16-1.25x), no el
// costo que se le paga al proveedor. Las OC no llevan retenciones, solo IVA.
public sealed record OrdenCompraConciliable(
    int IdAsientoContable,
    int Numero,
    DateOnly Fecha,
    string NitProveedor,
    string NombreProveedor,
    decimal Subtotal,
    decimal Iva);

public enum EstadoConciliacion
{
    Cuadra,
    Diferencia,
    Parcial,
    SinOrdenCompra,
}

public sealed record ResultadoFactura(
    FacturaElectronica Factura,
    EstadoConciliacion Estado,
    OrdenCompraConciliable? OrdenCompra,
    decimal? DiferenciaSubtotal,
    decimal? DiferenciaIva,
    string Detalle,
    // Solo cuando el cruce salió de la FC de World Office (último recurso). Si la FC cita varias OC,
    // OrdenCompra es la primera y el resto va en OrdenesAdicionales.
    FacturaCompraConciliable? FacturaCompra = null,
    IReadOnlyList<OrdenCompraConciliable>? OrdenesAdicionales = null)
{
    public IEnumerable<OrdenCompraConciliable> TodasLasOrdenes
        => OrdenCompra is null ? [] : new[] { OrdenCompra }.Concat(OrdenesAdicionales ?? []);
}

public sealed record OrdenPendiente(OrdenCompraConciliable OrdenCompra, decimal SaldoSubtotal);

public sealed record ResultadoConciliacion(
    IReadOnlyList<ResultadoFactura> Facturas,
    IReadOnlyList<OrdenPendiente> OrdenesPendientes,
    IReadOnlyList<OrdenFacturadaSinXml> OrdenesFacturadasSinXml,
    IReadOnlyList<ReferenciaOrdenInvalida> ReferenciasInvalidas);

// PorcentajeMinimoParcial: para tratar una factura como entrega parcial, el saldo que deja en la OC
// debe ser al menos ese % de la OC. Una factura 1-2% por debajo de la OC casi siempre es un precio
// distinto (caso real Imporinox, -435 sobre 73.109), no media entrega.
public sealed record ReglasConciliacion(
    decimal ToleranciaPesos = 100m,
    int DiasAntes = 90,
    int DiasDespues = 15,
    decimal PorcentajeMinimoParcial = 0.05m);

// Cruce XML del proveedor <-> OC de World Office. Orden de prioridad:
//   1. Número de OC que el proveedor puso en el XML (OrderReference), si es del mismo NIT.
//   2. Mismo NIT + mismo subtotal e IVA (± tolerancia) dentro de la ventana de fechas.
//   3. Entregas parciales: la factura cabe en el saldo de una OC del mismo NIT.
//   4. Hay OC del proveedor pero ningún valor encaja -> Diferencia contra la más cercana.
//   5. Último recurso, antes de dar Diferencia o SinOrdenCompra: la FC que Contabilidad registró en
//      WO para esa factura (mismo NIT y número) y la(s) OC que escribió en su Personalizado1.
//   6. Ninguna OC del proveedor en la ventana -> SinOrdenCompra.
// Después, una OC que siga con saldo pero que una FC de WO dice cubrir no es "sin factura": es una
// factura registrada cuyo XML no llegó (OrdenesFacturadasSinXml).
// La tolerancia existe porque WO guarda precios digitados con IVA y divididos por 1,19 (decimales
// largos): el XML y la OC difieren 1-2 pesos de redondeo aunque sea la misma compra.
public static class MotorConciliacion
{
    public static ResultadoConciliacion Conciliar(
        IEnumerable<FacturaElectronica> facturas,
        IEnumerable<OrdenCompraConciliable> ordenes,
        ReglasConciliacion? reglas = null,
        IEnumerable<FacturaCompraConciliable>? facturasCompra = null)
    {
        reglas ??= new ReglasConciliacion();
        var todasLasOrdenes = ordenes.ToList();
        ordenes = todasLasOrdenes;

        var porNit = ordenes
            .GroupBy(o => LectorFacturaElectronica.NormalizarNit(o.NitProveedor))
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Fecha).ThenBy(o => o.Numero).ToList());
        var saldos = porNit.Values.SelectMany(l => l).ToDictionary(o => o.IdAsientoContable, o => o.Subtotal);

        var pendientes = facturas
            .Where(f => f.EsFactura)
            .OrderBy(f => f.FechaEmision).ThenBy(f => f.Numero, StringComparer.Ordinal)
            .ToList();
        var resultados = new Dictionary<FacturaElectronica, ResultadoFactura>();

        // OC citadas en las FC de WO, ya validadas: solo cuentan las del mismo NIT de la FC. Las de
        // otro proveedor o inexistentes se reportan para que Contabilidad las corrija.
        var porNumero = todasLasOrdenes.ToLookup(o => o.Numero);
        var invalidas = new List<ReferenciaOrdenInvalida>();
        var citadasPorFc = new Dictionary<FacturaCompraConciliable, List<OrdenCompraConciliable>>();
        foreach (var fc in facturasCompra ?? [])
        {
            var nitFc = LectorFacturaElectronica.NormalizarNit(fc.NitProveedor);
            var validas = new List<OrdenCompraConciliable>();
            foreach (var numero in fc.OrdenesCitadas)
            {
                var delNit = porNumero[numero].FirstOrDefault(o => LectorFacturaElectronica.NormalizarNit(o.NitProveedor) == nitFc);
                if (delNit is not null)
                    validas.Add(delNit);
                else if (porNumero[numero].FirstOrDefault() is { } otra)
                    invalidas.Add(new ReferenciaOrdenInvalida(fc, numero, $"La OC {numero} es de otro proveedor ({otra.NombreProveedor}, NIT {otra.NitProveedor})."));
                else
                    invalidas.Add(new ReferenciaOrdenInvalida(fc, numero, $"La OC {numero} no está entre las OC sincronizadas de World Office."));
            }
            if (validas.Count > 0) citadasPorFc[fc] = validas;
        }

        FacturaCompraConciliable? FcDe(FacturaElectronica f)
        {
            var nit = LectorFacturaElectronica.NormalizarNit(f.NitEmisor);
            return citadasPorFc.Keys.FirstOrDefault(fc => LectorFacturaElectronica.NormalizarNit(fc.NitProveedor) == nit && fc.CorrespondeA(f));
        }

        List<OrdenCompraConciliable> Candidatas(FacturaElectronica f)
            => porNit.TryGetValue(LectorFacturaElectronica.NormalizarNit(f.NitEmisor), out var lista)
                ? lista.Where(o => o.Fecha >= f.FechaEmision.AddDays(-reglas.DiasAntes)
                                && o.Fecha <= f.FechaEmision.AddDays(reglas.DiasDespues)).ToList()
                : [];

        bool Igual(decimal a, decimal b) => Math.Abs(a - b) <= reglas.ToleranciaPesos;
        bool Intacta(OrdenCompraConciliable o) => saldos[o.IdAsientoContable] == o.Subtotal;
        bool DejaSaldoParcial(FacturaElectronica f, OrdenCompraConciliable o)
        {
            var restante = saldos[o.IdAsientoContable] - f.Subtotal;
            return restante > reglas.ToleranciaPesos && restante >= o.Subtotal * reglas.PorcentajeMinimoParcial;
        }

        // Contabilidad ya confirmó que esa factura cubre esas OC: se consumen todas (aunque el valor
        // no cuadre) para que no queden además como "OC sin factura".
        bool AsignarPorFc(FacturaElectronica f)
        {
            var fc = FcDe(f);
            if (fc is null) return false;

            var citadas = citadasPorFc[fc];
            var saldo = citadas.Sum(o => saldos[o.IdAsientoContable]);
            var intactas = citadas.All(Intacta);
            var ivaOc = citadas.Sum(o => o.Iva);
            var lista = string.Join(", ", citadas.Select(o => o.Numero));
            var plural = citadas.Count > 1 ? "las OC" : "la OC";
            var cuadra = Igual(f.Subtotal, saldo) && (!intactas || Igual(f.Iva, ivaOc));
            var detalle = cuadra
                ? $"Contabilidad registró la FC {fc.Numero} en World Office con {plural} {lista}."
                : $"Contabilidad registró la FC {fc.Numero} en World Office con {plural} {lista}, pero los valores no coinciden.";

            foreach (var o in citadas) saldos[o.IdAsientoContable] = 0m;
            resultados[f] = new ResultadoFactura(f, cuadra ? EstadoConciliacion.Cuadra : EstadoConciliacion.Diferencia, citadas[0],
                DiferenciaSubtotal: f.Subtotal - saldo,
                DiferenciaIva: intactas ? f.Iva - ivaOc : null,
                detalle,
                FacturaCompra: fc,
                OrdenesAdicionales: citadas.Skip(1).ToList());
            return true;
        }

        void Asignar(FacturaElectronica f, OrdenCompraConciliable o, EstadoConciliacion estado, string detalle)
        {
            var saldoAntes = saldos[o.IdAsientoContable];
            saldos[o.IdAsientoContable] = Math.Max(0m, saldoAntes - f.Subtotal);
            // El IVA solo se compara cuando la factura cubre la OC completa; en parciales no hay
            // contra qué (la OC no reparte el IVA por entrega).
            var ocCompleta = saldoAntes == o.Subtotal && estado != EstadoConciliacion.Parcial;
            resultados[f] = new ResultadoFactura(f, estado, o,
                DiferenciaSubtotal: f.Subtotal - saldoAntes,
                DiferenciaIva: ocCompleta ? f.Iva - o.Iva : null,
                detalle);
        }

        // 1. OC referenciada en el XML.
        foreach (var f in pendientes.Where(f => f.NumeroOrdenReferenciada is not null).ToList())
        {
            var oc = Candidatas(f).FirstOrDefault(o => o.Numero == f.NumeroOrdenReferenciada)
                     ?? porNit.GetValueOrDefault(LectorFacturaElectronica.NormalizarNit(f.NitEmisor))?
                         .FirstOrDefault(o => o.Numero == f.NumeroOrdenReferenciada);
            if (oc is null) continue;

            var saldo = saldos[oc.IdAsientoContable];
            if (Igual(f.Subtotal, saldo) && (!Intacta(oc) || Igual(f.Iva, oc.Iva)))
                Asignar(f, oc, EstadoConciliacion.Cuadra, $"OC {oc.Numero} indicada por el proveedor en la factura.");
            else if (DejaSaldoParcial(f, oc))
                Asignar(f, oc, EstadoConciliacion.Parcial, $"OC {oc.Numero} indicada por el proveedor; factura parcial.");
            else
                Asignar(f, oc, EstadoConciliacion.Diferencia, $"OC {oc.Numero} indicada por el proveedor, pero los valores no coinciden.");
            pendientes.Remove(f);
        }

        // 2. Coincidencia exacta NIT + subtotal + IVA contra una OC sin facturar.
        foreach (var f in pendientes.ToList())
        {
            var oc = Candidatas(f)
                .Where(o => Intacta(o) && Igual(f.Subtotal, o.Subtotal) && Igual(f.Iva, o.Iva))
                .OrderBy(o => Math.Abs(o.Fecha.DayNumber - f.FechaEmision.DayNumber))
                .FirstOrDefault();
            if (oc is null) continue;

            Asignar(f, oc, EstadoConciliacion.Cuadra, $"Mismo proveedor y valores que la OC {oc.Numero}.");
            pendientes.Remove(f);
        }

        // 3 y 4. Parciales (la factura completa o cabe en el saldo de una OC) y diferencias.
        foreach (var f in pendientes)
        {
            var candidatas = Candidatas(f);
            if (candidatas.Count == 0)
            {
                if (AsignarPorFc(f)) continue;
                resultados[f] = new ResultadoFactura(f, EstadoConciliacion.SinOrdenCompra, null, null, null,
                    "No hay OC de este proveedor en la ventana de fechas.");
                continue;
            }

            var completaSaldo = candidatas
                .Where(o => !Intacta(o) && saldos[o.IdAsientoContable] > 0 && Igual(f.Subtotal, saldos[o.IdAsientoContable]))
                .FirstOrDefault();
            if (completaSaldo is not null)
            {
                Asignar(f, completaSaldo, EstadoConciliacion.Cuadra, $"Completa el saldo de la OC {completaSaldo.Numero} (entrega parcial).");
                continue;
            }

            var cabe = candidatas
                .Where(o => DejaSaldoParcial(f, o))
                .OrderBy(o => saldos[o.IdAsientoContable] - f.Subtotal)
                .FirstOrDefault();
            if (cabe is not null)
            {
                Asignar(f, cabe, EstadoConciliacion.Parcial, $"Posible entrega parcial de la OC {cabe.Numero}: confirmar productos.");
                continue;
            }

            if (AsignarPorFc(f)) continue;

            var cercana = candidatas.OrderBy(o => Math.Abs(f.Subtotal - saldos[o.IdAsientoContable])).First();
            var diferencia = Math.Abs(f.Subtotal - saldos[cercana.IdAsientoContable]);
            if (Intacta(cercana) && diferencia < cercana.Subtotal * reglas.PorcentajeMinimoParcial)
            {
                // Tan cerca que es esa OC con otro precio (caso Imporinox): se liga y se consume,
                // para que no quede además como "OC sin factura".
                Asignar(f, cercana, EstadoConciliacion.Diferencia, $"Corresponde a la OC {cercana.Numero}, pero el proveedor facturó un valor distinto.");
                saldos[cercana.IdAsientoContable] = 0m;
                continue;
            }

            resultados[f] = new ResultadoFactura(f, EstadoConciliacion.Diferencia, cercana,
                f.Subtotal - saldos[cercana.IdAsientoContable], f.Iva - cercana.Iva,
                $"El valor no coincide con ninguna OC del proveedor; la más cercana es la {cercana.Numero}.");
        }

        // FC de WO que no se usaron con ningún XML: sus OC no están "sin factura", falta el XML.
        var fcUsadas = resultados.Values.Where(r => r.FacturaCompra is not null).Select(r => r.FacturaCompra!).ToHashSet();
        var facturadaPor = new Dictionary<int, FacturaCompraConciliable>();
        foreach (var (fc, citadas) in citadasPorFc.Where(p => !fcUsadas.Contains(p.Key)).OrderBy(p => p.Key.Fecha))
            foreach (var o in citadas)
                facturadaPor.TryAdd(o.IdAsientoContable, fc);

        var conSaldo = porNit.Values.SelectMany(l => l)
            .Where(o => saldos[o.IdAsientoContable] > reglas.ToleranciaPesos)
            .OrderBy(o => o.Fecha)
            .ToList();

        return new ResultadoConciliacion(
            resultados.Values.OrderBy(r => r.Factura.FechaEmision).ThenBy(r => r.Factura.Numero, StringComparer.Ordinal).ToList(),
            conSaldo.Where(o => !facturadaPor.ContainsKey(o.IdAsientoContable))
                .Select(o => new OrdenPendiente(o, saldos[o.IdAsientoContable])).ToList(),
            conSaldo.Where(o => facturadaPor.ContainsKey(o.IdAsientoContable))
                .Select(o => new OrdenFacturadaSinXml(o, facturadaPor[o.IdAsientoContable])).ToList(),
            invalidas);
    }
}
