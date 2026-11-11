using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class DesactivarTiposGiroDeMayor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 27,
                column: "Activo",
                value: false);

            migrationBuilder.UpdateData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 46,
                column: "Activo",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 27,
                column: "Activo",
                value: true);

            migrationBuilder.UpdateData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 46,
                column: "Activo",
                value: true);
        }
    }
}
