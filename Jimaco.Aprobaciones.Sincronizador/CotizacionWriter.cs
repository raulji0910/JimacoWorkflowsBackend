using System.Data;
using Dapper;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Utilidades;
using Microsoft.Data.SqlClient;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>Configuración de la creación de cotizaciones (appsettings, sección <c>Documentos</c>).</summary>
public record ConfiguracionCotizacion(
    // PRUEB mientras se prueba (prefijo de prueba de COT que ya existe en WO), COT en producción.
    string Prefijo,
    // NomPc del encabezado: "APROBACIONES" distingue los documentos creados por el sistema.
    string NomPc,
    // PersonalizadoN donde va la marca anti-duplicado: uno que NO esté en uso en la COT (P8, senUso 0
    // y sin datos en 2026), para que no se vea en la pantalla ni en la impresión de WO.
    string ColumnaMarca);

public enum ResultadoCreacionCotizacion { Creada, YaExistia, Conflicto }

public record CreacionCotizacionResultado(ResultadoCreacionCotizacion Resultado, string? Prefijo = null, decimal? Numero = null,
    int? IdAsientoContable = null, string? Mensaje = null)
{
    public static CreacionCotizacionResultado ConConflicto(string mensaje) => new(ResultadoCreacionCotizacion.Conflicto, Mensaje: mensaje);
}

/// <summary>
/// Crea en World Office una cotización armada en el portal (2026-11-25), igual a la que graba una
/// vendedora en WO. Plantilla: COT 16915 (IdAsientoContable 130001), que dejó exactamente:
/// <list type="bullet">
/// <item>1 encabezado en <c>[CuentasContables - Asientos]</c> (IdCuentaContableDocumento 'COT', 27 columnas con valor);</item>
/// <item>1 fila por producto en <c>CCA_M_Inventarios</c> (de a una: los triggers de WO leen una sola fila de INSERTED);</item>
/// <item>1 fila en <c>TotalesDocumentos</c> con el total del documento.</item>
/// </list>
/// Una cotización NO contabiliza ni mueve inventario. El número es el mayor del prefijo + 1, tomado
/// con bloqueo dentro de la transacción; si alguien graba el mismo número en el mismo instante,
/// <c>TrgI_ValidaLlave</c> rechaza y se reintenta. Todo en una sola transacción: o queda la
/// cotización completa, o no queda nada. Antes de grabar se revalida en WO el cliente, la sucursal,
/// la forma de pago, la bodega y cada producto (activo y con el mismo código).
/// </summary>
public class CotizacionWriter(string connectionString, ConfiguracionCotizacion config)
{
    private const int MaximoIntentos = 3;
    private const string TipoDocumento = "COT";
    private static readonly string[] ColumnasPersonalizadas = Enumerable.Range(1, 15).Select(n => $"Personalizado{n}").ToArray();

    public static string Marca(int cotizacionId) => $"APROBACIONES #{cotizacionId}";

