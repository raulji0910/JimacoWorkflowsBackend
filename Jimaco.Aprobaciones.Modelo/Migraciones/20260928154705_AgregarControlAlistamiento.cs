using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarControlAlistamiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PuedeGestionarAlistamiento",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PuedeVerAlistamiento",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CantidadDisponible",
                table: "RenglonesDocumentoVinculado",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            // Las filas ya existentes no tienen forma de saber cuándo se generó realmente su Hoja de
            // Alistamiento — arrancarlas en la fecha mínima dispararía una alerta de "atraso" falsa
            // apenas arranque el chequeo periódico. Se usa el momento del despliegue como punto de
            // partida en su lugar (más razonable que year 1, aunque no sea el dato real histórico).
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCreacion",
                table: "DocumentosVinculados",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaAlertaEnviada",
                table: "DocumentosVinculados",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PuedeGestionarAlistamiento", "PuedeVerAlistamiento" },
                values: new object[] { false, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PuedeGestionarAlistamiento",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PuedeVerAlistamiento",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "CantidadDisponible",
                table: "RenglonesDocumentoVinculado");

            migrationBuilder.DropColumn(
                name: "FechaCreacion",
                table: "DocumentosVinculados");

            migrationBuilder.DropColumn(
                name: "UltimaAlertaEnviada",
                table: "DocumentosVinculados");
        }
    }
}
