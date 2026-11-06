using System.Data;
using Dapper;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Microsoft.Data.SqlClient;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>Configuración fija del CE que crea el sistema (appsettings, sección <c>CuentasPorPagar</c>).</summary>
public record ConfiguracionCE(
    // CEJ-2 mientras se prueba (prefijo de prueba de CE que ya existe en WO), CEJ en producción.
    string Prefijo,
    // "Elaborado por" — IdTercero de WO (9700 = Camilo Piragauta, el contable que hace hoy los CE).
    int IdTerceroElabora,
    // IdUsuario / IdUsuarioWO del encabezado ("CAMILO").
    string UsuarioWO,
    // NomPc del encabezado: Camilo usa "AUX"; "APROBACIONES" permite distinguir los CE del sistema.
    string NomPc);

public enum ResultadoCreacionCE { Creado, YaExistia, Conflicto }

public record CreacionCEResultado(ResultadoCreacionCE Resultado, string? Prefijo = null, decimal? Numero = null, int? IdAsientoContable = null, string? Mensaje = null)
{
    public static CreacionCEResultado ConConflicto(string mensaje) => new(ResultadoCreacionCE.Conflicto, Mensaje: mensaje);
}

/// <summary>
/// Crea en World Office el Comprobante de Egreso de un pago a proveedor confirmado (2026-11-06) —
/// igual al que graba Contabilidad a mano: un encabezado en <c>[CuentasContables - Asientos]</c> y
/// sus renglones en <c>[CuentasContables - Asientos - Detalles]</c>, nada más (confirmado contra WO
/// real: un CE de Camilo con 6 facturas no dejó rastro en ninguna otra tabla). Plantilla tomada del
/// CEJ 15400 (pago de facturas) y el CEJ 15328 (cruce de anticipo):
/// <list type="bullet">
/// <item>Por factura: DÉBITO a la misma cuenta del renglón de la FCJ (220501/2335xx/2210xx), <c>AbonaADocumento</c> = ese renglón, <c>ValorAbono</c> = +valor.</item>
/// <item>Por anticipo: CRÉDITO a la cuenta del anticipo (133005), <c>AbonaADocumento</c> = el renglón del anticipo, <c>ValorAbono</c> = −valor.</item>
/// <item>Banco: CRÉDITO por el neto, sin cruce — solo si el neto es mayor que 0 (un cruce puro de anticipos no mueve plata).</item>
/// </list>
/// El número es el mayor del prefijo + 1 (WO no tiene tabla de consecutivos; su "reserva" es grabar
/// el encabezado apenas se abre la pantalla). Si Contabilidad graba otro CE con el mismo número en
/// el mismo instante, el trigger <c>TrgI_ValidaLlave</c> de WO lo rechaza y deshace todo — se
/// reintenta con el siguiente. Los triggers de WO leen UNA fila de INSERTED, por eso los renglones
/// se insertan de a uno.
///
/// Antes de grabar se relee en WO el saldo de cada factura y de cada anticipo: si algo cambió desde
/// que se decidió el pago (alguien lo pagó o cruzó a mano), NO se graba nada y se devuelve el
/// conflicto. Todo va en una sola transacción: o queda el CE completo, o no queda nada.
/// </summary>
public class ComprobanteEgresoWriter(string connectionString, ConfiguracionCE config)
{
    private const int MaximoIntentos = 3;

    /// <summary>Marca que va en la nota del encabezado — la usa la verificación de "ya existía" si una corrida anterior creó el CE pero no alcanzó a avisarle a la Api.</summary>
    public static string Marca(int instanciaDocumentoId) => $"(APROBACIONES #{instanciaDocumentoId})";

    public async Task<CreacionCEResultado> CrearAsync(PendienteCreacionCEDto pago, CancellationToken ct = default)
    {
        for (var intento = 1; ; intento++)
        {
            try
            {
                return await IntentarCrearAsync(pago, ct);
            }
            catch (SqlException ex) when (ex.Message.Contains("La llave ya existe", StringComparison.OrdinalIgnoreCase) && intento < MaximoIntentos)
            {
                // Otro CE tomó el mismo número en el mismo instante — la transacción ya se deshizo
                // entera (ROLLBACK del trigger), se vuelve a intentar con el número siguiente.
                Console.WriteLine($"[{DateTime.Now:s}]     El número de CE se usó al mismo tiempo en WO, reintentando ({intento}/{MaximoIntentos})...");
            }
        }
    }

