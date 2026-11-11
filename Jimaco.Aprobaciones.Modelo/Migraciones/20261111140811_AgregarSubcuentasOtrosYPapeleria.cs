using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarSubcuentasOtrosYPapeleria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "TiposGiro",
                columns: new[] { "Id", "Activo", "CodigoCuenta", "EsAnticipo", "Grupo", "Nombre", "Orden" },
                values: new object[,]
                {
                    { 76, true, "51359505", false, "Administración · Servicios", "51359505 OTROS", 125 },
                    { 77, true, "51953001", false, "Administración · Diversos", "51953001 PAPELERIA", 144 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 76);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 77);
        }
    }
}
