using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarRemisionado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
