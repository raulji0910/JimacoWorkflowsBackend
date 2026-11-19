using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarAprobacionAutomaticaConPasos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AprobacionAutomaticaAlLlegar",
                table: "DefinicionesFlujo",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // 2026-11-19 — pedido real: las OC a crédito de FERREALIADOS se siguen aprobando solas,
            // pero en vez de completarse pasan a Logística ("Recogida Transporte", rol Area de
            // Transporte, como el paso 2 del crédito de Jimaco) y solo la Asistente de Gerencia las
            // puede devolver (permiso "Devolver cualquier documento" — el paso NO permite devolver).
            // Se ubica el flujo por sus condiciones (empresa Ferrealiados, Credito, activo, sin pasos)
            // y el rol por nombre: los Ids no son los mismos en QA y producción. Las OC de ese flujo
            // que ya estaban Completadas pasan a ese paso ("todas las OC a crédito de Ferrealiados
            // deben quedar en este flujo"). Si no encuentra flujo o rol, no hace nada.
            migrationBuilder.Sql("""
                DECLARE @flujo int = (SELECT TOP 1 f.Id FROM DefinicionesFlujo f
                    JOIN TiposDocumento t ON t.Id = f.TipoDocumentoId
                    WHERE t.Empresa = 1 AND f.Activo = 1 AND f.FormaDePagoRequerida = 'Credito'
                      AND NOT EXISTS (SELECT 1 FROM PasosFlujo p WHERE p.DefinicionFlujoId = f.Id)
                    ORDER BY f.Id);
                DECLARE @rol int = (SELECT TOP 1 Id FROM Roles WHERE Nombre = N'Area de Transporte' AND Activo = 1 ORDER BY Id);

                IF @flujo IS NOT NULL AND @rol IS NOT NULL
                BEGIN
                    UPDATE DefinicionesFlujo
                    SET AprobacionAutomaticaAlLlegar = 1,
                        Nombre = N'OC Ferrealiados Crédito (aprobada al llegar, a Logística)'
                    WHERE Id = @flujo;

                    INSERT INTO PasosFlujo (DefinicionFlujoId, Nombre, Orden, PermiteDevolver, PermiteRechazar, PermiteDevolverAlEmisor,
                        RequiereComprobanteEgresoAlAprobar, RequiereComentarioAlAprobar, RequiereSoportePagoAlAprobar, PasoRedirigirAPagoContadoId)
                    VALUES (@flujo, N'Recogida Transporte', 1, 0, 0, 0, 0, 0, 0, NULL);
                    DECLARE @paso int = SCOPE_IDENTITY();

                    INSERT INTO PasoFlujoRoles (PasoFlujoId, RolId) VALUES (@paso, @rol);

                    UPDATE InstanciasDocumento
                    SET Estado = 1, PasoActualId = @paso
                    WHERE DefinicionFlujoId = @flujo AND Estado = 3;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AprobacionAutomaticaAlLlegar",
                table: "DefinicionesFlujo");
        }
    }
}
