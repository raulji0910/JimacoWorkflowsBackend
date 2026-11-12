using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarEntregasRemisiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PuedeGestionarEntregas",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PuedeVerEntregas",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConfiguracionesEntregas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesEntregas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Remisiones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdAsientoContableWO = table.Column<int>(type: "int", nullable: false),
                    Prefijo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Cliente = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NitCliente = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Direccion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Ciudad = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Telefono = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Vendedor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ElaboradoPorWO = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FormaDePago = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DireccionEntrega = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CampoEntrega = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    OrdenCompraCliente = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Cotizacion = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    FechaSincronizacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaEntrega = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EntregadaPorUsuarioId = table.Column<int>(type: "int", nullable: true),
                    ObservacionEntrega = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VerificacionQR = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Remisiones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Remisiones_Usuarios_EntregadaPorUsuarioId",
                        column: x => x.EntregadaPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RenglonesRemision",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RemisionId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnidadMedida = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenglonesRemision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RenglonesRemision_Remisiones_RemisionId",
                        column: x => x.RemisionId,
                        principalTable: "Remisiones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SoportesRemision",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RemisionId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RutaArchivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    TamanoBytes = table.Column<long>(type: "bigint", nullable: false),
                    QrLeido = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubidoPorUsuarioId = table.Column<int>(type: "int", nullable: false),
                    FechaCarga = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoportesRemision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoportesRemision_Remisiones_RemisionId",
                        column: x => x.RemisionId,
                        principalTable: "Remisiones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SoportesRemision_Usuarios_SubidoPorUsuarioId",
                        column: x => x.SubidoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PuedeGestionarEntregas", "PuedeVerEntregas" },
                values: new object[] { false, false });

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_EntregadaPorUsuarioId",
                table: "Remisiones",
                column: "EntregadaPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_Estado",
                table: "Remisiones",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_IdAsientoContableWO",
                table: "Remisiones",
                column: "IdAsientoContableWO",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Remisiones_Prefijo_Numero",
                table: "Remisiones",
                columns: new[] { "Prefijo", "Numero" });

            migrationBuilder.CreateIndex(
                name: "IX_RenglonesRemision_RemisionId",
                table: "RenglonesRemision",
                column: "RemisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SoportesRemision_RemisionId",
                table: "SoportesRemision",
                column: "RemisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SoportesRemision_SubidoPorUsuarioId",
                table: "SoportesRemision",
                column: "SubidoPorUsuarioId");
        
            // Fecha de inicio = el día (hora Colombia) en que se aplica esta migración, o sea el día
            // que el módulo sale a ese ambiente: "desde el día que salga a producción" (2026-11-12).
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM ConfiguracionesEntregas WHERE Id = 1)
                    INSERT INTO ConfiguracionesEntregas (Id, FechaInicio)
                    VALUES (1, CAST(DATEADD(hour, -5, SYSUTCDATETIME()) AS date));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesEntregas");

            migrationBuilder.DropTable(
                name: "RenglonesRemision");

            migrationBuilder.DropTable(
                name: "SoportesRemision");

            migrationBuilder.DropTable(
                name: "Remisiones");

            migrationBuilder.DropColumn(
                name: "PuedeGestionarEntregas",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PuedeVerEntregas",
                table: "Roles");
        }
    }
}
