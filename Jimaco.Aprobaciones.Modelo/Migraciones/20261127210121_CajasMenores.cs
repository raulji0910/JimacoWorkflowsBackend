using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class CajasMenores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PuedeAdministrarCajaFerrealiados",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PuedeAdministrarCajaJimaco",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PuedeGirarCajaFerrealiados",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PuedeGirarCajaJimaco",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiereGiroCajaAlAprobar",
                table: "PasosFlujo",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BeneficiariosCaja",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Cedula = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Correo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    EsInterno = table.Column<bool>(type: "bit", nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreadoPorUsuarioId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeneficiariosCaja", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BeneficiariosCaja_Usuarios_CreadoPorUsuarioId",
                        column: x => x.CreadoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Cajas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Empresa = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tope = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaldoMinimoAlerta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaldoActual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AvisoSaldoBajoEnviado = table.Column<bool>(type: "bit", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualizadoPorUsuarioId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cajas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cajas_Usuarios_ActualizadoPorUsuarioId",
                        column: x => x.ActualizadoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConceptosCaja",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CajaId = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptosCaja", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptosCaja_Cajas_CajaId",
                        column: x => x.CajaId,
                        principalTable: "Cajas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GirosCaja",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CajaId = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    BeneficiarioCajaId = table.Column<int>(type: "int", nullable: false),
                    ConceptoCajaId = table.Column<int>(type: "int", nullable: false),
                    ValorEntregado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Nota = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GiradoPorUsuarioId = table.Column<int>(type: "int", nullable: false),
                    ValorLegalizado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FechaLegalizacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LegalizadoPorUsuarioId = table.Column<int>(type: "int", nullable: true),
                    NotaLegalizacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ValorVueltasRecibidas = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SoporteNombreArchivo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SoporteRutaArchivo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SoporteContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    UltimoRecordatorio = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladoPorUsuarioId = table.Column<int>(type: "int", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GirosCaja", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GirosCaja_BeneficiariosCaja_BeneficiarioCajaId",
                        column: x => x.BeneficiarioCajaId,
                        principalTable: "BeneficiariosCaja",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GirosCaja_Cajas_CajaId",
                        column: x => x.CajaId,
                        principalTable: "Cajas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GirosCaja_ConceptosCaja_ConceptoCajaId",
                        column: x => x.ConceptoCajaId,
                        principalTable: "ConceptosCaja",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GirosCaja_Usuarios_AnuladoPorUsuarioId",
                        column: x => x.AnuladoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GirosCaja_Usuarios_GiradoPorUsuarioId",
                        column: x => x.GiradoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GirosCaja_Usuarios_LegalizadoPorUsuarioId",
                        column: x => x.LegalizadoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GirosCajaOrdenesCompra",
                columns: table => new
                {
                    GiroCajaId = table.Column<int>(type: "int", nullable: false),
                    InstanciaDocumentoId = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GirosCajaOrdenesCompra", x => new { x.GiroCajaId, x.InstanciaDocumentoId });
                    table.ForeignKey(
                        name: "FK_GirosCajaOrdenesCompra_GirosCaja_GiroCajaId",
                        column: x => x.GiroCajaId,
                        principalTable: "GirosCaja",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GirosCajaOrdenesCompra_InstanciasDocumento_InstanciaDocumentoId",
                        column: x => x.InstanciaDocumentoId,
                        principalTable: "InstanciasDocumento",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MovimientosCaja",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CajaId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<int>(type: "int", nullable: false),
                    Valor = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaldoDespues = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: false),
                    Nota = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GiroCajaId = table.Column<int>(type: "int", nullable: true),
                    Anulado = table.Column<bool>(type: "bit", nullable: false),
                    FechaAnulacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnuladoPorUsuarioId = table.Column<int>(type: "int", nullable: true),
                    MotivoAnulacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimientosCaja", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimientosCaja_Cajas_CajaId",
                        column: x => x.CajaId,
                        principalTable: "Cajas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovimientosCaja_GirosCaja_GiroCajaId",
                        column: x => x.GiroCajaId,
                        principalTable: "GirosCaja",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MovimientosCaja_Usuarios_AnuladoPorUsuarioId",
                        column: x => x.AnuladoPorUsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MovimientosCaja_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Cajas",
                columns: new[] { "Id", "ActualizadoPorUsuarioId", "AvisoSaldoBajoEnviado", "Empresa", "FechaActualizacion", "Nombre", "SaldoActual", "SaldoMinimoAlerta", "Tope", "Version" },
                values: new object[,]
                {
                    { 1, null, false, 0, null, "Caja menor Jimaco", 0m, 0m, 2000000m, new Guid("6f1d2c3a-0000-4000-8000-000000000001") },
                    { 2, null, false, 1, null, "Caja menor Ferrealiados", 0m, 0m, 2000000m, new Guid("6f1d2c3a-0000-4000-8000-000000000002") }
                });

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "PuedeAdministrarCajaFerrealiados", "PuedeAdministrarCajaJimaco", "PuedeGirarCajaFerrealiados", "PuedeGirarCajaJimaco" },
                values: new object[] { false, false, false, false });

            migrationBuilder.CreateIndex(
                name: "IX_BeneficiariosCaja_Cedula",
                table: "BeneficiariosCaja",
                column: "Cedula",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BeneficiariosCaja_CreadoPorUsuarioId",
                table: "BeneficiariosCaja",
                column: "CreadoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Cajas_ActualizadoPorUsuarioId",
                table: "Cajas",
                column: "ActualizadoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Cajas_Empresa",
                table: "Cajas",
                column: "Empresa",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConceptosCaja_CajaId_Nombre",
                table: "ConceptosCaja",
                columns: new[] { "CajaId", "Nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GirosCaja_AnuladoPorUsuarioId",
                table: "GirosCaja",
                column: "AnuladoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_GirosCaja_BeneficiarioCajaId",
                table: "GirosCaja",
                column: "BeneficiarioCajaId");

            migrationBuilder.CreateIndex(
                name: "IX_GirosCaja_CajaId_Estado",
                table: "GirosCaja",
                columns: new[] { "CajaId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_GirosCaja_CajaId_Numero",
                table: "GirosCaja",
                columns: new[] { "CajaId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GirosCaja_ConceptoCajaId",
                table: "GirosCaja",
                column: "ConceptoCajaId");

            migrationBuilder.CreateIndex(
                name: "IX_GirosCaja_GiradoPorUsuarioId",
                table: "GirosCaja",
                column: "GiradoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_GirosCaja_LegalizadoPorUsuarioId",
                table: "GirosCaja",
                column: "LegalizadoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_GirosCajaOrdenesCompra_InstanciaDocumentoId",
                table: "GirosCajaOrdenesCompra",
                column: "InstanciaDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCaja_AnuladoPorUsuarioId",
                table: "MovimientosCaja",
                column: "AnuladoPorUsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCaja_CajaId_Fecha",
                table: "MovimientosCaja",
                columns: new[] { "CajaId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCaja_GiroCajaId",
                table: "MovimientosCaja",
                column: "GiroCajaId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosCaja_UsuarioId",
                table: "MovimientosCaja",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GirosCajaOrdenesCompra");

            migrationBuilder.DropTable(
                name: "MovimientosCaja");

            migrationBuilder.DropTable(
                name: "GirosCaja");

            migrationBuilder.DropTable(
                name: "BeneficiariosCaja");

            migrationBuilder.DropTable(
                name: "ConceptosCaja");

            migrationBuilder.DropTable(
                name: "Cajas");

            migrationBuilder.DropColumn(
                name: "PuedeAdministrarCajaFerrealiados",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PuedeAdministrarCajaJimaco",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PuedeGirarCajaFerrealiados",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "PuedeGirarCajaJimaco",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "RequiereGiroCajaAlAprobar",
                table: "PasosFlujo");
        }
    }
}
