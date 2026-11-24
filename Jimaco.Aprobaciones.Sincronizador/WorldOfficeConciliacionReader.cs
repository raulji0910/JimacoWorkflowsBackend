using Dapper;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Microsoft.Data.SqlClient;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Lecturas (solo lectura) de World Office para la conciliación de facturas de proveedor
/// (2026-11-24). Mismo esquema que Ferrealiados, calibrado con el WO de Jimaco:
///  - OC: prefijo "OC"; costo = Cantidad × MontoMonetarioUnitario (TotalRenglon NO sirve en Jimaco:
///    a veces el doble, a veces menos, a veces NULL). Emisor = "elaborado por" (IdTerceroInterno).
///  - Factura de compra: prefijo "FCJ" (no "FC"). Número del proveedor completo en NúmDocumentoExterno
///    y la OC que cita Contabilidad en Personalizado1 ("20058", "19691-19583-19602").
/// </summary>
public class WorldOfficeConciliacionReader(string connectionString)
{
    private record CabeceraRow(int IdAsientoContable, decimal DocumentoNumero, DateTime Fecha, string? Identificacion,
        string? Nombre, string? Apellidos, string? EmisorNombre, string? EmisorApellidos, bool Anulada);

    private record FacturaCompraRow(int IdAsientoContable, decimal DocumentoNumero, DateTime Fecha, string? Identificacion,
        string? Nombre, string? Apellidos, string? DocExternoPrefijo, string? DocumentoExterno, string? Personalizado1, bool Anulada);

    private record RenglonRow(int IdAsientoContable, string? Codigo, string? Descripcion, decimal? Cantidad,
        decimal? ValorUnitario, decimal? Iva);

    /// <summary>Todas las OC desde <paramref name="desde"/>, anuladas incluidas (marcadas).</summary>
    public async Task<IReadOnlyList<OrdenCompraEntradaDto>> ObtenerOrdenesCompraAsync(string prefijo, DateOnly desde, CancellationToken ct = default)
    {
        const string sqlCabeceras = """
            SELECT
                a.IdAsientoContable,
                a.DocumentoNúmero AS DocumentoNumero,
                a.Fecha,
                t.Identificacion,
                t.Nombre,
                t.Apellidos,
                e.Nombre    AS EmisorNombre,
                e.Apellidos AS EmisorApellidos,
                CAST(CASE WHEN a.senAnulado = -1 THEN 1 ELSE 0 END AS bit) AS Anulada
            FROM [CuentasContables - Asientos] a
            LEFT JOIN Terceros t ON t.IdTercero = a.IdTerceroExterno
            LEFT JOIN Terceros e ON e.IdTercero = a.IdTerceroInterno
            WHERE a.prefijo = @Prefijo AND a.Fecha >= @Desde;
            """;

        // IVA del producto (Inventarios.Iva, fracción 0.19): la línea no trae su propio porcentaje.
        const string sqlRenglones = """
            SELECT
                c.IdAsientoContable,
                inv.CódigoInventario       AS Codigo,
                inv.Descripción            AS Descripcion,
                c.Cantidad,
                c.MontoMonetarioUnitario   AS ValorUnitario,
                inv.Iva
            FROM CCA_M_Inventarios c
            JOIN [CuentasContables - Asientos] a ON a.IdAsientoContable = c.IdAsientoContable
            LEFT JOIN Inventarios inv ON inv.IdInventario = c.IdInventario
            WHERE a.prefijo = @Prefijo AND a.Fecha >= @Desde
            ORDER BY c.IdAsientoContable, c.[Renglón];
            """;

        await using var conexion = new SqlConnection(connectionString);
        var parametros = new { Prefijo = prefijo, Desde = desde.ToDateTime(TimeOnly.MinValue) };
        var cabeceras = await conexion.QueryAsync<CabeceraRow>(new CommandDefinition(sqlCabeceras, parametros, commandTimeout: 300, cancellationToken: ct));
        var renglones = (await conexion.QueryAsync<RenglonRow>(new CommandDefinition(sqlRenglones, parametros, commandTimeout: 300, cancellationToken: ct)))
            .ToLookup(r => r.IdAsientoContable);

        return cabeceras.Select(c => new OrdenCompraEntradaDto(
                c.IdAsientoContable,
                (int)c.DocumentoNumero,
                DateOnly.FromDateTime(c.Fecha),
                c.Identificacion?.Trim() ?? "",
                $"{c.Nombre} {c.Apellidos}".Trim(),
                c.Anulada,
                renglones[c.IdAsientoContable]
                    .Select(r => new OrdenCompraRenglonEntradaDto(
                        r.Codigo?.Trim(),
                        r.Descripcion?.Trim() ?? "",
                        r.Cantidad ?? 0m,
                        r.ValorUnitario ?? 0m,
                        (r.Iva ?? 0m) * 100m))
                    .ToList(),
                NullSiVacio($"{c.EmisorNombre} {c.EmisorApellidos}")))
            .ToList();
    }

    /// <summary>Encabezados de las facturas de compra (FCJ) desde <paramref name="desde"/> (anuladas marcadas).</summary>
    public async Task<IReadOnlyList<FacturaCompraEntradaDto>> ObtenerFacturasCompraAsync(string prefijo, DateOnly desde, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                a.IdAsientoContable,
                a.DocumentoNúmero          AS DocumentoNumero,
                a.Fecha,
                t.Identificacion,
                t.Nombre,
                t.Apellidos,
                a.DocExternoPrefijo,
                a.NúmDocumentoExterno      AS DocumentoExterno,
                a.Personalizado1,
                CAST(CASE WHEN a.senAnulado = -1 THEN 1 ELSE 0 END AS bit) AS Anulada
            FROM [CuentasContables - Asientos] a
            LEFT JOIN Terceros t ON t.IdTercero = a.IdTerceroExterno
            WHERE a.prefijo = @Prefijo AND a.Fecha >= @Desde;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var filas = await conexion.QueryAsync<FacturaCompraRow>(new CommandDefinition(sql,
            new { Prefijo = prefijo, Desde = desde.ToDateTime(TimeOnly.MinValue) }, commandTimeout: 300, cancellationToken: ct));

        return filas.Select(f => new FacturaCompraEntradaDto(
                f.IdAsientoContable,
                (int)f.DocumentoNumero,
                DateOnly.FromDateTime(f.Fecha),
                f.Identificacion?.Trim() ?? "",
                $"{f.Nombre} {f.Apellidos}".Trim(),
                $"{f.DocExternoPrefijo?.Trim()}{f.DocumentoExterno?.Trim()}",
                f.Personalizado1?.Trim(),
                f.Anulada))
            .ToList();
    }

    private static string? NullSiVacio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
