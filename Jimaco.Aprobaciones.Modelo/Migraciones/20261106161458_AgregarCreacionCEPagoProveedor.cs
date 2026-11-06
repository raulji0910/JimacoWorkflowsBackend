using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarCreacionCEPagoProveedor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Fecha",
                table: "AnticiposProveedor",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdDocumentoDetalleWO",
                table: "AnticiposProveedor",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nota",
                table: "AnticiposProveedor",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroDocumento",
                table: "AnticiposProveedor",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Prefijo",
                table: "AnticiposProveedor",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AnticiposPagoProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstanciaDocumentoId = table.Column<int>(type: "int", nullable: false),
                    IdDocumentoDetalleWO = table.Column<int>(type: "int", nullable: false),
                    Prefijo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NumeroDocumento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SaldoAnterior = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnticiposPagoProveedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnticiposPagoProveedor_InstanciasDocumento_InstanciaDocumentoId",
                        column: x => x.InstanciaDocumentoId,
                        principalTable: "InstanciasDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CuentasBancoWO",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdCuentaContableWO = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PorDefecto = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CuentasBancoWO", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PagosProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstanciaDocumentoId = table.Column<int>(type: "int", nullable: false),
                    Identificacion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IdCuentaBancoWO = table.Column<int>(type: "int", nullable: true),
                    NombreBanco = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FechaPago = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PendienteCreacionCE = table.Column<bool>(type: "bit", nullable: false),
                    ConflictoCreacionCE = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PrefijoCE = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NumeroCE = table.Column<decimal>(type: "decimal(20,0)", precision: 20, scale: 0, nullable: true),
                    IdAsientoContableCE = table.Column<int>(type: "int", nullable: true),
                    FechaCreacionCE = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagosProveedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagosProveedor_InstanciasDocumento_InstanciaDocumentoId",
                        column: x => x.InstanciaDocumentoId,
                        principalTable: "InstanciasDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnticiposProveedor_IdDocumentoDetalleWO",
                table: "AnticiposProveedor",
                column: "IdDocumentoDetalleWO");

            migrationBuilder.CreateIndex(
                name: "IX_AnticiposPagoProveedor_IdDocumentoDetalleWO",
                table: "AnticiposPagoProveedor",
                column: "IdDocumentoDetalleWO");

            migrationBuilder.CreateIndex(
                name: "IX_AnticiposPagoProveedor_InstanciaDocumentoId",
                table: "AnticiposPagoProveedor",
                column: "InstanciaDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_CuentasBancoWO_IdCuentaContableWO",
                table: "CuentasBancoWO",
                column: "IdCuentaContableWO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_InstanciaDocumentoId",
                table: "PagosProveedor",
                column: "InstanciaDocumentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_PendienteCreacionCE",
                table: "PagosProveedor",
                column: "PendienteCreacionCE");

            // Pagos a proveedor creados antes de esta migración (solo en QA): se les crea su fila de
            // PagoProveedor para que el gerente general los pueda confirmar igual.
            migrationBuilder.Sql("""
                INSERT INTO PagosProveedor (InstanciaDocumentoId, Identificacion, PendienteCreacionCE)
                SELECT i.Id, ISNULL(JSON_VALUE(i.DatosJson, '$.nit'), ''), 0
                FROM InstanciasDocumento i
                JOIN TiposDocumento t ON t.Id = i.TipoDocumentoId
                WHERE t.PrefijoWorldOffice = 'CXP'
                  AND NOT EXISTS (SELECT 1 FROM PagosProveedor p WHERE p.InstanciaDocumentoId = i.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnticiposPagoProveedor");

            migrationBuilder.DropTable(
                name: "CuentasBancoWO");

            migrationBuilder.DropTable(
                name: "PagosProveedor");

            migrationBuilder.DropIndex(
                name: "IX_AnticiposProveedor_IdDocumentoDetalleWO",
                table: "AnticiposProveedor");

            migrationBuilder.DropColumn(
                name: "Fecha",
                table: "AnticiposProveedor");

            migrationBuilder.DropColumn(
                name: "IdDocumentoDetalleWO",
                table: "AnticiposProveedor");

            migrationBuilder.DropColumn(
                name: "Nota",
                table: "AnticiposProveedor");

            migrationBuilder.DropColumn(
                name: "NumeroDocumento",
                table: "AnticiposProveedor");

            migrationBuilder.DropColumn(
                name: "Prefijo",
                table: "AnticiposProveedor");
        }
    }
}
