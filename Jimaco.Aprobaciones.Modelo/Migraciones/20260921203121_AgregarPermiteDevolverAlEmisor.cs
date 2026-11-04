using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarPermiteDevolverAlEmisor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: true (no false, lo que EF genera por defecto) — así los pasos que ya
            // existen (ej. "Aprobacion comercial", que hoy vuelve al emisor por tener
            // DestinosDevolucion vacío) mantienen ese mismo comportamiento tras la migración, en
            // vez de quedar sin ninguna opción de devolución.
            migrationBuilder.AddColumn<bool>(
                name: "PermiteDevolverAlEmisor",
                table: "PasosFlujo",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermiteDevolverAlEmisor",
                table: "PasosFlujo");
        }
    }
}
