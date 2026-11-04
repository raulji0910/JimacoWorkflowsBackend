using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarComprobanteEgreso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiereComprobanteEgresoAlAprobar",
                table: "PasosFlujo",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ConflictoComprobanteEgresoWO",
                table: "InstanciasDocumento",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroComprobanteEgresoSolicitado",
                table: "InstanciasDocumento",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PendienteBusquedaComprobanteEgresoWO",
                table: "InstanciasDocumento",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ComprobantesEgreso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstanciaDocumentoId = table.Column<int>(type: "int", nullable: false),
                    NumeroReferencia = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Beneficiario = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NitBeneficiario = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Ciudad = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Telefono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Concepto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ElaboradoPor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprobantesEgreso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComprobantesEgreso_InstanciasDocumento_InstanciaDocumentoId",
                        column: x => x.InstanciaDocumentoId,
                        principalTable: "InstanciasDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RenglonesComprobanteEgreso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComprobanteEgresoId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    CodigoCuenta = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NombreCuenta = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Concepto = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Tercero = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Debito = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Credito = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenglonesComprobanteEgreso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RenglonesComprobanteEgreso_ComprobantesEgreso_ComprobanteEgresoId",
                        column: x => x.ComprobanteEgresoId,
                        principalTable: "ComprobantesEgreso",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesEgreso_InstanciaDocumentoId",
                table: "ComprobantesEgreso",
                column: "InstanciaDocumentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenglonesComprobanteEgreso_ComprobanteEgresoId",
                table: "RenglonesComprobanteEgreso",
                column: "ComprobanteEgresoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RenglonesComprobanteEgreso");

            migrationBuilder.DropTable(
                name: "ComprobantesEgreso");

            migrationBuilder.DropColumn(
                name: "RequiereComprobanteEgresoAlAprobar",
                table: "PasosFlujo");

            migrationBuilder.DropColumn(
                name: "ConflictoComprobanteEgresoWO",
                table: "InstanciasDocumento");

            migrationBuilder.DropColumn(
                name: "NumeroComprobanteEgresoSolicitado",
                table: "InstanciasDocumento");

            migrationBuilder.DropColumn(
                name: "PendienteBusquedaComprobanteEgresoWO",
                table: "InstanciasDocumento");
        }
    }
}
