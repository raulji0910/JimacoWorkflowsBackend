using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarFacturacionRemisiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aviso",
                table: "SoportesRemision",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IaTipoDetectado",
                table: "SoportesRemision",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoDocumentoFacturacionId",
                table: "SoportesRemision",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PuedeDocumentarEntregas",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PuedeFacturarEntregas",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EstadoFacturacion",
                table: "Remisiones",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FechaEnvioFacturacion",
                table: "Remisiones",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventosFacturacionRemision",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RemisionId = table.Column<int>(type: "int", nullable: false),
                    Accion = table.Column<int>(type: "int", nullable: false),
                    Comentario = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosFacturacionRemision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosFacturacionRemision_Remisiones_RemisionId",
                        column: x => x.RemisionId,
                        principalTable: "Remisiones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventosFacturacionRemision_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TiposDocumentoFacturacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Obligatorio = table.Column<bool>(type: "bit", nullable: false),
                    NitsClientes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposDocumentoFacturacion", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PuedeDocumentarEntregas", "PuedeFacturarEntregas" },
                values: new object[] { false, false });

            migrationBuilder.InsertData(
                table: "TiposDocumentoFacturacion",
                columns: new[] { "Id", "Activo", "Descripcion", "NitsClientes", "Nombre", "Obligatorio", "Orden" },
                values: new object[,]
                {
                    { 1, true, "Orden de compra que el cliente le emitió a Jimaco (número de OC, ítems, cantidades).", null, "Orden de Compra", true, 1 },
                    { 2, true, "Entrada de almacén / recibo de mercancía del cliente que constata que recibió lo entregado por Jimaco.", null, "Entrada de almacén", true, 2 },
                    { 3, true, "Factura proforma del pedido.", "900268747;900331322", "Proforma", true, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_SoportesRemision_TipoDocumentoFacturacionId",
                table: "SoportesRemision",
                column: "TipoDocumentoFacturacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_ElaboradoPorWO",
                table: "Remisiones",
                column: "ElaboradoPorWO");

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_EstadoFacturacion",
                table: "Remisiones",
                column: "EstadoFacturacion");

            migrationBuilder.CreateIndex(
                name: "IX_EventosFacturacionRemision_RemisionId",
                table: "EventosFacturacionRemision",
                column: "RemisionId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosFacturacionRemision_UsuarioId",
                table: "EventosFacturacionRemision",
                column: "UsuarioId");

            migrationBuilder.AddForeignKey(
                name: "FK_SoportesRemision_TiposDocumentoFacturacion_TipoDocumentoFacturacionId",
                table: "SoportesRemision",
                column: "TipoDocumentoFacturacionId",
                principalTable: "TiposDocumentoFacturacion",
                principalColumn: "Id");

            // Las remisiones que ya estaban entregadas antes de la fase 2 arrancan "pendientes de documentos".
            migrationBuilder.Sql("UPDATE Remisiones SET EstadoFacturacion = 0 WHERE Estado = 1 AND EstadoFacturacion IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SoportesRemision_TiposDocumentoFacturacion_TipoDocumentoFacturacionId",
                table: "SoportesRemision");

            migrationBuilder.DropTable(
                name: "EventosFacturacionRemision");

            migrationBuilder.DropTable(
                name: "TiposDocumentoFacturacion");

            migrationBuilder.DropIndex(
                name: "IX_SoportesRemision_TipoDocumentoFacturacionId",
                table: "SoportesRemision");

            migrationBuilder.DropIndex(
                name: "IX_Remisiones_ElaboradoPorWO",
                table: "Remisiones");

            migrationBuilder.DropIndex(
                name: "IX_Remisiones_EstadoFacturacion",
                table: "Remisiones");

            migrationBuilder.DropColumn(
                name: "Aviso",
                table: "SoportesRemision");

            migrationBuilder.DropColumn(
                name: "IaTipoDetectado",
                table: "SoportesRemision");

            migrationBuilder.DropColumn(
                name: "TipoDocumentoFacturacionId",
                table: "SoportesRemision");

            migrationBuilder.DropColumn(
                name: "PuedeDocumentarEntregas",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PuedeFacturarEntregas",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "EstadoFacturacion",
                table: "Remisiones");

            migrationBuilder.DropColumn(
                name: "FechaEnvioFacturacion",
                table: "Remisiones");
        }
    }
}
