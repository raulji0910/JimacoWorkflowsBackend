using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarNombresCompletosWOUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NombresCompletosWO",
                table: "Usuarios",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Nombres tal cual aparecen en "Elaborado por" de las Cotizaciones reales de producción
            // (2026-11-04). Laura y Yazmín comparten sus avisos a pedido del usuario. En una BD donde
            // esos correos no existan (QA/local) el UPDATE simplemente no toca nada.
            migrationBuilder.Sql("""
                UPDATE Usuarios SET NombresCompletosWO = 'LAURA SOFIA HERNANDEZ CARO; YAZMIN RENDON RODRIGUEZ' WHERE Email = 'ventas@jimaco.com.co';
                UPDATE Usuarios SET NombresCompletosWO = 'YAZMIN RENDON RODRIGUEZ; LAURA SOFIA HERNANDEZ CARO' WHERE Email = 'compras@jimaco.com.co';
                UPDATE Usuarios SET NombresCompletosWO = 'LADY JOHANNA CARDONA RODRIGUEZ' WHERE Email = 'ventas.lady@jimaco.com.co';
                UPDATE Usuarios SET NombresCompletosWO = 'MARIA DEL PILAR JIMENEZ ROJAS' WHERE Email = 'ventas.contratos@jimaco.com.co';
                UPDATE Usuarios SET NombresCompletosWO = 'LEIDY CATALINA JIMENEZ CARO' WHERE Email = 'gerencia.comercial@jimaco.com.co';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NombresCompletosWO",
                table: "Usuarios");
        }
    }
}
