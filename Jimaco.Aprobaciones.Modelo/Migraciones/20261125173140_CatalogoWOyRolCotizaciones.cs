using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class CatalogoWOyRolCotizaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PuedeCrearCotizaciones",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BodegasWO",
                columns: table => new
                {
                    IdBodegaWO = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Activa = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BodegasWO", x => x.IdBodegaWO);
                });

            migrationBuilder.CreateTable(
                name: "CamposPersonalizadosWO",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TipoDocumento = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Etiqueta = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CamposPersonalizadosWO", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DireccionesTerceroWO",
                columns: table => new
                {
                    IdDireccionWO = table.Column<int>(type: "int", nullable: false),
                    IdTerceroWO = table.Column<int>(type: "int", nullable: false),
                    Direccion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Telefonos = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Ciudad = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Principal = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DireccionesTerceroWO", x => x.IdDireccionWO);
                });

            migrationBuilder.CreateTable(
                name: "ExistenciasWO",
                columns: table => new
                {
                    IdInventarioWO = table.Column<int>(type: "int", nullable: false),
                    IdBodegaWO = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Existencia = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExistenciasWO", x => new { x.IdInventarioWO, x.IdBodegaWO });
                });

            migrationBuilder.CreateTable(
                name: "FormasPagoWO",
                columns: table => new
                {
                    IdFormaPagoWO = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormasPagoWO", x => x.IdFormaPagoWO);
                });

            migrationBuilder.CreateTable(
                name: "ProductosWO",
                columns: table => new
                {
                    IdInventarioWO = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Iva = table.Column<decimal>(type: "decimal(9,6)", nullable: false),
                    IdClasificacion = table.Column<int>(type: "int", nullable: true),
                    Precio1 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    VersionWO = table.Column<long>(type: "bigint", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductosWO", x => x.IdInventarioWO);
                });

            migrationBuilder.CreateTable(
                name: "SincronizacionCatalogoWO",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    UltimaVersionProductos = table.Column<long>(type: "bigint", nullable: false),
                    FechaProductos = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaExistencias = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaCatalogos = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SincronizacionCatalogoWO", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "PuedeCrearCotizaciones",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_CamposPersonalizadosWO_TipoDocumento_Numero",
                table: "CamposPersonalizadosWO",
                columns: new[] { "TipoDocumento", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DireccionesTerceroWO_IdTerceroWO",
                table: "DireccionesTerceroWO",
                column: "IdTerceroWO");

            migrationBuilder.CreateIndex(
                name: "IX_ProductosWO_Codigo",
                table: "ProductosWO",
                column: "Codigo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BodegasWO");

            migrationBuilder.DropTable(
                name: "CamposPersonalizadosWO");

            migrationBuilder.DropTable(
                name: "DireccionesTerceroWO");

            migrationBuilder.DropTable(
                name: "ExistenciasWO");

            migrationBuilder.DropTable(
                name: "FormasPagoWO");

            migrationBuilder.DropTable(
                name: "ProductosWO");

            migrationBuilder.DropTable(
                name: "SincronizacionCatalogoWO");

            migrationBuilder.DropColumn(
                name: "PuedeCrearCotizaciones",
                table: "Roles");
        }
    }
}
