using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class CotizacionesPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IdTerceroWO",
                table: "Usuarios",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cotizaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdTerceroWO = table.Column<int>(type: "int", nullable: true),
                    ClienteIdentificacion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ClienteNombre = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IdDireccionWO = table.Column<int>(type: "int", nullable: true),
                    DireccionTexto = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IdFormaPagoWO = table.Column<int>(type: "int", nullable: true),
                    FormaPagoNombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Concepto = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VigenciaDias = table.Column<int>(type: "int", nullable: false),
                    MasDatosJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaDocumento = table.Column<DateOnly>(type: "date", nullable: true),
                    FechaEnvio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsuarioWO = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IdTerceroElaboraWO = table.Column<int>(type: "int", nullable: true),
                    PrefijoWO = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NumeroWO = table.Column<decimal>(type: "decimal(18,0)", nullable: true),
                    IdAsientoContableWO = table.Column<int>(type: "int", nullable: true),
                    FechaCreacionWO = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MensajeError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cotizaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cotizaciones_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CotizacionRenglones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CotizacionId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    IdInventarioWO = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    IdBodegaWO = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    IdClasificacion = table.Column<int>(type: "int", nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CotizacionRenglones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CotizacionRenglones_Cotizaciones_CotizacionId",
                        column: x => x.CotizacionId,
                        principalTable: "Cotizaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cotizaciones_Estado",
                table: "Cotizaciones",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Cotizaciones_UsuarioId_Estado",
                table: "Cotizaciones",
                columns: new[] { "UsuarioId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_CotizacionRenglones_CotizacionId",
                table: "CotizacionRenglones",
                column: "CotizacionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CotizacionRenglones");

            migrationBuilder.DropTable(
                name: "Cotizaciones");

            migrationBuilder.DropColumn(
                name: "IdTerceroWO",
                table: "Usuarios");
        }
    }
}
