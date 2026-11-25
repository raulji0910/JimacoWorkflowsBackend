using Dapper;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Microsoft.Data.SqlClient;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Lecturas (solo lectura) del catálogo de World Office para crear documentos desde el portal
/// (2026-11-25, modo --documentos). Esquema confirmado contra JIMACO 2023 (consultas del 2026-11-25):
///  - <c>Inventarios</c>: productos; <c>VersionCol</c> es rowversion → sincronización incremental.
///  - <c>Vista_ExistenciasPorBodegas</c>: existencia por producto y bodega, la MISMA que muestra WO
///    (vista cifrada: no se recalcula acá).
///  - <c>Inventarios - Bodegas</c>, <c>Terceros - Direcciones</c> (+ <c>Ciudades</c>), <c>Formas De Pago</c>.
///  - <c>Campos_Pers_Docum</c>: etiquetas de "Más Datos" por tipo de documento (NumParte 1 = encabezado).
/// </summary>
public class WorldOfficeCatalogoReader(string connectionString)
{
    private const int TiempoEsperaSegundos = 300;

    public async Task<IReadOnlyList<ProductoWOInputDto>> ObtenerProductosAsync(long desdeVersion, CancellationToken ct = default)
    {
        // rowversion → binary(8) big-endian: comparar contra el número convertido a binary(8) respeta el orden.
        const string sql = """
            SELECT CAST(IdInventario AS int) AS IdInventario,
                   LTRIM(RTRIM(ISNULL(CódigoInventario, ''))) AS Codigo,
                   LTRIM(RTRIM(ISNULL(Descripción, ''))) AS Descripcion,
                   LTRIM(RTRIM(UnidadDeMedida)) AS UnidadMedida,
                   CAST(ISNULL(Iva, 0) AS decimal(9, 6)) AS Iva,
                   CAST(IdClasificacion AS int) AS IdClasificacion,
                   CAST(ISNULL(Precio1, 0) AS decimal(18, 2)) AS Precio1,
                   CAST(CASE WHEN ISNULL(Activo, 0) <> 0 THEN 1 ELSE 0 END AS bit) AS Activo,
                   CAST(VersionCol AS bigint) AS Version
            FROM Inventarios
            WHERE VersionCol > CAST(@Desde AS binary(8))
            ORDER BY VersionCol;
            """;
        await using var conexion = new SqlConnection(connectionString);
        var filas = await conexion.QueryAsync<ProductoFila>(new CommandDefinition(sql, new { Desde = desdeVersion },
            commandTimeout: TiempoEsperaSegundos, cancellationToken: ct));
        return filas.Select(f => new ProductoWOInputDto(f.IdInventario, f.Codigo, f.Descripcion, f.UnidadMedida, f.Iva,
            f.IdClasificacion, f.Precio1, f.Activo, f.Version)).ToList();
    }

