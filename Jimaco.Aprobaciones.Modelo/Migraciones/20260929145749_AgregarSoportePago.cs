using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarSoportePago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiereSoportePagoAlAprobar",
                table: "PasosFlujo",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SoportePagoAdjuntoId",
                table: "InstanciasDocumento",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstanciasDocumento_SoportePagoAdjuntoId",
                table: "InstanciasDocumento",
                column: "SoportePagoAdjuntoId");

            migrationBuilder.AddForeignKey(
                name: "FK_InstanciasDocumento_Adjuntos_SoportePagoAdjuntoId",
                table: "InstanciasDocumento",
                column: "SoportePagoAdjuntoId",
                principalTable: "Adjuntos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InstanciasDocumento_Adjuntos_SoportePagoAdjuntoId",
                table: "InstanciasDocumento");

            migrationBuilder.DropIndex(
                name: "IX_InstanciasDocumento_SoportePagoAdjuntoId",
                table: "InstanciasDocumento");

            migrationBuilder.DropColumn(
                name: "RequiereSoportePagoAlAprobar",
                table: "PasosFlujo");

            migrationBuilder.DropColumn(
                name: "SoportePagoAdjuntoId",
                table: "InstanciasDocumento");
        }
    }
}
