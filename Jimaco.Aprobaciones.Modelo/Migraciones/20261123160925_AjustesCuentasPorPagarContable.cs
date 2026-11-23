using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AjustesCuentasPorPagarContable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PuedeConsultarPagos",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CuentasIncluidas",
                table: "ConfiguracionesCuentasPorPagar",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "2205,2210,2335");

            migrationBuilder.CreateTable(
                name: "SoportesLotePago",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LotePagoId = table.Column<int>(type: "int", nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    RutaArchivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoportesLotePago", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoportesLotePago_LotesPago_LotePagoId",
                        column: x => x.LotePagoId,
                        principalTable: "LotesPago",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ConfiguracionesCuentasPorPagar",
                keyColumn: "Id",
                keyValue: 1,
                column: "CuentasIncluidas",
                value: "2205,2210,2335");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                column: "PuedeConsultarPagos",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_SoportesLotePago_LotePagoId",
                table: "SoportesLotePago",
                column: "LotePagoId");

            // Los lotes que ya existen tenían UN soporte en LotesPago: se copia como su soporte n.º 1
            // (las columnas viejas se conservan para poder volver a la imagen anterior de la Api).
            migrationBuilder.Sql("""
                INSERT INTO SoportesLotePago (LotePagoId, NombreArchivo, RutaArchivo, ContentType, TamanoBytes, Orden)
                SELECT l.Id, COALESCE(l.NombreArchivoSoporte, 'soporte.pdf'), l.RutaArchivoSoporte, 'application/pdf',
                       COALESCE((SELECT TOP 1 a.TamanoBytes FROM Adjuntos a WHERE a.RutaArchivo = l.RutaArchivoSoporte), 0), 1
                FROM LotesPago l
                WHERE l.RutaArchivoSoporte IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SoportesLotePago");

            migrationBuilder.DropColumn(
                name: "PuedeConsultarPagos",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "CuentasIncluidas",
                table: "ConfiguracionesCuentasPorPagar");
        }
    }
}
