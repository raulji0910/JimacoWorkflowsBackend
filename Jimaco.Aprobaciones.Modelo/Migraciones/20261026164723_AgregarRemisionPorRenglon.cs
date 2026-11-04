using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarRemisionPorRenglon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemisionesRenglon",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RenglonDocumentoVinculadoId = table.Column<int>(type: "int", nullable: false),
                    NumeroRemision = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CantidadRemisionada = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemisionesRenglon", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RemisionesRenglon_RenglonesDocumentoVinculado_RenglonDocumentoVinculadoId",
                        column: x => x.RenglonDocumentoVinculadoId,
                        principalTable: "RenglonesDocumentoVinculado",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemisionesRenglon_RenglonDocumentoVinculadoId",
                table: "RemisionesRenglon",
                column: "RenglonDocumentoVinculadoId");

            // Backfill (2026-10-26, acotado a pedido del usuario): antes de perder las columnas
            // viejas, se preserva lo que ya estaba marcado como Remisionado a nivel de documento —
            // PERO solo para lo de hace más de una semana. Insertar un despacho "completo" acá marca
            // ese renglón como 100% remisionado, y eso lo saca para siempre de
            // ListarPendientesRemisionAsync (el Sincronizador nunca vuelve a cruzarlo contra WO real
            // una vez que ya no aparece pendiente) — así que si se backfillea TODO lo histórico, el
            // desglose fino real (qué Remisión mandó qué cantidad, para los casos de despacho
            // parcial — ver Hoja 16099/16228) queda congelado para siempre en una aproximación.
            // Dejando SIN marcar lo de la última semana, esas Hojas siguen apareciendo como
            // pendientes y el Sincronizador las cruza solas en su primera corrida real contra WO,
            // con el desglose fino completo (mismo mecanismo normal, sin nada especial). Lo de más
            // de una semana sí se backfillea aproximado (un solo despacho completo), porque ya son
            // Hojas cerradas que nadie va a seguir gestionando — no vale la pena perseguir precisión
            // histórica ahí.
            migrationBuilder.Sql("""
                INSERT INTO RemisionesRenglon (RenglonDocumentoVinculadoId, NumeroRemision, Fecha, CantidadRemisionada)
                SELECT r.Id, d.NumeroRemision, ISNULL(d.FechaRemisionado, d.Fecha), r.Cantidad
                FROM DocumentosVinculados d
                INNER JOIN RenglonesDocumentoVinculado r ON r.DocumentoVinculadoId = d.Id
                WHERE d.Remisionado = 1 AND d.NumeroRemision IS NOT NULL
                  AND ISNULL(d.FechaRemisionado, d.Fecha) < DATEADD(day, -7, CAST(GETDATE() AS DATE));
                """);

            migrationBuilder.DropColumn(
                name: "FechaRemisionado",
                table: "DocumentosVinculados");

            migrationBuilder.DropColumn(
                name: "NumeroRemision",
                table: "DocumentosVinculados");

            migrationBuilder.DropColumn(
                name: "Remisionado",
                table: "DocumentosVinculados");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemisionesRenglon");

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaRemisionado",
                table: "DocumentosVinculados",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroRemision",
                table: "DocumentosVinculados",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Remisionado",
                table: "DocumentosVinculados",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
