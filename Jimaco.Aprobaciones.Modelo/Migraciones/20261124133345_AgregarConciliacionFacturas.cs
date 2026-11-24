using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarConciliacionFacturas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PuedeVerConciliacion",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "FacturasCompraWorldOffice",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdAsientoContable = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    NitProveedor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NombreProveedor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DocumentoExterno = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferenciaOrdenes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Anulada = table.Column<bool>(type: "bit", nullable: false),
                    FechaSincronizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacturasCompraWorldOffice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FacturasProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Cufe = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TipoDocumento = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    FechaEmision = table.Column<DateOnly>(type: "date", nullable: false),
                    NitEmisor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NombreEmisor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NitReceptor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReferenciaOrden = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    XmlGzip = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CorreoAsunto = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorreoFecha = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Correo")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacturasProveedor", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ObservacionesOrdenCompra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdAsientoContable = table.Column<int>(type: "int", nullable: false),
                    NumeroOrden = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    NombreUsuario = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservacionesOrdenCompra", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrdenesCompraWorldOffice",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdAsientoContable = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    NitProveedor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NombreProveedor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Emisor = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Anulada = table.Column<bool>(type: "bit", nullable: false),
                    FechaSincronizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdenesCompraWorldOffice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SincronizacionConciliacion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    UltimaSincronizacionOrdenes = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SincronizacionConciliacion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FacturaProveedorRenglones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FacturaProveedorId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PorcentajeIva = table.Column<decimal>(type: "decimal(5,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacturaProveedorRenglones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacturaProveedorRenglones_FacturasProveedor_FacturaProveedorId",
                        column: x => x.FacturaProveedorId,
                        principalTable: "FacturasProveedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ObservacionesFacturaProveedor",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FacturaProveedorId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    NombreUsuario = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservacionesFacturaProveedor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObservacionesFacturaProveedor_FacturasProveedor_FacturaProveedorId",
                        column: x => x.FacturaProveedorId,
                        principalTable: "FacturasProveedor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrdenCompraWorldOfficeRenglones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrdenCompraWorldOfficeId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ValorUnitario = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    PorcentajeIva = table.Column<decimal>(type: "decimal(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdenCompraWorldOfficeRenglones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrdenCompraWorldOfficeRenglones_OrdenesCompraWorldOffice_OrdenCompraWorldOfficeId",
                        column: x => x.OrdenCompraWorldOfficeId,
                        principalTable: "OrdenesCompraWorldOffice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "PuedeVerConciliacion",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_FacturaProveedorRenglones_FacturaProveedorId",
                table: "FacturaProveedorRenglones",
                column: "FacturaProveedorId");

            migrationBuilder.CreateIndex(
                name: "IX_FacturasCompraWorldOffice_Fecha",
                table: "FacturasCompraWorldOffice",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_FacturasCompraWorldOffice_IdAsientoContable",
                table: "FacturasCompraWorldOffice",
                column: "IdAsientoContable",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacturasCompraWorldOffice_NitProveedor",
                table: "FacturasCompraWorldOffice",
                column: "NitProveedor");

            migrationBuilder.CreateIndex(
                name: "IX_FacturasProveedor_Cufe",
                table: "FacturasProveedor",
                column: "Cufe",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacturasProveedor_FechaEmision",
                table: "FacturasProveedor",
                column: "FechaEmision");

            migrationBuilder.CreateIndex(
                name: "IX_FacturasProveedor_NitEmisor",
                table: "FacturasProveedor",
                column: "NitEmisor");

            migrationBuilder.CreateIndex(
                name: "IX_ObservacionesFacturaProveedor_FacturaProveedorId",
                table: "ObservacionesFacturaProveedor",
                column: "FacturaProveedorId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservacionesOrdenCompra_IdAsientoContable",
                table: "ObservacionesOrdenCompra",
                column: "IdAsientoContable");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenCompraWorldOfficeRenglones_OrdenCompraWorldOfficeId",
                table: "OrdenCompraWorldOfficeRenglones",
                column: "OrdenCompraWorldOfficeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompraWorldOffice_Fecha",
                table: "OrdenesCompraWorldOffice",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompraWorldOffice_IdAsientoContable",
                table: "OrdenesCompraWorldOffice",
                column: "IdAsientoContable",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompraWorldOffice_NitProveedor",
                table: "OrdenesCompraWorldOffice",
                column: "NitProveedor");

            migrationBuilder.CreateIndex(
                name: "IX_OrdenesCompraWorldOffice_Numero",
                table: "OrdenesCompraWorldOffice",
                column: "Numero");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacturaProveedorRenglones");

            migrationBuilder.DropTable(
                name: "FacturasCompraWorldOffice");

            migrationBuilder.DropTable(
                name: "ObservacionesFacturaProveedor");

            migrationBuilder.DropTable(
                name: "ObservacionesOrdenCompra");

            migrationBuilder.DropTable(
                name: "OrdenCompraWorldOfficeRenglones");

            migrationBuilder.DropTable(
                name: "SincronizacionConciliacion");

            migrationBuilder.DropTable(
                name: "FacturasProveedor");

            migrationBuilder.DropTable(
                name: "OrdenesCompraWorldOffice");

            migrationBuilder.DropColumn(
                name: "PuedeVerConciliacion",
                table: "Roles");
        }
    }
}
