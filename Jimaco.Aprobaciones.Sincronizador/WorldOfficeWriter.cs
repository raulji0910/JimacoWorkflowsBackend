using Dapper;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Microsoft.Data.SqlClient;

namespace Jimaco.Aprobaciones.Sincronizador;

public enum ResultadoEscrituraWO
{
    /// <summary>Se escribió el valor pedido (1=aprobado o 2=rechazado) ahora mismo.</summary>
    Escrito,

    /// <summary>Ya estaba en el valor pedido en WO (alguien lo hizo por Access, o una corrida anterior) — no hace falta tocar nada, se confirma igual.</summary>
    YaEstabaAprobado,

    /// <summary>La fila está anulada en WO — conflicto real, no se escribe nada.</summary>
    Anulado,

    /// <summary>
    /// La fila tiene un valor de <c>senAprobado</c> distinto al pedido y distinto de "pendiente"
    /// (ej. quisimos escribir "rechazado" pero alguien ya lo había aprobado por Access, o
    /// viceversa) — conflicto real, no se pisa a ciegas.
    /// </summary>
    EstadoDistinto,

    /// <summary>No existe ninguna fila con ese IdAsientoContable + prefijo — no debería pasar nunca, pero no se asume nada si pasa.</summary>
    NoEncontrado
}

/// <summary>
/// Escribe en World Office el resultado del primer paso de un documento sincronizado desde acá —
/// a diferencia de <see cref="WorldOfficeReader"/> (solo lectura, login <c>wf_readonly</c>), esta
/// clase SÍ escribe, con un login separado (ver exploracion-worldoffice.sql, sección de escritura)
/// que solo necesita permiso de <c>UPDATE</c> sobre <c>senAprobado</c>/<c>IdTerceroAprobador</c> en
/// <c>[CuentasContables - Asientos]</c> — nunca toca ninguna otra tabla ni columna, y nunca hace
/// <c>INSERT</c>/<c>DELETE</c>. Es la única escritura automática de todo este proyecto contra WO —
/// ver "Escritura de vuelta a World Office" en CLAUDE.md antes de tocar esta clase.
///
/// <c>senAprobado</c> resultó ser un código de 3 estados, no un booleano (confirmado 2026-09-14
/// contra una fila real marcada "rechazado" en la pantalla nativa de Aprobación de Documentos de
/// WO): <c>0</c>/<c>NULL</c> = pendiente, <c>1</c> = aprobado, <c>2</c> = rechazado.
///
/// Desde 2026-09-16 <see cref="ResultadoPrimerPaso.Devuelto"/> escribe de vuelta a "pendiente"
/// (<c>NULL</c>) en vez de "rechazado" — antes "Devolver" y "Rechazar" escribían lo mismo acá,
/// dejando la OC en un estado terminal aunque la intención fuera "corregí esto y reenvialo".
/// </summary>
public class WorldOfficeWriter(string connectionString)
{
    public async Task<ResultadoEscrituraWO> EscribirPrimerPasoAsync(
        string prefijo, int idAsientoContable, string usuarioWO, ResultadoPrimerPaso resultado, CancellationToken ct = default)
    {
        int? valorDestino = resultado switch
        {
            ResultadoPrimerPaso.Aprobado => 1,
            ResultadoPrimerPaso.Rechazado => 2,
            ResultadoPrimerPaso.Devuelto => null,
            _ => throw new ArgumentOutOfRangeException(nameof(resultado), resultado, null)
        };

        // Al volver a "pendiente" no queda ningún aprobador — limpiar IdTerceroAprobador también,
        // para que la fila quede igual que una OC recién mandada a aprobar. Para Aprobado/Rechazado
        // sigue siendo el usuario real de WO, para trazabilidad (decisión explícita del usuario).
        string? aprobadorDestino = resultado == ResultadoPrimerPaso.Devuelto ? null : usuarioWO;

        await using var conexion = new SqlConnection(connectionString);

        // Chequeo defensivo ANTES de escribir — nunca pisar un valor sin saber qué había. Cubre
        // el caso de que alguien ya haya aprobado/rechazado/anulado esa misma fila en WO (por
        // Access) mientras el documento seguía pendiente en Jimaco Aprobaciones.
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

        // "Pendiente" en WO es NULL o 0 indistintamente — tratarlos como el mismo destino cuando
        // lo que queremos es justamente volver a pendiente, para no forzar un UPDATE redundante.
        var yaEstaEnDestino = valorDestino is null
            ? estado.SenAprobado is null or 0
            : estado.SenAprobado == valorDestino;
        if (yaEstaEnDestino)
            return ResultadoEscrituraWO.YaEstabaAprobado;

        // El estado de partida válido depende de qué se quiere escribir (bug real encontrado y
        // arreglado 2026-09-24, con un caso real de prueba — "Devolver" NUNCA había podido escribir
        // en WO desde que existe esta lógica, 2026-09-16): Aprobado/Rechazado solo tienen sentido
        // viniendo de "pendiente" (NULL/0) — cualquier otro valor ya escrito es un conflicto real
        // (alguien lo aprobó/rechazó por Access mientras tanto). Devuelto es al revés: la
        // transición normal es "se aprobó el paso 1, después se devuelve" — viene de Aprobado (1),
        // no de pendiente. Ahí un conflicto real sería encontrarlo Rechazado (2, terminal), no
        // Aprobado.
        var estadoDePartidaValido = resultado == ResultadoPrimerPaso.Devuelto
            ? estado.SenAprobado == 1
            : estado.SenAprobado is null or 0;

        if (!estadoDePartidaValido)
            return ResultadoEscrituraWO.EstadoDistinto;

        // El WHERE también tiene que exigir el estado de partida correcto según el caso — con
        // Devuelto viene de senAprobado=1, no de pendiente (mismo motivo que arriba).
        var sqlUpdate = resultado == ResultadoPrimerPaso.Devuelto
            ? """
              UPDATE [CuentasContables - Asientos]
              SET senAprobado = @ValorDestino, IdTerceroAprobador = @Aprobador
              WHERE IdAsientoContable = @IdAsientoContable AND prefijo = @Prefijo
                AND senAprobado = 1
                AND (senAnulado = 0 OR senAnulado IS NULL);
              """
            : """
              UPDATE [CuentasContables - Asientos]
              SET senAprobado = @ValorDestino, IdTerceroAprobador = @Aprobador
              WHERE IdAsientoContable = @IdAsientoContable AND prefijo = @Prefijo
                AND (senAprobado = 0 OR senAprobado IS NULL)
                AND (senAnulado = 0 OR senAnulado IS NULL);
              """;
        var filas = await conexion.ExecuteAsync(
            new CommandDefinition(sqlUpdate, new { IdAsientoContable = idAsientoContable, Prefijo = prefijo, Aprobador = aprobadorDestino, ValorDestino = valorDestino }, cancellationToken: ct));

        // Si "filas" da 0 acá es porque justo en el medio (entre el SELECT de arriba y este
        // UPDATE) alguien más lo tocó — no es un error, la próxima corrida lo va a ver limpio
        // (YaEstabaAprobado o EstadoDistinto según corresponda).
        return filas > 0 ? ResultadoEscrituraWO.Escrito : ResultadoEscrituraWO.YaEstabaAprobado;
    }

    private class EstadoActualRow
    {
        public int? SenAprobado { get; set; }
        public int? SenAnulado { get; set; }
    }
}
