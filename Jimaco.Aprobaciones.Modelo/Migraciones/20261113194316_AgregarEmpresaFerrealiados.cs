using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarEmpresaFerrealiados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HistorialDisponible_PrefijoOrigen_NumeroReferencia_Codigo",
                table: "HistorialDisponible");

            migrationBuilder.AddColumn<string>(
                name: "UsuarioWOFerrealiados",
                table: "Usuarios",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Empresa",
                table: "TiposDocumento",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PuedeGestionarAlistamientoFerrealiados",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PuedeVerAlistamientoFerrealiados",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Empresa",
                table: "InstanciasDocumento",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Empresa",
                table: "HistorialDisponible",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Empresa",
                table: "DocumentosVinculados",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PuedeGestionarAlistamientoFerrealiados", "PuedeVerAlistamientoFerrealiados" },
                values: new object[] { false, false });

            migrationBuilder.CreateIndex(
                name: "IX_InstanciasDocumento_Empresa_PrefijoOrigen_IdAsientoContableOrigen",
                table: "InstanciasDocumento",
                columns: new[] { "Empresa", "PrefijoOrigen", "IdAsientoContableOrigen" });

            migrationBuilder.CreateIndex(
                name: "IX_HistorialDisponible_Empresa_PrefijoOrigen_NumeroReferencia_Codigo",
                table: "HistorialDisponible",
                columns: new[] { "Empresa", "PrefijoOrigen", "NumeroReferencia", "Codigo" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosVinculados_Empresa_PrefijoOrigen_NumeroReferencia",
                table: "DocumentosVinculados",
                columns: new[] { "Empresa", "PrefijoOrigen", "NumeroReferencia" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstanciasDocumento_Empresa_PrefijoOrigen_IdAsientoContableOrigen",
                table: "InstanciasDocumento");

            migrationBuilder.DropIndex(
                name: "IX_HistorialDisponible_Empresa_PrefijoOrigen_NumeroReferencia_Codigo",
                table: "HistorialDisponible");

            migrationBuilder.DropIndex(
                name: "IX_DocumentosVinculados_Empresa_PrefijoOrigen_NumeroReferencia",
                table: "DocumentosVinculados");

            migrationBuilder.DropColumn(
                name: "UsuarioWOFerrealiados",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Empresa",
                table: "TiposDocumento");

            migrationBuilder.DropColumn(
                name: "PuedeGestionarAlistamientoFerrealiados",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PuedeVerAlistamientoFerrealiados",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "Empresa",
                table: "InstanciasDocumento");

            migrationBuilder.DropColumn(
                name: "Empresa",
                table: "HistorialDisponible");

            migrationBuilder.DropColumn(
                name: "Empresa",
                table: "DocumentosVinculados");

            migrationBuilder.CreateIndex(
                name: "IX_HistorialDisponible_PrefijoOrigen_NumeroReferencia_Codigo",
                table: "HistorialDisponible",
                columns: new[] { "PrefijoOrigen", "NumeroReferencia", "Codigo" });
        }
    }
}
