using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarSincronizacionWO : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UsuarioWO",
                table: "Usuarios",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictoWO",
                table: "InstanciasDocumento",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEscrituraWO",
                table: "InstanciasDocumento",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdAsientoContableOrigen",
                table: "InstanciasDocumento",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PendienteEscrituraWO",
                table: "InstanciasDocumento",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PrefijoOrigen",
                table: "InstanciasDocumento",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsuarioWO",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "ConflictoWO",
                table: "InstanciasDocumento");

            migrationBuilder.DropColumn(
                name: "FechaEscrituraWO",
                table: "InstanciasDocumento");

            migrationBuilder.DropColumn(
                name: "IdAsientoContableOrigen",
                table: "InstanciasDocumento");

            migrationBuilder.DropColumn(
                name: "PendienteEscrituraWO",
                table: "InstanciasDocumento");

            migrationBuilder.DropColumn(
                name: "PrefijoOrigen",
                table: "InstanciasDocumento");
        }
    }
}
