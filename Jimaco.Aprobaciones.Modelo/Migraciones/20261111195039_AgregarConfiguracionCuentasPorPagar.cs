using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarConfiguracionCuentasPorPagar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionesCuentasPorPagar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    PrefijoCE = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CrearCEEnWorldOffice = table.Column<bool>(type: "bit", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualizadoPorUsuarioId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesCuentasPorPagar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracionesCuentasPorPagar_Usuarios_ActualizadoPorUsuarioId",
                        column: x => x.ActualizadoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "ConfiguracionesCuentasPorPagar",
                columns: new[] { "Id", "ActualizadoPorUsuarioId", "CrearCEEnWorldOffice", "FechaActualizacion", "PrefijoCE" },
                values: new object[] { 1, null, false, null, "CEJ-2" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionesCuentasPorPagar_ActualizadoPorUsuarioId",
                table: "ConfiguracionesCuentasPorPagar",
                column: "ActualizadoPorUsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesCuentasPorPagar");
        }
    }
}
