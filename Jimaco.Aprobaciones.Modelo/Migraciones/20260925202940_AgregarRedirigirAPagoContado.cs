using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarRedirigirAPagoContado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PasoRedirigirAPagoContadoId",
                table: "PasosFlujo",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasosFlujo_PasoRedirigirAPagoContadoId",
                table: "PasosFlujo",
                column: "PasoRedirigirAPagoContadoId");

            migrationBuilder.AddForeignKey(
                name: "FK_PasosFlujo_PasosFlujo_PasoRedirigirAPagoContadoId",
                table: "PasosFlujo",
                column: "PasoRedirigirAPagoContadoId",
                principalTable: "PasosFlujo",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PasosFlujo_PasosFlujo_PasoRedirigirAPagoContadoId",
                table: "PasosFlujo");

            migrationBuilder.DropIndex(
                name: "IX_PasosFlujo_PasoRedirigirAPagoContadoId",
                table: "PasosFlujo");

            migrationBuilder.DropColumn(
                name: "PasoRedirigirAPagoContadoId",
                table: "PasosFlujo");
        }
    }
}
