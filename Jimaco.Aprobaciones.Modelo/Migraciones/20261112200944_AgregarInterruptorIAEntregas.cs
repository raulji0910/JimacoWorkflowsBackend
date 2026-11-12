using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarInterruptorIAEntregas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActualizadoPorUsuarioId",
                table: "ConfiguracionesEntregas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaActualizacion",
                table: "ConfiguracionesEntregas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VerificacionIAActiva",
                table: "ConfiguracionesEntregas",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionesEntregas_ActualizadoPorUsuarioId",
                table: "ConfiguracionesEntregas",
                column: "ActualizadoPorUsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConfiguracionesEntregas_Usuarios_ActualizadoPorUsuarioId",
                table: "ConfiguracionesEntregas",
                column: "ActualizadoPorUsuarioId",
                principalTable: "Usuarios",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConfiguracionesEntregas_Usuarios_ActualizadoPorUsuarioId",
                table: "ConfiguracionesEntregas");

            migrationBuilder.DropIndex(
                name: "IX_ConfiguracionesEntregas_ActualizadoPorUsuarioId",
                table: "ConfiguracionesEntregas");

            migrationBuilder.DropColumn(
                name: "ActualizadoPorUsuarioId",
                table: "ConfiguracionesEntregas");

            migrationBuilder.DropColumn(
                name: "FechaActualizacion",
                table: "ConfiguracionesEntregas");

            migrationBuilder.DropColumn(
                name: "VerificacionIAActiva",
                table: "ConfiguracionesEntregas");
        }
    }
}
