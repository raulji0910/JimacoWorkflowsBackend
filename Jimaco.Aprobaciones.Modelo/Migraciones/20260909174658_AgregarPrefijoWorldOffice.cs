using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarPrefijoWorldOffice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Respaldo "OC" para filas existentes: hoy el único TipoDocumento en uso es
            // "Orden de Compra", y ese es justo su prefijo real en World Office (ver
            // esquema-worldoffice-oc.md) — no es un placeholder al azar.
            migrationBuilder.AddColumn<string>(
                name: "PrefijoWorldOffice",
                table: "TiposDocumento",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "OC");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrefijoWorldOffice",
                table: "TiposDocumento");
        }
    }
}
