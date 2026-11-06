using Dapper;
using Jimaco.Aprobaciones.Sincronizador.Models;
using Microsoft.Data.SqlClient;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Todo lo que el Sincronizador necesita leer de la base de World Office. Estrictamente de solo
/// lectura — pensado para correr con el login <c>wf_readonly</c> (ver
/// ProyectosJimaco/WorkflowDocumentos/exploracion-worldoffice.sql). No hay ningún INSERT/UPDATE
/// acá ni en ningún otro lado de este proyecto: si algún día hace falta escribir de vuelta en
/// World Office, es una decisión aparte, no algo que este agente deba hacer calladamente.
/// </summary>
public class WorldOfficeReader(string connectionString)
{
    /// <summary>
    /// Documentos nuevos con cualquiera de los <paramref name="prefijos"/> configurados (uno por
    /// cada <c>TipoDocumento</c> con <c>PrefijoWorldOffice</c> seteado en Jimaco Aprobaciones — ver
    /// <see cref="JimacoAprobacionesClient.ListarTiposDocumentoAsync"/>). Nada hardcodeado a "OC":
    /// el llamador decide qué prefijos importan según la configuración real, no este método.
    /// </summary>
    public async Task<IReadOnlyList<OcHeaderRow>> ObtenerDocumentosNuevosAsync(int marcaDeAgua, IReadOnlyList<string> prefijos, CancellationToken ct = default)
    {
        if (prefijos.Count == 0) return [];

        const string sql = """
            SELECT
                IdAsientoContable,
                prefijo               AS Prefijo,
                DocumentoNúmero       AS DocumentoNumero,
                Fecha,
                IdTerceroExterno,
                IdTerceroInterno,
                NúmDocumentoExterno   AS NumDocumentoExterno,
                Nota,
                IdFormaDePago
            FROM [CuentasContables - Asientos]
            WHERE prefijo IN @Prefijos
              AND IdAsientoContable > @Marca
              AND (senAnulado = 0 OR senAnulado IS NULL)
            ORDER BY IdAsientoContable;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { Marca = marcaDeAgua, Prefijos = prefijos }, cancellationToken: ct);
        var filas = await conexion.QueryAsync<OcHeaderRow>(comando);
        return filas.ToList();
    }

    /// <summary>
    /// Un documento puntual por prefijo + número (no por rango de IdAsientoContable) — para cuando
    /// la detección de "hay un documento nuevo" viene de otro lado (el correo de "Solicitar
    /// Aprobación", ver <see cref="CorreoAprobacionWatcher"/>) en vez de escanear la base. Null si
    /// no existe esa fila o está anulada — el llamador decide qué hacer con eso (ver Program.cs).
    /// </summary>
    public async Task<OcHeaderRow?> ObtenerDocumentoPorReferenciaAsync(string prefijo, decimal documentoNumero, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                IdAsientoContable,
                prefijo               AS Prefijo,
                DocumentoNúmero       AS DocumentoNumero,
                Fecha,
                IdTerceroExterno,
                IdTerceroInterno,
                NúmDocumentoExterno   AS NumDocumentoExterno,
                Nota,
                IdFormaDePago,
                IdUsuarioWO           AS SolicitanteWO,
                Personalizado4
            FROM [CuentasContables - Asientos]
            WHERE prefijo = @Prefijo AND DocumentoNúmero = @DocumentoNumero
              AND (senAnulado = 0 OR senAnulado IS NULL);
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { Prefijo = prefijo, DocumentoNumero = documentoNumero }, cancellationToken: ct);
        return await conexion.QueryFirstOrDefaultAsync<OcHeaderRow>(comando);
    }

    public async Task<TerceroRow?> ObtenerTerceroAsync(int idTercero, CancellationToken ct = default)
    {
        const string sql = """
            SELECT IdTercero, Nombre, Apellidos, Identificacion
            FROM Terceros
            WHERE IdTercero = @IdTercero;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { IdTercero = idTercero }, cancellationToken: ct);
        return await conexion.QueryFirstOrDefaultAsync<TerceroRow>(comando);
    }

    /// <summary>Suma de <c>TotalRenglon</c> de todos los renglones de la OC — la cabecera no trae un campo de valor total propio.</summary>
    public async Task<decimal> ObtenerValorTotalAsync(int idAsientoContable, CancellationToken ct = default)
    {
        const string sql = """
            SELECT SUM(TotalRenglon)
            FROM CCA_M_Inventarios
            WHERE IdAsientoContable = @IdAsientoContable;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { IdAsientoContable = idAsientoContable }, cancellationToken: ct);
        return await conexion.ExecuteScalarAsync<decimal?>(comando) ?? 0m;
    }

    /// <summary>
    /// Los renglones/ítems reales de la OC (ver esquema-worldoffice-oc.md, sección
    /// "CCA_M_Inventarios") — código y descripción salen de <c>Inventarios</c>, el resto de la
    /// línea misma. El IVA se toma del producto (<c>Inventarios.Iva</c>) porque la línea no trae su
    /// propio porcentaje.
    /// </summary>
    public async Task<IReadOnlyList<RenglonRow>> ObtenerRenglonesAsync(int idAsientoContable, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                inv.CódigoInventario AS Codigo,
                inv.Descripción      AS Descripcion,
                c.Cantidad,
                c.UnidadDeMedida     AS UnidadMedida,
                c.MontoMonetarioUnitario AS ValorUnitario,
                inv.Iva              AS PorcentajeIva
            FROM CCA_M_Inventarios c
            LEFT JOIN Inventarios inv ON inv.IdInventario = c.IdInventario
            WHERE c.IdAsientoContable = @IdAsientoContable
            ORDER BY c.[Renglón];
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { IdAsientoContable = idAsientoContable }, cancellationToken: ct);
        var filas = await conexion.QueryAsync<RenglonRow>(comando);
        return filas.ToList();
    }

    /// <summary>
    /// Descripción de la forma de pago — la tabla catálogo es <c>[Formas De Pago]</c> (con
    /// espacios), pero no se confirmó todavía el nombre exacto de su columna de texto (ver
    /// esquema-worldoffice-oc.md). Para no tumbar la sincronización entera por un nombre de columna
    /// equivocado, se toma dinámicamente la primera columna de texto no vacía distinta del id, en
    /// vez de asumir un nombre fijo.
    /// </summary>
    public async Task<string?> ObtenerFormaDePagoAsync(int idFormaDePago, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM [Formas De Pago] WHERE IdFormaDePago = @IdFormaDePago;";

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { IdFormaDePago = idFormaDePago }, cancellationToken: ct);
        var fila = await conexion.QueryFirstOrDefaultAsync(comando) as IDictionary<string, object>;
        if (fila is null) return null;

        foreach (var (columna, valor) in fila)
        {
            if (columna.Equals("IdFormaDePago", StringComparison.OrdinalIgnoreCase)) continue;
            if (valor is string texto && !string.IsNullOrWhiteSpace(texto)) return texto.Trim();
        }
        return null;
    }

    /// <summary>Dirección principal de un tercero (<c>Terceros - Direcciones</c>, unida por <c>IdTercero</c>, no por <c>IdDirección</c>) — prefiere la marcada <c>senDirecPpal</c>, si no hay ninguna toma cualquiera.</summary>
    public async Task<string?> ObtenerDireccionPrincipalAsync(int idTercero, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP 1 Dirección
            FROM [Terceros - Direcciones]
            WHERE IdTercero = @IdTercero
            ORDER BY senDirecPpal DESC;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { IdTercero = idTercero }, cancellationToken: ct);
        return await conexion.ExecuteScalarAsync<string?>(comando);
    }

    /// <summary>
    /// Una dirección puntual por su Id exacto (<c>IdTerceroDireccion</c>, la PK de <c>Terceros -
    /// Direcciones</c> — es lo que guarda <c>IdDirecciónExterno</c> en la tabla madre, confirmado
    /// cruzando ambos valores), con la Ciudad ya resuelta contra <c>Ciudades</c>. A diferencia de
    /// <see cref="ObtenerDireccionPrincipalAsync"/>, no asume "la principal del tercero" — usa
    /// exactamente la dirección que el documento tenía seleccionada.
    /// </summary>
    public async Task<DireccionRow?> ObtenerDireccionPorIdAsync(int idDireccion, CancellationToken ct = default)
    {
        const string sql = """
            SELECT d.Dirección AS Direccion, d.Teléfonos AS Telefonos, c.Ciudad
            FROM [Terceros - Direcciones] d
            LEFT JOIN Ciudades c ON c.IdCiudad = d.IdCiudad
            WHERE d.IdTerceroDireccion = @IdDireccion;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { IdDireccion = idDireccion }, cancellationToken: ct);
        return await conexion.QueryFirstOrDefaultAsync<DireccionRow>(comando);
    }

    /// <summary>
    /// Un documento externo ligado a otro (ej. la Cotización de origen de una Orden de Compra, vía
    /// <c>NúmDocumentoExterno</c>) — misma tabla madre, pero con los campos extra que necesita la
    /// Hoja de Alistamiento (<c>IdDirecciónExterno</c>, <c>Personalizado2</c>) que la lectura
    /// normal de OC nunca pidió. Null si no existe o está anulado.
    /// </summary>
    public async Task<OcHeaderRow?> ObtenerDocumentoVinculadoAsync(string prefijo, decimal documentoNumero, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                IdAsientoContable,
                prefijo               AS Prefijo,
                DocumentoNúmero       AS DocumentoNumero,
                Fecha,
                IdTerceroExterno,
                IdTerceroInterno,
                NúmDocumentoExterno   AS NumDocumentoExterno,
                Nota,
                IdFormaDePago,
                IdDirecciónExterno    AS IdDireccionExterno,
                Personalizado2
            FROM [CuentasContables - Asientos]
            WHERE prefijo = @Prefijo AND DocumentoNúmero = @DocumentoNumero
              AND (senAnulado = 0 OR senAnulado IS NULL);
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { Prefijo = prefijo, DocumentoNumero = documentoNumero }, cancellationToken: ct);
        return await conexion.QueryFirstOrDefaultAsync<OcHeaderRow>(comando);
    }

    /// <summary>
    /// Las líneas débito/crédito de un Comprobante de Egreso (prefijo <c>CEJ</c>, misma tabla madre
    /// que OC/COT — ver <see cref="ObtenerDocumentoVinculadoAsync"/> para la cabecera). Esquema
    /// confirmado con datos reales, no adivinado (ver comentario en <see
    /// cref="Models.DetalleComprobanteEgresoRow"/>).
    /// </summary>
    public async Task<IReadOnlyList<DetalleComprobanteEgresoRow>> ObtenerDetalleComprobanteEgresoAsync(int idAsientoContable, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                IdCuentaContable,
                Débito  AS Debito,
                Crédito AS Credito,
                Nota,
                Renglón AS Renglon
            FROM [CuentasContables - Asientos - Detalles]
            WHERE IdAsientoContable = @IdAsientoContable
            ORDER BY Renglón;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { IdAsientoContable = idAsientoContable }, cancellationToken: ct);
        var filas = await conexion.QueryAsync<DetalleComprobanteEgresoRow>(comando);
        return filas.ToList();
    }

    /// <summary>
    /// Remisiones recientes (prefijo <c>REM</c>, no anuladas) con su campo libre "COTIZACION"
    /// (<c>Personalizado4</c>) — se trae de una sola vez, acotado a los últimos <paramref
    /// name="diasAtras"/> días, en vez de consultar WO una vez por cada Cotización pendiente (más
    /// barato: un solo escaneo acotado contra N consultas puntuales). El cruce real (extraer los
    /// números del texto libre y comparar) lo hace <c>Program.cs</c>.
    /// </summary>
    public async Task<IReadOnlyList<RemisionRow>> ObtenerRemisionesRecientesAsync(int diasAtras, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                IdAsientoContable,
                DocumentoNúmero AS DocumentoNumero,
                Fecha,
                Personalizado4  AS Cotizacion
            FROM [CuentasContables - Asientos]
            WHERE prefijo = 'REM'
              AND (senAnulado = 0 OR senAnulado IS NULL)
              AND Fecha >= DATEADD(day, -@DiasAtras, CAST(GETDATE() AS DATE))
              AND Personalizado4 IS NOT NULL AND Personalizado4 <> '';
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { DiasAtras = diasAtras }, cancellationToken: ct);
        var filas = await conexion.QueryAsync<RemisionRow>(comando);
        return filas.ToList();
    }

    /// <summary>
    /// Cotizaciones recientes (prefijo <c>COT</c>, no anuladas) marcadas a propósito por el vendedor
    /// como Hoja de Alistamiento sin OC (2026-10-22): el campo "Contacto" del encabezado en World
    /// Office (<c>Personalizado1</c> para este prefijo — confirmado por SQL contra la Cotización
    /// 16314, ver CONTEXTO_PROYECTO... en memoria) debe CONTENER la palabra "INVENTARIO" (no
    /// exigimos que sea igual, el usuario dejó la puerta abierta a que el vendedor escriba algo más
    /// en el mismo campo, ej. "INVENTARIO - BOGOTÁ"). Es una señal EXPLÍCITA puesta a propósito por
    /// quien sabe si la venta va en serio — reemplaza la idea descartada de traer TODA Cotización
    /// reciente sin más filtro, que traía ruido (Cotizaciones que nunca se concretan).
    /// <paramref name="fechaInicio"/> es un piso FIJO (pedido explícito del usuario: "que solo busque
    /// a partir de hoy, lo antiguo no lo traiga") — el día en que esta función se activó en
    /// producción, para que nunca traiga Cotizaciones viejas aunque por casualidad tengan
    /// "INVENTARIO" en ese campo por otra razón. <paramref name="diasAtras"/> es solo una cota de
    /// rendimiento adicional (para no escanear una ventana cada vez más vieja con el paso de los
    /// meses) — entre las dos gana la más reciente, nunca se baja del piso fijo. Program.cs decide
    /// cuáles ya existen en nuestro sistema (el endpoint de creación es idempotente de todas formas).
    /// </summary>
    public async Task<IReadOnlyList<CotizacionRecienteRow>> ObtenerCotizacionesRecientesAsync(DateTime fechaInicio, int diasAtras, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                DocumentoNúmero AS DocumentoNumero,
                Fecha
            FROM [CuentasContables - Asientos]
            WHERE prefijo = 'COT'
              AND (senAnulado = 0 OR senAnulado IS NULL)
              AND Personalizado1 LIKE '%INVENTARIO%'
              AND Fecha >= @FechaInicio
              AND Fecha >= DATEADD(day, -@DiasAtras, CAST(GETDATE() AS DATE));
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { FechaInicio = fechaInicio, DiasAtras = diasAtras }, cancellationToken: ct);
        var filas = await conexion.QueryAsync<CotizacionRecienteRow>(comando);
        return filas.ToList();
    }

    /// <summary>Resuelve varios <c>IdCuentaContable</c> de una vez contra el catálogo <c>CuentasContables</c> (código real + nombre impresos).</summary>
    public async Task<IReadOnlyList<CuentaContableRow>> ObtenerCuentasContablesAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];

        const string sql = """
            SELECT
                IdCuentaContable,
                CódigoCuentaContable AS CodigoCuentaContable,
                CuentaContable
            FROM CuentasContables
            WHERE IdCuentaContable IN @Ids;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { Ids = ids }, cancellationToken: ct);
        var filas = await conexion.QueryAsync<CuentaContableRow>(comando);
        return filas.ToList();
    }

    /// <summary>
    /// Cuentas por pagar abiertas (2026-11-05) desde <c>Vista_CuentasPorPagar_Detallada</c> — la vista
    /// de World Office que ya descuenta los abonos (una factura pagada del todo no aparece), así que
    /// no se recalcula nada acá. Solo las cuentas cuyo código empieza por alguno de
    /// <paramref name="prefijosCuenta"/> (ej. 2205 proveedores nacionales, 2210 del exterior, 2335
    /// costos y gastos por pagar) — la vista también trae bancos, socios, nómina y anticipos de
    /// clientes, que no son pagos a proveedores. Se usan las columnas sin tilde, que la vista trae
    /// duplicadas (<c>DocumentoNumero</c>, <c>NumDocumentoExterno</c>, <c>CodigoCuentaContable</c>).
    /// </summary>
    public async Task<IReadOnlyList<PartidaCuentaPorPagarRow>> ObtenerCuentasPorPagarAsync(IReadOnlyList<string> prefijosCuenta, CancellationToken ct = default)
    {
        if (prefijosCuenta.Count == 0) return [];

        var parametros = new DynamicParameters();
        var condiciones = prefijosCuenta.Select((prefijo, i) =>
        {
            parametros.Add($"C{i}", prefijo + "%");
            return $"CodigoCuentaContable LIKE @C{i}";
        }).ToList();

        var sql = $"""
            SELECT
                AbonaADocumento,
                Identificacion,
                Nombres_terceros     AS NombresTerceros,
                Documento,
                prefijo              AS Prefijo,
                DocumentoNumero,
                NumDocumentoExterno,
                Fecha,
                Vencimiento,
                Saldo,
                CodigoCuentaContable
            FROM Vista_CuentasPorPagar_Detallada
            WHERE Saldo <> 0 AND ({string.Join(" OR ", condiciones)});
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, parametros, cancellationToken: ct);
        var filas = await conexion.QueryAsync<PartidaCuentaPorPagarRow>(comando);
        return filas.ToList();
    }

    /// <summary>
    /// Anticipos a proveedores sin cruzar, POR PARTIDA (2026-11-06; antes era un total por tercero):
    /// cada anticipo es un renglón de la cuenta de anticipos (<paramref name="prefijoCuenta"/>, ej.
    /// 133005) que se autorreferencia en <c>AbonaADocumento</c>, y los cruces posteriores apuntan a
    /// ese mismo Id — agrupando por <c>AbonaADocumento</c> sale lo que le queda a cada uno (confirmado
    /// contra WO real, ronda 4 de exploracion-crear-ce-wo). Los movimientos sin <c>AbonaADocumento</c>
    /// (4 en total al 2026-11-06, residuos para depurar) se ignoran: no se pueden cruzar. Prefijo,
    /// número, fecha y nota salen del renglón de origen — la nota es lo que dice a qué OC corresponde.
    /// </summary>
    public async Task<IReadOnlyList<AnticipoProveedorRow>> ObtenerAnticiposProveedorAsync(string prefijoCuenta, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                d.AbonaADocumento            AS IdDocumentoDetalle,
                t.Identificacion,
                LTRIM(RTRIM(ISNULL(t.Nombre, '') + ' ' + ISNULL(t.Apellidos, ''))) AS NombreTercero,
                COUNT(*)                     AS Movimientos,
                SUM(d.Débito - d.Crédito)    AS Saldo,
                MIN(o.prefijo)               AS Prefijo,
                MIN(o.DocumentoNúmero)       AS DocumentoNumero,
                MIN(o.Fecha)                 AS Fecha,
                MIN(CAST(dor.Nota AS nvarchar(300))) AS Nota
            FROM [CuentasContables - Asientos - Detalles] d
            JOIN [CuentasContables - Asientos] a ON a.IdAsientoContable = d.IdAsientoContable
            JOIN Terceros t ON t.IdTercero = d.IdTercero
            LEFT JOIN [CuentasContables - Asientos - Detalles] dor ON dor.IdDocumentoDetalle = d.AbonaADocumento
            LEFT JOIN [CuentasContables - Asientos] o ON o.IdAsientoContable = dor.IdAsientoContable
            WHERE (a.senAnulado = 0 OR a.senAnulado IS NULL)
              AND d.AbonaADocumento IS NOT NULL
              AND d.IdCuentaContable IN (SELECT IdCuentaContable FROM CuentasContables WHERE CódigoCuentaContable LIKE @Cuenta)
            GROUP BY d.AbonaADocumento, t.Identificacion, t.Nombre, t.Apellidos
            HAVING SUM(d.Débito - d.Crédito) <> 0;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { Cuenta = prefijoCuenta + "%" }, cancellationToken: ct);
        var filas = await conexion.QueryAsync<AnticipoProveedorRow>(comando);
        return filas.ToList();
    }

    /// <summary>
    /// Cuentas de banco/caja desde donde se paga a proveedores (2026-11-06): en WO no hay una tabla de
    /// bancos aparte — cada banco es una cuenta contable hoja y activa (ej. 11100502 BANCOLOMBIA CTA
    /// CTE) bajo alguno de <paramref name="prefijosCuenta"/> (1105 caja, 1110 bancos).
    /// </summary>
    public async Task<IReadOnlyList<CuentaContableRow>> ObtenerCuentasBancoAsync(IReadOnlyList<string> prefijosCuenta, CancellationToken ct = default)
    {
        if (prefijosCuenta.Count == 0) return [];

        var parametros = new DynamicParameters();
        var condiciones = prefijosCuenta.Select((prefijo, i) =>
        {
            parametros.Add($"C{i}", prefijo + "%");
            return $"c.CódigoCuentaContable LIKE @C{i}";
        }).ToList();

        var sql = $"""
            SELECT c.IdCuentaContable, c.CódigoCuentaContable AS CodigoCuentaContable, c.CuentaContable
            FROM CuentasContables c
            WHERE ({string.Join(" OR ", condiciones)})
              AND ISNULL(c.Inactivo, 0) = 0
              AND NOT EXISTS (SELECT 1 FROM CuentasContables h WHERE h.SubCuentaContableDe = c.IdCuentaContable)
            ORDER BY c.CódigoCuentaContable;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, parametros, cancellationToken: ct);
        var filas = await conexion.QueryAsync<CuentaContableRow>(comando);
        return filas.ToList();
    }
}
