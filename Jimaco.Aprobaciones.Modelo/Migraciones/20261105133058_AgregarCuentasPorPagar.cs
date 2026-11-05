using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarCuentasPorPagar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PuedeVerCuentasPorPagar",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AnticiposProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Identificacion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NombreTercero = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Movimientos = table.Column<int>(type: "int", nullable: false),
                    Saldo = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnticiposProveedor", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FacturasPagoProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstanciaDocumentoId = table.Column<int>(type: "int", nullable: false),
                    IdDocumentoDetalleWO = table.Column<int>(type: "int", nullable: false),
                    Prefijo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NumeroDocumento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    NumeroDocumentoExterno = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Vencimiento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CodigoCuenta = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacturasPagoProveedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacturasPagoProveedor_InstanciasDocumento_InstanciaDocumentoId",
                        column: x => x.InstanciaDocumentoId,
                        principalTable: "InstanciasDocumento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartidasCuentaPorPagar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdDocumentoDetalleWO = table.Column<int>(type: "int", nullable: true),
                    Identificacion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NombreTercero = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Documento = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Prefijo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NumeroDocumento = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    NumeroDocumentoExterno = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Vencimiento = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Pendiente = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CodigoCuenta = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartidasCuentaPorPagar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SincronizacionesCuentasPorPagar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaCorte = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Partidas = table.Column<int>(type: "int", nullable: false),
                    Anticipos = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SincronizacionesCuentasPorPagar", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "PuedeVerCuentasPorPagar",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_AnticiposProveedor_Identificacion",
                table: "AnticiposProveedor",
                column: "Identificacion");

            migrationBuilder.CreateIndex(
                name: "IX_FacturasPagoProveedor_IdDocumentoDetalleWO",
                table: "FacturasPagoProveedor",
                column: "IdDocumentoDetalleWO");

            migrationBuilder.CreateIndex(
                name: "IX_FacturasPagoProveedor_InstanciaDocumentoId",
                table: "FacturasPagoProveedor",
                column: "InstanciaDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_PartidasCuentaPorPagar_IdDocumentoDetalleWO",
                table: "PartidasCuentaPorPagar",
                column: "IdDocumentoDetalleWO");

            migrationBuilder.CreateIndex(
                name: "IX_PartidasCuentaPorPagar_Identificacion",
                table: "PartidasCuentaPorPagar",
                column: "Identificacion");

            // Tipo de documento de los pagos que decide Gerencia Comercial desde el módulo de Cuentas
            // por Pagar. "CXP" no existe en World Office a propósito (ver
            // CuentasPorPagarService.PrefijoPagoProveedor). El flujo (Contabilidad → Gerencia General)
            // se configura desde la pantalla de Flujos en cada ambiente, porque los roles cambian
            // entre ambientes.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM TiposDocumento WHERE PrefijoWorldOffice = 'CXP' OR Nombre = 'Pago a Proveedor')
                    INSERT INTO TiposDocumento (Nombre, Descripcion, PrefijoWorldOffice, Activo)
                    VALUES ('Pago a Proveedor', 'Facturas de proveedores que Gerencia Comercial decidió pagar desde Cuentas por Pagar.', 'CXP', 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnticiposProveedor");

            migrationBuilder.DropTable(
                name: "FacturasPagoProveedor");

            migrationBuilder.DropTable(
                name: "PartidasCuentaPorPagar");

            migrationBuilder.DropTable(
                name: "SincronizacionesCuentasPorPagar");

            migrationBuilder.DropColumn(
                name: "PuedeVerCuentasPorPagar",
                table: "Roles");
        }
    }
}
