using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarVerificacionIASoporteRemision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IaHayFirma",
                table: "SoportesRemision",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IaNumeroLeido",
                table: "SoportesRemision",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IaObservacion",
                table: "SoportesRemision",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Valido",
                table: "SoportesRemision",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IaHayFirma",
                table: "SoportesRemision");

            migrationBuilder.DropColumn(
                name: "IaNumeroLeido",
                table: "SoportesRemision");

            migrationBuilder.DropColumn(
                name: "IaObservacion",
                table: "SoportesRemision");

            migrationBuilder.DropColumn(
                name: "Valido",
                table: "SoportesRemision");
        }
    }
}
