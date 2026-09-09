using Dapper;
using Microsoft.Data.SqlClient;

namespace Jimaco.Aprobaciones.Sincronizador;

public enum ResultadoEscrituraWO
{
    /// <summary>Se escribió senAprobado=1 ahora mismo.</summary>
    Escrito,

    /// <summary>Ya estaba aprobado en WO (alguien lo aprobó por Access, o una corrida anterior) — no hace falta tocar nada, se confirma igual.</summary>
    YaEstabaAprobado,

    /// <summary>La fila está anulada en WO — conflicto real, no se escribe nada.</summary>
    Anulado,

    /// <summary>No existe ninguna fila con ese IdAsientoContable + prefijo — no debería pasar nunca, pero no se asume nada si pasa.</summary>
    NoEncontrado
}

/// <summary>
/// Escribe en World Office la aprobación del primer paso de un documento sincronizado desde acá —
/// a diferencia de <see cref="WorldOfficeReader"/> (solo lectura, login <c>wf_readonly</c>), esta
/// clase SÍ escribe, con un login separado (ver exploracion-worldoffice.sql, sección de escritura)
/// que solo necesita permiso de <c>UPDATE</c> sobre <c>senAprobado</c>/<c>IdTerceroAprobador</c> en
/// <c>[CuentasContables - Asientos]</c> — nunca toca ninguna otra tabla ni columna, y nunca hace
/// <c>INSERT</c>/<c>DELETE</c>. Es la única escritura automática de todo este proyecto contra WO —
/// ver "Escritura de vuelta a World Office" en CLAUDE.md antes de tocar esta clase.
/// </summary>
public class WorldOfficeWriter(string connectionString)
{
    public async Task<ResultadoEscrituraWO> AprobarPrimerPasoAsync(
        string prefijo, int idAsientoContable, string usuarioWO, CancellationToken ct = default)
    {
        await using var conexion = new SqlConnection(connectionString);

        // Chequeo defensivo ANTES de escribir — nunca pisar un valor sin saber qué había. Cubre
        // el caso de que alguien ya haya aprobado o anulado esa misma fila en WO (por Access)
        // mientras el documento seguía pendiente en Jimaco Aprobaciones.
        const string sqlEstado = """
            SELECT senAprobado AS SenAprobado, senAnulado AS SenAnulado
            FROM [CuentasContables - Asientos]
            WHERE IdAsientoContable = @IdAsientoContable AND prefijo = @Prefijo;
            """;
        var estado = await conexion.QueryFirstOrDefaultAsync<EstadoActualRow>(
            new CommandDefinition(sqlEstado, new { IdAsientoContable = idAsientoContable, Prefijo = prefijo }, cancellationToken: ct));

        if (estado is null)
            return ResultadoEscrituraWO.NoEncontrado;

        if (estado.SenAnulado is not null && estado.SenAnulado != 0)
            return ResultadoEscrituraWO.Anulado;

        if (estado.SenAprobado is not null && estado.SenAprobado != 0)
            return ResultadoEscrituraWO.YaEstabaAprobado;

        const string sqlUpdate = """
            UPDATE [CuentasContables - Asientos]
            SET senAprobado = 1, IdTerceroAprobador = @UsuarioWO
            WHERE IdAsientoContable = @IdAsientoContable AND prefijo = @Prefijo
              AND (senAprobado = 0 OR senAprobado IS NULL)
              AND (senAnulado = 0 OR senAnulado IS NULL);
            """;
        var filas = await conexion.ExecuteAsync(
            new CommandDefinition(sqlUpdate, new { IdAsientoContable = idAsientoContable, Prefijo = prefijo, UsuarioWO = usuarioWO }, cancellationToken: ct));

        // Si "filas" da 0 acá es porque justo en el medio (entre el SELECT de arriba y este
        // UPDATE) alguien más lo aprobó o anuló — no es un error, la próxima corrida lo va a ver
        // como YaEstabaAprobado/Anulado limpio.
        return filas > 0 ? ResultadoEscrituraWO.Escrito : ResultadoEscrituraWO.YaEstabaAprobado;
    }

    private class EstadoActualRow
    {
        public int? SenAprobado { get; set; }
        public int? SenAnulado { get; set; }
    }
}
