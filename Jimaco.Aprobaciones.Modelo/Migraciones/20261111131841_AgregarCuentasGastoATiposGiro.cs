using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Jimaco.Aprobaciones.Modelo.Migraciones
{
    /// <inheritdoc />
    public partial class AgregarCuentasGastoATiposGiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Grupo",
                table: "TiposGiro",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 1,
                column: "Grupo",
                value: "Anticipos");

            migrationBuilder.InsertData(
                table: "TiposGiro",
                columns: new[] { "Id", "Activo", "CodigoCuenta", "EsAnticipo", "Grupo", "Nombre", "Orden" },
                values: new object[,]
                {
                    { 2, true, "510551", false, "Administración · Gastos de personal", "510551 DOTACION Y SUMINISTRO A TRABAJADORES", 100 },
                    { 3, true, "510563", false, "Administración · Gastos de personal", "510563 CAPACITACION AL PERSONAL", 101 },
                    { 4, true, "510566", false, "Administración · Gastos de personal", "510566 GASTOS DEPORTIVOS Y DE RECREACION", 102 },
                    { 5, true, "510584", false, "Administración · Gastos de personal", "510584 GASTOS MEDICOS Y DROGAS", 103 },
                    { 6, true, "511010", false, "Administración · Honorarios", "511010 REVISORÍA FISCAL", 104 },
                    { 7, true, "511095", false, "Administración · Honorarios", "511095 OTROS", 105 },
                    { 8, true, "511505", false, "Administración · Impuestos", "511505 INDUSTRIA Y COMERCIO", 106 },
                    { 9, true, "511506", false, "Administración · Impuestos", "511506 4*1000 GRAVAMEN MOVIMIENTO FINANCIERO", 107 },
                    { 10, true, "511595", false, "Administración · Impuestos", "511595 OTROS", 108 },
                    { 11, true, "512010", false, "Administración · Arrendamientos", "512010 CONSTRUCCIONES Y EDIFICACIONES", 109 },
                    { 12, true, "512040", false, "Administración · Arrendamientos", "512040 FLOTA Y EQUIPO DE TRANSPORTE", 110 },
                    { 13, true, "513010", false, "Administración · Seguros", "513010 CUMPLIMIENTO", 111 },
                    { 14, true, "513020", false, "Administración · Seguros", "513020 VIDA COLECTIVA", 112 },
                    { 15, true, "513025", false, "Administración · Seguros", "513025 INCENDIO", 113 },
                    { 16, true, "513030", false, "Administración · Seguros", "513030 TERREMOTO", 114 },
                    { 17, true, "513040", false, "Administración · Seguros", "513040 FLOTA Y EQUIPO DE TRANSPORTE", 115 },
                    { 18, true, "513505", false, "Administración · Servicios", "513505 ASEO Y VIGILANCIA", 116 },
                    { 19, true, "513520", false, "Administración · Servicios", "513520 PROCESAMIENTO ELECTRONICO DE DATOS", 117 },
                    { 20, true, "513525", false, "Administración · Servicios", "513525 ACUEDUCTO Y ALCANTARILLADO", 118 },
                    { 21, true, "513530", false, "Administración · Servicios", "513530 ENERGIA ELECTRICA", 119 },
                    { 22, false, "513535", false, "Administración · Servicios", "513535 TELEFONO", 120 },
                    { 23, true, "51353501", false, "Administración · Servicios", "51353501 TELEFONO GRAVADO", 121 },
                    { 24, true, "51353502", false, "Administración · Servicios", "51353502 TELEFONO NO GRAVADO", 122 },
                    { 25, true, "513550", false, "Administración · Servicios", "513550 TRANSPORTE, FLETES Y ACARREOS", 123 },
                    { 26, true, "513555", false, "Administración · Servicios", "513555 GAS", 124 },
                    { 27, true, "513595", false, "Administración · Servicios", "513595 OTROS", 125 },
                    { 28, true, "514010", false, "Administración · Gastos legales", "514010 REGISTRO MERCANTIL", 126 },
                    { 29, true, "514095", false, "Administración · Gastos legales", "514095 OTROS", 127 },
                    { 30, true, "514540", false, "Administración · Mantenimiento y reparaciones", "514540 FLOTA Y EQUIPO DE TRANSPORTE", 128 },
                    { 31, true, "515005", false, "Administración · Adecuación e instalación", "515005 INSTALACIONES ELECTRICAS", 129 },
                    { 32, true, "515015", false, "Administración · Adecuación e instalación", "515015 REPARACIONES LOCATIVAS", 130 },
                    { 33, true, "515095", false, "Administración · Adecuación e instalación", "515095 OTROS", 131 },
                    { 34, true, "515505", false, "Administración · Gastos de viaje", "515505 ALOJAMIENTO Y MANUTENCION", 132 },
                    { 35, true, "515515", false, "Administración · Gastos de viaje", "515515 PASAJES AEREOS", 133 },
                    { 36, true, "515520", false, "Administración · Gastos de viaje", "515520 PASAJES TERRESTRES", 134 },
                    { 37, true, "515595", false, "Administración · Gastos de viaje", "515595 OTROS", 135 },
                    { 38, true, "516005", false, "Administración · Depreciaciones", "516005 CONSTRUCCIONES Y EDIFICACIONES", 136 },
                    { 39, true, "516015", false, "Administración · Depreciaciones", "516015 EQUIPO DE OFICINA", 137 },
                    { 40, true, "516020", false, "Administración · Depreciaciones", "516020 EQUIPO DE COMPUTACION Y COMUNICACION", 138 },
                    { 41, true, "516035", false, "Administración · Depreciaciones", "516035 FLOTA Y EQUIPO DE TRANSPORTE", 139 },
                    { 42, true, "516070", false, "Administración · Depreciaciones", "516070 MEJORA EN PROPIEDADES AJENAS", 140 },
                    { 43, true, "519505", false, "Administración · Diversos", "519505 COMISIONES", 141 },
                    { 44, true, "519515", false, "Administración · Diversos", "519515 MUSICA AMBIENTAL", 142 },
                    { 45, true, "519525", false, "Administración · Diversos", "519525 ELEMENTOS DE ASEO Y CAFETERIA", 143 },
                    { 46, true, "519530", false, "Administración · Diversos", "519530 UTILES, PAPELERIA Y FOTOCOPIAS", 144 },
                    { 47, true, "519535", false, "Administración · Diversos", "519535 COMBUSTIBLES Y LUBRICANTES", 145 },
                    { 48, true, "519540", false, "Administración · Diversos", "519540 ENVASES Y EMPAQUES", 146 },
                    { 49, true, "519560", false, "Administración · Diversos", "519560 CASINO Y RESTAURANTE", 147 },
                    { 50, true, "519565", false, "Administración · Diversos", "519565 PARQUEADEROS", 148 },
                    { 51, true, "519595", false, "Administración · Diversos", "519595 OTROS", 149 },
                    { 52, true, "522040", false, "Ventas · Arrendamientos", "522040 FLOTA Y EQUIPO DE TRANSPORTE", 150 },
                    { 53, true, "523020", false, "Ventas · Seguros", "523020 VIDA COLECTIVA", 151 },
                    { 54, true, "523075", false, "Ventas · Seguros", "523075 OBLIGATORIO ACCIDENTE DE TRANSITO", 152 },
                    { 55, true, "523515", false, "Ventas · Servicios", "523515 ASISTENCIA TECNICA", 153 },
                    { 56, true, "523525", false, "Ventas · Servicios", "523525 ACUEDUCTO Y ALCANTARILLADO", 154 },
                    { 57, true, "523535", false, "Ventas · Servicios", "523535 TELEFONO", 155 },
                    { 58, true, "523550", false, "Ventas · Servicios", "523550 TRANSPORTE, FLETES Y ACARREOS", 156 },
                    { 59, true, "523560", false, "Ventas · Servicios", "523560 PROPAGANDA Y PUBLICIDAD", 157 },
                    { 60, true, "523595", false, "Ventas · Servicios", "523595 OTROS", 158 },
                    { 61, true, "524540", false, "Ventas · Mantenimiento y reparaciones", "524540 FLOTA Y EQUIPO DE TRANSPORTE - MANTENIMIENTO DE VEHICULO", 159 },
                    { 62, true, "525095", false, "Ventas · Adecuación e instalación", "525095 OTROS", 160 },
                    { 63, true, "525505", false, "Ventas · Gastos de viaje", "525505 ALOJAMIENTO Y MANUTENCION", 161 },
                    { 64, true, "525515", false, "Ventas · Gastos de viaje", "525515 PASAJES AEREOS", 162 },
                    { 65, true, "525520", false, "Ventas · Gastos de viaje", "525520 PASAJES TERRESTRES", 163 },
                    { 66, true, "525595", false, "Ventas · Gastos de viaje", "525595 OTROS", 164 },
                    { 67, true, "529525", false, "Ventas · Diversos", "529525 ELEMENTOS DE ASEO Y CAFETERIA", 165 },
                    { 68, true, "529530", false, "Ventas · Diversos", "529530 UTILES, PAPELERIA Y FOTOCOPIAS", 166 },
                    { 69, true, "529535", false, "Ventas · Diversos", "529535 COMBUSTIBLES Y LUBRICANTES", 167 },
                    { 70, true, "529540", false, "Ventas · Diversos", "529540 ENVASES Y EMPAQUES", 168 },
                    { 71, true, "529550", false, "Ventas · Diversos", "529550 ESTAMPILLAS", 169 },
                    { 72, true, "529560", false, "Ventas · Diversos", "529560 CASINO Y RESTAURANTE", 170 },
                    { 73, true, "529565", false, "Ventas · Diversos", "529565 PARQUEADEROS", 171 },
                    { 74, true, "529566", false, "Ventas · Diversos", "529566 PEAJES", 172 },
                    { 75, true, "529595", false, "Ventas · Diversos", "529595 OTROS", 173 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 28);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 29);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 30);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 31);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 32);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 33);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 34);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 38);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 41);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 42);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 43);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 45);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 46);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 47);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 48);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 49);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 51);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 52);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 56);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 57);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 58);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 59);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 60);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 61);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 62);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 63);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 64);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 65);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 66);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 67);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 68);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 69);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 70);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 71);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 72);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 73);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 74);

            migrationBuilder.DeleteData(
                table: "TiposGiro",
                keyColumn: "Id",
                keyValue: 75);

            migrationBuilder.DropColumn(
                name: "Grupo",
                table: "TiposGiro");
        }
    }
}
