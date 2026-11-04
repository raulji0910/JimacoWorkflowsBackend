using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class ParametrizarFlujosPorCondicionYDevolucionMultiple : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PasosFlujo_PasosFlujo_PasoDestinoDevolucionId",
                table: "PasosFlujo");

            migrationBuilder.DropIndex(
                name: "IX_PasosFlujo_PasoDestinoDevolucionId",
                table: "PasosFlujo");

            migrationBuilder.DropColumn(
                name: "PasoDestinoDevolucionId",
                table: "PasosFlujo");

            migrationBuilder.AddColumn<string>(
                name: "FormaDePagoRequerida",
                table: "DefinicionesFlujo",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorMaximo",
                table: "DefinicionesFlujo",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorMinimo",
                table: "DefinicionesFlujo",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PasoFlujoDestinosDevolucion",
                columns: table => new
                {
                    PasoFlujoOrigenId = table.Column<int>(type: "int", nullable: false),
                    PasoFlujoDestinoId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasoFlujoDestinosDevolucion", x => new { x.PasoFlujoOrigenId, x.PasoFlujoDestinoId });
                    table.ForeignKey(
                        name: "FK_PasoFlujoDestinosDevolucion_PasosFlujo_PasoFlujoDestinoId",
                        column: x => x.PasoFlujoDestinoId,
                        principalTable: "PasosFlujo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PasoFlujoDestinosDevolucion_PasosFlujo_PasoFlujoOrigenId",
                        column: x => x.PasoFlujoOrigenId,
                        principalTable: "PasosFlujo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasoFlujoDestinosDevolucion_PasoFlujoDestinoId",
                table: "PasoFlujoDestinosDevolucion",
                column: "PasoFlujoDestinoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasoFlujoDestinosDevolucion");

            migrationBuilder.DropColumn(
                name: "FormaDePagoRequerida",
                table: "DefinicionesFlujo");

            migrationBuilder.DropColumn(
                name: "ValorMaximo",
                table: "DefinicionesFlujo");

            migrationBuilder.DropColumn(
                name: "ValorMinimo",
                table: "DefinicionesFlujo");

            migrationBuilder.AddColumn<int>(
                name: "PasoDestinoDevolucionId",
                table: "PasosFlujo",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasosFlujo_PasoDestinoDevolucionId",
                table: "PasosFlujo",
                column: "PasoDestinoDevolucionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PasosFlujo_PasosFlujo_PasoDestinoDevolucionId",
                table: "PasosFlujo",
                column: "PasoDestinoDevolucionId",
                principalTable: "PasosFlujo",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
