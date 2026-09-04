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
    public async Task<IReadOnlyList<OcHeaderRow>> ObtenerOcNuevasAsync(int marcaDeAgua, CancellationToken ct = default)
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
                IdFormaDePago
            FROM [CuentasContables - Asientos]
            WHERE prefijo = 'OC'
              AND IdAsientoContable > @Marca
              AND (senAnulado = 0 OR senAnulado IS NULL)
            ORDER BY IdAsientoContable;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { Marca = marcaDeAgua }, cancellationToken: ct);
        var filas = await conexion.QueryAsync<OcHeaderRow>(comando);
        return filas.ToList();
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
}
