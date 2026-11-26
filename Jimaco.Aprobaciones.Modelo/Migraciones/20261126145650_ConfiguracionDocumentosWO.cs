using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class ConfiguracionDocumentosWO : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionesDocumentosWO",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    PrefijoCotizacion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CrearCotizacionesEnWorldOffice = table.Column<bool>(type: "bit", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualizadoPorUsuarioId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesDocumentosWO", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracionesDocumentosWO_Usuarios_ActualizadoPorUsuarioId",
                        column: x => x.ActualizadoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "ConfiguracionesDocumentosWO",
                columns: new[] { "Id", "ActualizadoPorUsuarioId", "CrearCotizacionesEnWorldOffice", "FechaActualizacion", "PrefijoCotizacion" },
                values: new object[] { 1, null, false, null, "PRUEB" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionesDocumentosWO_ActualizadoPorUsuarioId",
                table: "ConfiguracionesDocumentosWO",
                column: "ActualizadoPorUsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesDocumentosWO");
        }
    }
}