    /// <summary>El prefijo tiene que tener al menos una COT en WO: un error de digitación no arranca una numeración nueva desde 1.</summary>
    public async Task<bool> PrefijoExisteAsync(CancellationToken ct = default)
    {
        await using var conexion = new SqlConnection(connectionString);
        await conexion.OpenAsync(ct);
        return await conexion.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM [CuentasContables - Asientos]
                                     WHERE IdEmpresa = 1 AND IdCuentaContableDocumento = @Tipo AND prefijo = @Prefijo)
                        THEN 1 ELSE 0 END
            """, new { Tipo = TipoDocumento, config.Prefijo }, cancellationToken: ct));
    }

    public async Task<CreacionCotizacionResultado> CrearAsync(PendienteCreacionCotizacionDto cotizacion, CancellationToken ct = default)
    {
        if (!ColumnasPersonalizadas.Contains(config.ColumnaMarca))
            return CreacionCotizacionResultado.ConConflicto($"Columna de marca inválida en la configuración: {config.ColumnaMarca}.");
        if (cotizacion.MasDatos.Keys.Any(n => $"Personalizado{n}" == config.ColumnaMarca))
            return CreacionCotizacionResultado.ConConflicto($"El campo {config.ColumnaMarca} está reservado para la marca del sistema.");

        for (var intento = 1; ; intento++)
        {
            try
            {
                return await IntentarCrearAsync(cotizacion, ct);
            }
            catch (SqlException ex) when (ex.Message.Contains("La llave ya existe", StringComparison.OrdinalIgnoreCase) && intento < MaximoIntentos)
            {
                Console.WriteLine($"[{DateTime.Now:s}]     El número de cotización se usó al mismo tiempo en WO, reintentando ({intento}/{MaximoIntentos})...");
            }
        }
    }

    private async Task<CreacionCotizacionResultado> IntentarCrearAsync(PendienteCreacionCotizacionDto cot, CancellationToken ct)
    {
        await using var conexion = new SqlConnection(connectionString);
        await conexion.OpenAsync(ct);
        var marca = Marca(cot.CotizacionId);

        // 1. ¿Ya se creó en una corrida anterior (y falló el aviso a la Api)? Se busca por la marca.
        var existente = await conexion.QueryFirstOrDefaultAsync<ExistenteRow>(new CommandDefinition($"""
            SELECT TOP 1 IdAsientoContable, prefijo AS Prefijo, DocumentoNúmero AS DocumentoNumero
            FROM [CuentasContables - Asientos]
            WHERE IdEmpresa = 1 AND IdCuentaContableDocumento = @Tipo AND NomPc = @NomPc AND {config.ColumnaMarca} = @Marca
            ORDER BY IdAsientoContable DESC;
            """, new { Tipo = TipoDocumento, config.NomPc, Marca = marca }, cancellationToken: ct));
        if (existente is not null)
            return new CreacionCotizacionResultado(ResultadoCreacionCotizacion.YaExistia, existente.Prefijo, existente.DocumentoNumero, existente.IdAsientoContable);

        if (cot.Renglones.Count == 0)
            return CreacionCotizacionResultado.ConConflicto("La cotización no tiene productos.");

        await using var tx = (SqlTransaction)await conexion.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // 2. Revalidar en WO lo que viene del catálogo del portal (puede tener unos minutos de atraso).
        var identificacion = await conexion.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(Identificacion)) FROM Terceros WHERE IdTercero = @Id;", new { Id = cot.IdTercero }, tx, cancellationToken: ct));
        if (identificacion is null)
            return await Abortar(tx, "El cliente ya no existe en World Office.", ct);
        if (!string.Equals(identificacion, cot.ClienteIdentificacion.Trim(), StringComparison.OrdinalIgnoreCase))
            return await Abortar(tx, $"El cliente cambió de identificación en World Office ({identificacion}, se esperaba {cot.ClienteIdentificacion}).", ct);

        int? idDireccion = cot.IdDireccion;
        if (idDireccion is not null)
        {
            var deQuien = await conexion.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT IdTercero FROM [Terceros - Direcciones] WHERE IdTerceroDireccion = @Id;", new { Id = idDireccion }, tx, cancellationToken: ct));
            if (deQuien != cot.IdTercero)
                return await Abortar(tx, "La sucursal elegida ya no existe o no es de ese cliente en World Office.", ct);
        }
        else
        {
            idDireccion = await conexion.ExecuteScalarAsync<int?>(new CommandDefinition("""
                SELECT TOP 1 IdTerceroDireccion FROM [Terceros - Direcciones]
                WHERE IdTercero = @IdTercero ORDER BY CASE WHEN ISNULL(senDirecPpal, 0) <> 0 THEN 0 ELSE 1 END, IdTerceroDireccion;
                """, new { cot.IdTercero }, tx, cancellationToken: ct));
        }

        if (!await conexion.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM [Formas De Pago] WHERE IdFormaDePago = @Id) THEN 1 ELSE 0 END;",
                new { Id = cot.IdFormaPago }, tx, cancellationToken: ct)))
            return await Abortar(tx, "La forma de pago ya no existe en World Office.", ct);

        if (!await conexion.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM Terceros WHERE IdTercero = @Id) THEN 1 ELSE 0 END;",
                new { Id = cot.IdTerceroElabora }, tx, cancellationToken: ct)))
            return await Abortar(tx, $"El tercero \"elaborado por\" ({cot.IdTerceroElabora}) no existe en World Office.", ct);

        var ids = cot.Renglones.Select(r => r.IdInventario).Distinct().ToArray();
        var productos = (await conexion.QueryAsync<ProductoRow>(new CommandDefinition("""
            SELECT IdInventario, LTRIM(RTRIM(CódigoInventario)) AS Codigo,
                   CAST(CASE WHEN ISNULL(Activo, 0) <> 0 THEN 1 ELSE 0 END AS bit) AS Activo
            FROM Inventarios WHERE IdInventario IN @Ids;
            """, new { Ids = ids }, tx, cancellationToken: ct))).ToDictionary(p => p.IdInventario);
        var bodegas = (await conexion.QueryAsync<string>(new CommandDefinition(
            "SELECT LTRIM(RTRIM(IdBodegaInventario)) FROM [Inventarios - Bodegas] WHERE ISNULL(Activa, 0) <> 0;",
            transaction: tx, cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var r in cot.Renglones)
        {
            if (!productos.TryGetValue(r.IdInventario, out var p) || !p.Activo)
                return await Abortar(tx, $"El producto {r.Codigo} está inactivo o ya no existe en World Office.", ct);
            if (!string.Equals(p.Codigo, r.Codigo, StringComparison.OrdinalIgnoreCase))
                return await Abortar(tx, $"El producto {r.Codigo} cambió de código en World Office ({p.Codigo}).", ct);
            if (!bodegas.Contains(r.IdBodega))
                return await Abortar(tx, $"La bodega \"{r.IdBodega}\" del producto {r.Codigo} no existe o está inactiva en World Office.", ct);
        }

        // 3. Largos reales de las columnas de texto (para no romper el INSERT).
        var largoNota = await LargoColumnaAsync(conexion, tx, "[dbo].[CuentasContables - Asientos]", "Nota", ct);
        var largoNotaRenglon = await LargoColumnaAsync(conexion, tx, "[dbo].[CCA_M_Inventarios]", "Nota", ct);
        var largoUsuario = await LargoColumnaAsync(conexion, tx, "[dbo].[CuentasContables - Asientos]", "IdUsuarioWO", ct);
        var largoPersonalizado = await LargoColumnaAsync(conexion, tx, "[dbo].[CuentasContables - Asientos]", "Personalizado1", ct);

        // 4. Número: el mayor del prefijo + 1, bloqueando el rango hasta el commit.
        var numero = await conexion.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT ISNULL(MAX(DocumentoNúmero), 0) + 1
            FROM [CuentasContables - Asientos] WITH (UPDLOCK, HOLDLOCK)
            WHERE IdEmpresa = 1 AND IdCuentaContableDocumento = @Tipo AND prefijo = @Prefijo;
            """, new { Tipo = TipoDocumento, config.Prefijo }, tx, cancellationToken: ct));

        // 5. Encabezado — las mismas columnas que dejó la COT 16915, más los "Más Datos" y la marca.
        var personalizados = new Dictionary<string, string>();
        foreach (var (n, valor) in cot.MasDatos)
            if (n is >= 1 and <= 15 && !string.IsNullOrWhiteSpace(valor))
                personalizados[$"Personalizado{n}"] = Recortar(valor.Trim(), largoPersonalizado);
        personalizados[config.ColumnaMarca] = marca;

        var parametros = new DynamicParameters(new
        {
            Tipo = TipoDocumento, config.Prefijo, Numero = numero, Fecha = cot.Fecha.Date, IdTercero = cot.IdTercero,
            IdDireccion = idDireccion, IdElabora = cot.IdTerceroElabora, Nota = Recortar(cot.Concepto, largoNota),
            IdFormaPago = cot.IdFormaPago, config.NomPc, Usuario = Recortar(cot.UsuarioWO, largoUsuario)
        });
        foreach (var (columna, valor) in personalizados)
            parametros.Add(columna, valor);
        var columnasExtra = string.Concat(personalizados.Keys.Select(c => $", {c}"));
        var valoresExtra = string.Concat(personalizados.Keys.Select(c => $", @{c}"));

        var idAsiento = await conexion.ExecuteScalarAsync<int>(new CommandDefinition($"""
            INSERT INTO [CuentasContables - Asientos]
                (IdEmpresa, IdCuentaContableDocumento, prefijo, DocumentoNúmero, Fecha,
                 IdTerceroExterno, IdDirecciónExterno, IdTerceroInterno, Nota, Verificado, IdFormaDePago,
                 PerPagosGrande, Prestamo, NumPagos, InteresMensual, Edad, ClubDeAmigos, NumNinos, NumAdultos,
                 NomPc, senDomicilio, senAnulado, senCreadoEvento, senBloqueado, HoraRegistro, IdUsuarioWO, senElectronico{columnasExtra})
            VALUES
                (1, @Tipo, @Prefijo, @Numero, @Fecha,
                 @IdTercero, @IdDireccion, @IdElabora, @Nota, 0, @IdFormaPago,
                 0, 0, 0, 0, 0, 0, 0, 0,
                 @NomPc, 0, 0, 0, 0, GETDATE(), @Usuario, 0{valoresExtra});
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, parametros, tx, cancellationToken: ct));

        // 6. Renglones, de a uno. Vencimiento = fecha + vigencia (así lo deja WO).
        var vencimiento = cot.Fecha.Date.AddDays(cot.VigenciaDias);
        foreach (var r in cot.Renglones)
        {
            await conexion.ExecuteAsync(new CommandDefinition("""
                INSERT INTO CCA_M_Inventarios
                    (IdAsientoContable, IdInventario, IdBodegaInventario, Cantidad, Cantidad_Conversion,
                     CostoPromedio, CostoPromedio_Conversion, Dcto, FactorConversiónMovimientoABodega, FactorConversiónMovimientoAInventario,
                     HoraRegistro, IdClasificación, ImpoConsumo, Invisible, Iva, MontoMonetarioUnitario, MontoUnitarioExt, Nivel, Nota,
                     PorcImpoConsumo, SenCruzado, senImpreso, SenIvaAlCosto, SenNoAuditar, senObsequio, SenReserva,
                     UnidadDeMedida, Valor_Conversion, Vencimiento, Verificado)
                VALUES
                    (@IdAsiento, @IdInventario, @IdBodega, @Cantidad, @Cantidad,
                     0, 0, 0, 1, 1,
                     GETDATE(), @IdClasificacion, 0, 0, @Iva, @Valor, 0, 0, @Nota,
                     0, 0, 0, 0, -1, 0, 0,
                     @Unidad, @Valor, @Vencimiento, 0);
                """, new
            {
                IdAsiento = idAsiento, r.IdInventario, IdBodega = r.IdBodega, r.Cantidad, IdClasificacion = r.IdClasificacion ?? 2,
                r.Iva, Valor = r.ValorUnitario, Nota = string.IsNullOrWhiteSpace(r.Nota) ? null : Recortar(r.Nota.Trim(), largoNotaRenglon),
                Unidad = r.UnidadMedida, Vencimiento = vencimiento
            }, tx, cancellationToken: ct));
        }

        // 7. Total del documento (WO lo guarda aparte; lo usan búsquedas e informes).
        var total = TotalesWO.Documento(cot.Renglones.Select(r => (r.Cantidad, r.ValorUnitario, r.Iva))).Total;
        if (total != cot.Total)
            return await Abortar(tx, $"El total no cuadra ({total:N0} calculado vs {cot.Total:N0} en el portal). No se grabó nada.", ct);

        var idEsIdentidad = await conexion.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TotalesDocumentos'), 'Id', 'IsIdentity');", transaction: tx, cancellationToken: ct)) == 1;
        await conexion.ExecuteAsync(new CommandDefinition(idEsIdentidad
            ? "INSERT INTO TotalesDocumentos (IdAsientoContable, ValorTotallocal, ValorTotalExt) VALUES (@IdAsiento, @Total, 0);"
            : """
              INSERT INTO TotalesDocumentos (Id, IdAsientoContable, ValorTotallocal, ValorTotalExt)
              SELECT ISNULL(MAX(Id), 0) + 1, @IdAsiento, @Total, 0 FROM TotalesDocumentos WITH (UPDLOCK, HOLDLOCK);
              """, new { IdAsiento = idAsiento, Total = total }, tx, cancellationToken: ct));

        // 8. Control final: lo grabado es lo que se mandó.
        var grabado = await conexion.QueryFirstAsync<(int Renglones, decimal Cantidades)>(new CommandDefinition(
            "SELECT COUNT(*), ISNULL(SUM(Cantidad), 0) FROM CCA_M_Inventarios WHERE IdAsientoContable = @Id;",
            new { Id = idAsiento }, tx, cancellationToken: ct));
        if (grabado.Renglones != cot.Renglones.Count || grabado.Cantidades != cot.Renglones.Sum(r => r.Cantidad))
            return await Abortar(tx, $"Los renglones grabados no cuadran ({grabado.Renglones} de {cot.Renglones.Count}). No se grabó nada.", ct);

        await tx.CommitAsync(ct);
        return new CreacionCotizacionResultado(ResultadoCreacionCotizacion.Creada, config.Prefijo, numero, idAsiento);
    }

    private static async Task<CreacionCotizacionResultado> Abortar(SqlTransaction tx, string mensaje, CancellationToken ct)
    {
        await tx.RollbackAsync(ct);
        return CreacionCotizacionResultado.ConConflicto(mensaje);
    }

    /// <summary>Largo máximo (en caracteres) de una columna de texto de WO. ntext/sin límite = 4000.</summary>
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

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];

    private sealed class ExistenteRow
    {
        public int IdAsientoContable { get; set; }
        public string? Prefijo { get; set; }
        public decimal DocumentoNumero { get; set; }
    }

    private sealed class ProductoRow
    {
        public int IdInventario { get; set; }
        public string? Codigo { get; set; }
        public bool Activo { get; set; }
    }
}
