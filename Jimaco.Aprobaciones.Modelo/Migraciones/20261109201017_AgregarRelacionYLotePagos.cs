using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarRelacionYLotePagos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCorteTerceros",
                table: "SincronizacionesCuentasPorPagar",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PuedePagarCuentas",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CodigoCuentaGiro",
                table: "PagosProveedor",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Concepto",
                table: "PagosProveedor",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EsExtraordinario",
                table: "PagosProveedor",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "GiroEsAnticipo",
                table: "PagosProveedor",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "IdTerceroWO",
                table: "PagosProveedor",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LotePagoId",
                table: "PagosProveedor",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombreTercero",
                table: "PagosProveedor",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombreTipoGiro",
                table: "PagosProveedor",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelacionPagoId",
                table: "PagosProveedor",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TipoGiroId",
                table: "PagosProveedor",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BorradoresPagoProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Identificacion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NombreTercero = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FacturasIds = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AnticiposIds = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorradoresPagoProveedor", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LotesPago",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    PagadoPorUsuarioId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaPago = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IdCuentaBancoWO = table.Column<int>(type: "int", nullable: true),
                    NombreBanco = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NombreArchivoSoporte = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RutaArchivoSoporte = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AvisoTerminadoEnviado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotesPago", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RelacionesPago",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    CreadoPorUsuarioId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelacionesPago", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TercerosWO",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdTerceroWO = table.Column<int>(type: "int", nullable: false),
                    Identificacion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TercerosWO", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TiposGiro",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CodigoCuenta = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EsAnticipo = table.Column<bool>(type: "bit", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TiposGiro", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "PuedePagarCuentas",
                value: false);

            migrationBuilder.InsertData(
                table: "TiposGiro",
                columns: new[] { "Id", "Activo", "CodigoCuenta", "EsAnticipo", "Nombre", "Orden" },
                values: new object[] { 1, true, "133005", true, "Anticipo a proveedores", 1 });

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_LotePagoId",
                table: "PagosProveedor",
                column: "LotePagoId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosProveedor_RelacionPagoId",
                table: "PagosProveedor",
                column: "RelacionPagoId");

            migrationBuilder.CreateIndex(
                name: "IX_BorradoresPagoProveedor_UsuarioId_Identificacion",
                table: "BorradoresPagoProveedor",
                columns: new[] { "UsuarioId", "Identificacion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LotesPago_Numero",
                table: "LotesPago",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RelacionesPago_Numero",
                table: "RelacionesPago",
                column: "Numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TercerosWO_Identificacion",
                table: "TercerosWO",
                column: "Identificacion");

            migrationBuilder.CreateIndex(
                name: "IX_TercerosWO_IdTerceroWO",
                table: "TercerosWO",
                column: "IdTerceroWO",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PagosProveedor_LotesPago_LotePagoId",
                table: "PagosProveedor",
                column: "LotePagoId",
                principalTable: "LotesPago",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PagosProveedor_RelacionesPago_RelacionPagoId",
                table: "PagosProveedor",
                column: "RelacionPagoId",
                principalTable: "RelacionesPago",
                principalColumn: "Id");

            // Órdenes creadas antes de este cambio: el nombre del tercero que muestra el menú "Pagos".
            migrationBuilder.Sql("""
                UPDATE p SET NombreTercero = LEFT(i.Proveedor, 300)
                FROM PagosProveedor p JOIN InstanciasDocumento i ON i.Id = p.InstanciaDocumentoId
                WHERE p.NombreTercero IS NULL AND i.Proveedor IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PagosProveedor_LotesPago_LotePagoId",
                table: "PagosProveedor");

            migrationBuilder.DropForeignKey(
                name: "FK_PagosProveedor_RelacionesPago_RelacionPagoId",
                table: "PagosProveedor");

            migrationBuilder.DropTable(
                name: "BorradoresPagoProveedor");

            migrationBuilder.DropTable(
                name: "LotesPago");

            migrationBuilder.DropTable(
                name: "RelacionesPago");

            migrationBuilder.DropTable(
                name: "TercerosWO");

            migrationBuilder.DropTable(
                name: "TiposGiro");

            migrationBuilder.DropIndex(
                name: "IX_PagosProveedor_LotePagoId",
                table: "PagosProveedor");

            migrationBuilder.DropIndex(
                name: "IX_PagosProveedor_RelacionPagoId",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "FechaCorteTerceros",
                table: "SincronizacionesCuentasPorPagar");

            migrationBuilder.DropColumn(
                name: "PuedePagarCuentas",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "CodigoCuentaGiro",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "Concepto",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "EsExtraordinario",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "GiroEsAnticipo",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "IdTerceroWO",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "LotePagoId",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "NombreTercero",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "NombreTipoGiro",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "RelacionPagoId",
                table: "PagosProveedor");

            migrationBuilder.DropColumn(
                name: "TipoGiroId",
                table: "PagosProveedor");
        }
    }
}