    private async Task<CreacionCEResultado> IntentarCrearAsync(PendienteCreacionCEDto pago, CancellationToken ct)
    {
        await using var conexion = new SqlConnection(connectionString);
        await conexion.OpenAsync(ct);

        // 1. ¿Ya se creó en una corrida anterior (y falló el aviso a la Api)? Se busca por la marca.
        var existente = await conexion.QueryFirstOrDefaultAsync<CeExistenteRow>(new CommandDefinition("""
            SELECT TOP 1 IdAsientoContable, prefijo AS Prefijo, DocumentoNúmero AS DocumentoNumero
            FROM [CuentasContables - Asientos]
            WHERE IdCuentaContableDocumento = 'ce' AND (senAnulado = 0 OR senAnulado IS NULL)
              AND NomPc = @NomPc AND Nota LIKE @Marca
            ORDER BY IdAsientoContable DESC;
            """, new { config.NomPc, Marca = "%" + Marca(pago.InstanciaDocumentoId) + "%" }, cancellationToken: ct));
        if (existente is not null)
            return new CreacionCEResultado(ResultadoCreacionCE.YaExistia, existente.Prefijo, existente.DocumentoNumero, existente.IdAsientoContable);

        if (pago.Facturas.Count == 0)
            return CreacionCEResultado.ConConflicto("El pago no tiene facturas.");
        if (pago.Neto < 0)
            return CreacionCEResultado.ConConflicto("Los anticipos superan el valor de las facturas.");
        if (pago.Neto > 0 && pago.IdCuentaBancoWO is null)
            return CreacionCEResultado.ConConflicto("No se eligió el banco del pago.");

        await using var tx = (SqlTransaction)await conexion.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // 2. Releer en WO cada factura y cada anticipo (dentro de la transacción, con bloqueo de
        // actualización sobre el renglón de origen): saldo actual, cuenta y tercero.
        var facturas = new List<(FacturaCreacionCEDto Pago, OrigenRow Origen)>();
        foreach (var f in pago.Facturas)
        {
            var origen = await LeerOrigenAsync(conexion, tx, f.IdDocumentoDetalleWO, ct);
            var etiqueta = $"{f.Prefijo} {f.NumeroDocumento}".Trim();
            if (origen is null)
                return await Abortar(tx, $"La factura {etiqueta} ya no existe en World Office.", ct);

            // Pendiente de una factura = Crédito − Débito de todos los renglones que la cruzan.
            var pendiente = origen.Credito - origen.Debito;
            if (pendiente != f.Valor)
                return await Abortar(tx, pendiente <= 0
                    ? $"La factura {etiqueta} ya aparece pagada en World Office (¿alguien hizo el comprobante a mano?)."
                    : $"La factura {etiqueta} cambió de saldo en World Office: se aprobó pagar {f.Valor:N0} y hoy debe {pendiente:N0}.", ct);

            facturas.Add((f, origen));
        }

        var idProveedor = facturas[0].Origen.IdTercero;
        if (facturas.Any(f => f.Origen.IdTercero != idProveedor))
            return await Abortar(tx, "Las facturas del pago no son todas del mismo tercero en World Office.", ct);

        var anticipos = new List<(AnticipoCreacionCEDto Pago, OrigenRow Origen)>();
        foreach (var a in pago.Anticipos)
        {
            var origen = await LeerOrigenAsync(conexion, tx, a.IdDocumentoDetalleWO, ct);
            var etiqueta = $"{a.Prefijo} {a.NumeroDocumento}".Trim();
            if (origen is null)
                return await Abortar(tx, $"El anticipo {etiqueta} ya no existe en World Office.", ct);

            // Saldo de un anticipo = Débito − Crédito (naturaleza contraria a la factura).
            var saldo = origen.Debito - origen.Credito;
            if (saldo < a.Valor)
                return await Abortar(tx, $"El anticipo {etiqueta} ya no tiene saldo suficiente en World Office: se iba a descontar {a.Valor:N0} y le quedan {saldo:N0}.", ct);
            if (origen.IdTercero != idProveedor)
                return await Abortar(tx, $"El anticipo {etiqueta} es de otro tercero en World Office.", ct);

            anticipos.Add((a, origen));
        }

        var totalFacturas = facturas.Sum(f => f.Pago.Valor);
        var totalAnticipos = anticipos.Sum(a => a.Pago.Valor);
        var neto = totalFacturas - totalAnticipos;
        if (neto != pago.Neto)
            return await Abortar(tx, $"El neto no cuadra ({neto:N0} calculado vs {pago.Neto:N0} aprobado).", ct);

        // 3. Dirección principal del proveedor (WO la guarda en el encabezado).
        var idDireccion = await conexion.ExecuteScalarAsync<int?>(new CommandDefinition("""
            SELECT TOP 1 IdTerceroDireccion FROM [Terceros - Direcciones]
            WHERE IdTercero = @IdTercero ORDER BY CASE WHEN ISNULL(senDirecPpal, 0) <> 0 THEN 0 ELSE 1 END, IdTerceroDireccion;
            """, new { IdTercero = idProveedor }, tx, cancellationToken: ct));

        // 4. Número: el mayor del prefijo + 1, bloqueando el rango hasta el commit.
        var numero = await conexion.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT ISNULL(MAX(DocumentoNúmero), 0) + 1
            FROM [CuentasContables - Asientos] WITH (UPDLOCK, HOLDLOCK)
            WHERE IdEmpresa = 1 AND IdCuentaContableDocumento = 'ce' AND prefijo = @Prefijo;
            """, new { config.Prefijo }, tx, cancellationToken: ct));

        var largoNotaEncabezado = await LargoColumnaAsync(conexion, tx, "[dbo].[CuentasContables - Asientos]", "Nota", ct);
        var largoNotaRenglon = await LargoColumnaAsync(conexion, tx, "[dbo].[CuentasContables - Asientos - Detalles]", "Nota", ct);

        var concepto = pago.Facturas.Count == 1
            ? $"PAGO FC {Etiqueta(pago.Facturas[0])}"
            : "PAGO FC VARIAS";
        if (pago.Anticipos.Count > 0) concepto += " Y CRUCE ANTICIPO";
        var notaEncabezado = Recortar($"{concepto} - ORDEN DE PAGO {pago.NumeroReferencia} {Marca(pago.InstanciaDocumentoId)}", largoNotaEncabezado);
        if (!notaEncabezado.Contains(Marca(pago.InstanciaDocumentoId)))
            notaEncabezado = Recortar($"{concepto} {Marca(pago.InstanciaDocumentoId)}", largoNotaEncabezado);

        var fecha = pago.FechaPago.Date;

        // 5. Encabezado. SCOPE_IDENTITY y no OUTPUT: la tabla tiene triggers (OUTPUT sin INTO no se permite).
        var idAsiento = await conexion.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO [CuentasContables - Asientos]
                (IdEmpresa, IdCuentaContableDocumento, prefijo, DocumentoNúmero, Fecha,
                 IdTerceroExterno, IdDirecciónExterno, IdTerceroInterno, Nota, NomPc,
                 Verificado, senAnulado, senBloqueado, ChkImpreso, IdUsuario, IdUsuarioWO, senElectronico)
            VALUES
                (1, 'ce', @Prefijo, @Numero, @Fecha,
                 @IdProveedor, @IdDireccion, @IdElabora, @Nota, @NomPc,
                 0, 0, 0, 0, @Usuario, @Usuario, 0);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new
        {
            config.Prefijo, Numero = numero, Fecha = fecha, IdProveedor = idProveedor, IdDireccion = idDireccion,
            IdElabora = config.IdTerceroElabora, Nota = notaEncabezado, config.NomPc, Usuario = config.UsuarioWO
        }, tx, cancellationToken: ct));

        // 6. Renglones, de a uno.
        foreach (var (f, origen) in facturas)
            await InsertarRenglonAsync(conexion, tx, idAsiento, origen.IdCuentaContable, debito: f.Valor, credito: 0, idProveedor,
                abonaA: f.IdDocumentoDetalleWO, valorAbono: f.Valor, fecha,
                Recortar($"PAGO FC {Etiqueta(f)}", largoNotaRenglon), ct);

        foreach (var (a, origen) in anticipos)
            await InsertarRenglonAsync(conexion, tx, idAsiento, origen.IdCuentaContable, debito: 0, credito: a.Valor, idProveedor,
                abonaA: a.IdDocumentoDetalleWO, valorAbono: -a.Valor, fecha,
                Recortar($"CRUCE ANTICIPO {a.Prefijo} {a.NumeroDocumento}".Trim(), largoNotaRenglon), ct);

        if (neto > 0)
            await InsertarRenglonAsync(conexion, tx, idAsiento, pago.IdCuentaBancoWO!.Value, debito: 0, credito: neto, idProveedor,
                abonaA: null, valorAbono: null, fecha, Recortar(notaEncabezado, largoNotaRenglon), ct);

        // 7. Control final de partida doble antes de confirmar.
        var cuadre = await conexion.QueryFirstAsync<(decimal Debito, decimal Credito)>(new CommandDefinition("""
            SELECT ISNULL(SUM(Débito), 0), ISNULL(SUM(Crédito), 0)
            FROM [CuentasContables - Asientos - Detalles] WHERE IdAsientoContable = @Id;
            """, new { Id = idAsiento }, tx, cancellationToken: ct));
        if (cuadre.Debito != cuadre.Credito || cuadre.Debito != totalFacturas)
            return await Abortar(tx, $"El comprobante no cuadró (débitos {cuadre.Debito:N0}, créditos {cuadre.Credito:N0}). No se grabó nada.", ct);

        await tx.CommitAsync(ct);
        return new CreacionCEResultado(ResultadoCreacionCE.Creado, config.Prefijo, numero, idAsiento);
    }

    private static async Task<CreacionCEResultado> Abortar(SqlTransaction tx, string mensaje, CancellationToken ct)
    {
        await tx.RollbackAsync(ct);
        return CreacionCEResultado.ConConflicto(mensaje);
    }

    /// <summary>Renglón de origen (cuenta, tercero) y la suma de todo lo que lo cruza, sin contar documentos anulados.</summary>
    private static Task<OrigenRow?> LeerOrigenAsync(SqlConnection conexion, SqlTransaction tx, int idDocumentoDetalle, CancellationToken ct) =>
        conexion.QueryFirstOrDefaultAsync<OrigenRow?>(new CommandDefinition("""
            SELECT o.IdCuentaContable, o.IdTercero,
                   ISNULL(SUM(x.Débito), 0)  AS Debito,
                   ISNULL(SUM(x.Crédito), 0) AS Credito
            FROM [CuentasContables - Asientos - Detalles] o WITH (UPDLOCK)
            JOIN [CuentasContables - Asientos] ao ON ao.IdAsientoContable = o.IdAsientoContable
            LEFT JOIN [CuentasContables - Asientos - Detalles] x ON x.AbonaADocumento = o.IdDocumentoDetalle
                AND EXISTS (SELECT 1 FROM [CuentasContables - Asientos] ax
                            WHERE ax.IdAsientoContable = x.IdAsientoContable AND (ax.senAnulado = 0 OR ax.senAnulado IS NULL))
            WHERE o.IdDocumentoDetalle = @Id AND (ao.senAnulado = 0 OR ao.senAnulado IS NULL)
            GROUP BY o.IdCuentaContable, o.IdTercero;
            """, new { Id = idDocumentoDetalle }, tx, cancellationToken: ct));

    private static Task InsertarRenglonAsync(SqlConnection conexion, SqlTransaction tx, int idAsiento, int idCuenta,
        decimal debito, decimal credito, int idTercero, int? abonaA, decimal? valorAbono, DateTime fecha, string nota, CancellationToken ct) =>
        conexion.ExecuteAsync(new CommandDefinition("""
            INSERT INTO [CuentasContables - Asientos - Detalles]
                (IdAsientoContable, IdCuentaContable, Débito, Crédito, IdTercero, AbonaADocumento,
                 Vencimiento, Nota, ValorAbono, ValorExtranjero, TRMFac, Verificado, SenEntregado, SenEnviadoConcil)
            VALUES
                (@IdAsiento, @IdCuenta, @Debito, @Credito, @IdTercero, @AbonaA,
                 @Fecha, @Nota, @ValorAbono, 0, 0, 0, 0, 0);
            """, new
        {
            IdAsiento = idAsiento, IdCuenta = idCuenta, Debito = debito, Credito = credito, IdTercero = idTercero,
            AbonaA = abonaA, Fecha = fecha, Nota = nota, ValorAbono = valorAbono
        }, tx, cancellationToken: ct));

    /// <summary>Largo máximo (en caracteres) de una columna de texto de WO — para no romper el INSERT con notas largas. -1 = sin límite.</summary>
    private static async Task<int> LargoColumnaAsync(SqlConnection conexion, SqlTransaction tx, string tabla, string columna, CancellationToken ct)
    {
        var info = await conexion.QueryFirstOrDefaultAsync<(string Tipo, short Largo)>(new CommandDefinition("""
            SELECT ty.name, c.max_length
            FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID(@Tabla) AND c.name = @Columna;
            """, new { Tabla = tabla, Columna = columna }, tx, cancellationToken: ct));

        if (info.Tipo is null || info.Largo < 0 || info.Tipo is "ntext" or "text") return 4000;
        return info.Tipo.StartsWith('n') ? info.Largo / 2 : info.Largo;
    }

    private static string Etiqueta(FacturaCreacionCEDto f) =>
        string.Join(' ', new[] { f.NumeroDocumentoExterno, f.Prefijo, f.NumeroDocumento }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];

    private sealed class CeExistenteRow
    {
        public int IdAsientoContable { get; set; }
        public string? Prefijo { get; set; }
        public decimal DocumentoNumero { get; set; }
    }

    private sealed class OrigenRow
    {
        public int IdCuentaContable { get; set; }
        public int IdTercero { get; set; }
        public decimal Debito { get; set; }
        public decimal Credito { get; set; }
    }
}
