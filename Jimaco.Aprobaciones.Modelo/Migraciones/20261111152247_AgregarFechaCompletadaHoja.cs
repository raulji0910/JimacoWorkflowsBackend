using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarFechaCompletadaHoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCompletada",
                table: "DocumentosVinculados",
                type: "datetime2",
                nullable: true);

            // Hojas que HOY ya están completas: su fecha es el último cambio de Disponible registrado en
            // la bitácora (existe desde 2026-11-09). Sin bitácora quedan en null (orden de siempre).
            migrationBuilder.Sql("""
                UPDATE d SET FechaCompletada = b.UltimaFecha
                FROM DocumentosVinculados d
                CROSS APPLY (SELECT MAX(h.Fecha) AS UltimaFecha FROM HistorialDisponible h
                             WHERE h.PrefijoOrigen = d.PrefijoOrigen AND h.NumeroReferencia = d.NumeroReferencia) b
                WHERE b.UltimaFecha IS NOT NULL
                  AND EXISTS (SELECT 1 FROM RenglonesDocumentoVinculado r WHERE r.DocumentoVinculadoId = d.Id)
                  AND NOT EXISTS (SELECT 1 FROM RenglonesDocumentoVinculado r
                                  WHERE r.DocumentoVinculadoId = d.Id AND r.CantidadDisponible < r.Cantidad);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaCompletada",
                table: "DocumentosVinculados");
        }
    }
}