    /// <summary>Solo las existencias distintas de cero (las demás se muestran como 0 en el portal).</summary>
    public async Task<IReadOnlyList<ExistenciaWOInputDto>> ObtenerExistenciasAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT CAST(IdInventario AS int) AS IdInventario, LTRIM(RTRIM(IdBodegaInventario)) AS IdBodega, CAST(Existencia AS decimal(18, 4)) AS Existencia
            FROM Vista_ExistenciasPorBodegas
            WHERE ISNULL(Existencia, 0) <> 0 AND IdBodegaInventario IS NOT NULL;
            """;
        await using var conexion = new SqlConnection(connectionString);
        var filas = await conexion.QueryAsync<ExistenciaWOInputDto>(new CommandDefinition(sql,
            commandTimeout: TiempoEsperaSegundos, cancellationToken: ct));
        return filas.ToList();
    }

    public async Task<CatalogosWOInputDto> ObtenerCatalogosAsync(CancellationToken ct = default)
    {
        await using var conexion = new SqlConnection(connectionString);

        var bodegas = (await conexion.QueryAsync<BodegaWOInputDto>(new CommandDefinition("""
            SELECT LTRIM(RTRIM(IdBodegaInventario)) AS IdBodega, LTRIM(RTRIM(ISNULL(Nombre, IdBodegaInventario))) AS Nombre,
                   CAST(CASE WHEN ISNULL(Activa, 0) <> 0 THEN 1 ELSE 0 END AS bit) AS Activa
            FROM [Inventarios - Bodegas];
            """, cancellationToken: ct))).ToList();

        var direcciones = (await conexion.QueryAsync<DireccionTerceroWOInputDto>(new CommandDefinition("""
            -- CAST explícitos: Dapper arma el record por constructor y exige los tipos exactos.
            SELECT CAST(d.IdTerceroDireccion AS int) AS IdDireccion, CAST(d.IdTercero AS int) AS IdTercero,
                   CAST(LTRIM(RTRIM(d.Dirección)) AS nvarchar(300)) AS Direccion, CAST(LTRIM(RTRIM(d.Teléfonos)) AS nvarchar(100)) AS Telefonos,
                   CAST(LTRIM(RTRIM(c.Ciudad)) AS nvarchar(100)) AS Ciudad,
                   CAST(CASE WHEN ISNULL(d.senDirecPpal, 0) <> 0 THEN 1 ELSE 0 END AS bit) AS Principal
            FROM [Terceros - Direcciones] d
            LEFT JOIN Ciudades c ON c.IdCiudad = d.IdCiudad
            WHERE d.IdTercero IS NOT NULL;
            """, commandTimeout: TiempoEsperaSegundos, cancellationToken: ct))).ToList();

        // [Formas De Pago] no tiene una columna de nombre conocida (igual que en
        // WorldOfficeReader.ObtenerFormaDePagoAsync): se toma la primera columna de texto con valor.
        var formasPago = new List<FormaPagoWOInputDto>();
        foreach (IDictionary<string, object?> fila in await conexion.QueryAsync(new CommandDefinition(
                     "SELECT * FROM [Formas De Pago];", cancellationToken: ct)))
        {
            var id = fila.FirstOrDefault(kv => kv.Key.Equals("IdFormaDePago", StringComparison.OrdinalIgnoreCase)).Value;
            if (id is null) continue;
            var nombre = fila
                .Where(kv => !kv.Key.Equals("IdFormaDePago", StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Value as string)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            formasPago.Add(new FormaPagoWOInputDto(Convert.ToInt32(id), nombre?.Trim() ?? ""));
        }

        var campos = new List<CampoPersonalizadoWOInputDto>();
        foreach (var fila in await conexion.QueryAsync<CampoFila>(new CommandDefinition("""
                     SELECT LTRIM(RTRIM(TipoDoc)) AS TipoDoc, LTRIM(RTRIM(NumCampo)) AS NumCampo,
                            LTRIM(RTRIM(NomEtiqueta)) AS NomEtiqueta, CAST(ISNULL(senUso, 0) AS int) AS SenUso
                     FROM Campos_Pers_Docum
                     WHERE LTRIM(RTRIM(NumParte)) = '1';
                     """, cancellationToken: ct)))
        {
            if (string.IsNullOrWhiteSpace(fila.TipoDoc) || !int.TryParse(fila.NumCampo, out var numero) || numero is < 1 or > 15) continue;
            campos.Add(new CampoPersonalizadoWOInputDto(fila.TipoDoc, numero, fila.NomEtiqueta ?? "", fila.SenUso != 0));
        }

        return new CatalogosWOInputDto(bodegas, direcciones, formasPago, campos);
    }

    private sealed record ProductoFila(int IdInventario, string Codigo, string Descripcion, string? UnidadMedida, decimal Iva,
        int? IdClasificacion, decimal Precio1, bool Activo, long Version);

    private sealed record CampoFila(string? TipoDoc, string? NumCampo, string? NomEtiqueta, int SenUso);
}
