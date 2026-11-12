using System.Globalization;
using System.Reflection;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// PDF de entrega de una o varias Remisiones (una por página), calcado del formato real de la
/// Remisión de World Office (REMISION_EJEMPLO.pdf, REM 29421): franja "REMISION No.", cliente/NIT/
/// fecha, dirección de entrega, campo de entrega y OC, elaborado por y forma de pago, sello SGS y
/// logo, tabla Item/Código/Cantidad/Medida/Descripción, "Despachado/Recibido por" y el pie legal.
/// Lo único que agrega es el QR (<see cref="RemisionService.CodigoQR"/>): al subir la foto de la
/// remisión firmada, el navegador lo lee y confirma que es esta remisión.
/// </summary>
public class RemisionPdfGenerador : IRemisionPdfGenerador
{
    private static readonly CultureInfo CulturaEsCo = CultureInfo.GetCultureInfo("es-CO");
    private static readonly byte[] Logo = Cargar("logo-jimaco.png");
    private static readonly byte[] SelloSgs = Cargar("sello-sgs.png");

    // Verde de los encabezados de World Office (verde puro, más saturado que el de la OC).
    private const string Verde = "#00A000";

    private const string AvisoCambios =
        "Informacion importante: NO SE ACEPTAN CAMBIOS NI DEVOLUCIONES DESPUES DE 8 DIAS DE ENTREGADO EL PEDIDO. " +
        "MATERIAL CORTADO O FABRICADO NO SE ACEPTA NINGUN CAMBIO O DEVOLUCION.";
    private const string Contacto = "Pbx: 601378 29 33 - CALLE 23 # 25 - 06 - cartera@jimaco.com.co - www.jimaco.com.co";
    private const string Tributario =
        "Somos Grandes Contribuyentes de ICA Según Resolución DDI 029334 de 31 de octubre de 2025 - No Somos Auto " +
        "Retenedores de Renta - Actividad Económica 4663 - ICA 6,9 X 1000 IVA Régimen Común No somos Agentes de Retención de IVA";

    private static byte[] Cargar(string archivo)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Jimaco.Aprobaciones.Negocio.Recursos.{archivo}")
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido {archivo}.");
        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }

    internal static byte[] GenerarQR(string texto)
    {
        using var generador = new QRCodeGenerator();
        // Nivel Q (25 % de corrección): aguanta arrugas, sombras y algo de firma encima.
        using var datos = generador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(datos).GetGraphic(10);
    }

    public byte[] Generar(IReadOnlyList<RemisionDetalleDto> remisiones) =>
        Document.Create(contenedor =>
        {
            foreach (var r in remisiones)
                contenedor.Page(pagina => Pagina(pagina, r));
        }).GeneratePdf();

    private static void Pagina(PageDescriptor pagina, RemisionDetalleDto r)
    {
        var qr = GenerarQR(r.CodigoQR);

        pagina.Size(PageSizes.Letter);
        pagina.MarginHorizontal(26);
        pagina.MarginVertical(30);
        pagina.DefaultTextStyle(x => x.FontSize(9).FontFamily("DejaVu Sans"));

        pagina.Header().Row(fila =>
        {
            // ---- Bloque izquierdo: los datos, igual que World Office ----
            fila.RelativeItem(5.3f).Column(c =>
            {
                c.Item().Row(t =>
                {
                    t.RelativeItem(3).Background(Verde).PaddingVertical(2).AlignCenter()
                        .Text("REMISION No.").FontSize(14).Bold().FontColor(Colors.White);
                    t.RelativeItem(2).AlignCenter().AlignMiddle().Text(r.Numero).FontSize(14).Bold();
                });

                c.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(52);
                        cd.RelativeColumn(1.1f);
                        cd.RelativeColumn(1.6f);
                    });
                    t.Cell().Element(Etiqueta).Text("CLIENTE");
                    t.Cell().ColumnSpan(2).Element(Valor).Text(r.Cliente ?? "");
                    t.Cell().Element(Etiqueta).Text("NIT");
                    t.Cell().Element(Valor).Text(r.NitCliente ?? "");
                    t.Cell().Element(Valor).AlignCenter().Text(r.Fecha.ToString("dddd, d 'de' MMMM 'de' yyyy", CulturaEsCo));
                });

                c.Item().Element(EtiquetaCentrada).Text("DIRECCION DE ENTREGA");
                c.Item().Element(ValorCentrado).Text(r.DireccionEntrega ?? "");

                c.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(2.6f);
                        cd.RelativeColumn(1.6f);
                    });
                    t.Cell().Element(EtiquetaCentrada).Text("CAMPO DE ENTREGA Y/O QUIEN RECIBE");
                    t.Cell().Element(EtiquetaCentrada).Text("ORDEN DE COMPRA");
                    t.Cell().Element(ValorCentrado).Text(r.CampoEntrega ?? "");
                    t.Cell().Element(ValorCentrado).Text(r.OrdenCompraCliente ?? "");
                    t.Cell().Element(EtiquetaCentrada).Text("ELABORADO POR");
                    t.Cell().Element(EtiquetaCentrada).Text("FORMA DE PAGO");
                    t.Cell().PaddingHorizontal(2).Text(r.Vendedor ?? r.ElaboradoPorWO ?? "").FontSize(8.5f);
                    t.Cell().AlignCenter().Text(r.FormaDePago ?? "").FontSize(8.5f);
                });
            });

            // ---- Bloque derecho: sello SGS, logo y el QR de esta remisión ----
            fila.RelativeItem(4.7f).PaddingLeft(6).Row(d =>
            {
                d.RelativeItem().AlignMiddle().Column(s =>
                {
                    s.Item().AlignCenter().MaxWidth(78).Image(SelloSgs).FitWidth();
                });
                d.RelativeItem(1.7f).AlignMiddle().Column(l =>
                {
                    l.Item().AlignCenter().MaxWidth(150).Image(Logo).FitWidth();
                });
                d.ConstantItem(86).AlignMiddle().Column(q =>
                {
                    q.Item().AlignCenter().Width(80).Height(80).Image(qr).FitArea();
                    q.Item().AlignCenter().Text(r.CodigoQR).FontSize(6);
                });
            });
        });

        // Como en World Office: el recuadro de ítems llega hasta el pie, con las líneas de las
        // columnas de arriba abajo (capa de fondo extendida) aunque haya un solo ítem.
        pagina.Content().PaddingTop(6).Layers(capas =>
        {
            capas.Layer().Extend().Border(1).Row(lineas =>
            {
                foreach (var ancho in AnchosColumnas)
                    lineas.ConstantItem(ancho).BorderRight(1);
                lineas.RelativeItem();
            });

            capas.PrimaryLayer().Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    foreach (var ancho in AnchosColumnas) cd.ConstantColumn(ancho);
                    cd.RelativeColumn();
                });
                t.Header(h =>
                {
                    foreach (var titulo in new[] { "Item", "Código", "Cantidad", "Medida", "Descripción" })
                        h.Cell().Border(1).Background(Verde).PaddingVertical(2).AlignCenter()
                            .Text(titulo).Bold().FontColor(Colors.White).FontSize(9);
                });
                foreach (var x in r.Renglones)
                {
                    Renglon(t.Cell(), x.Orden.ToString(CulturaEsCo), true);
                    Renglon(t.Cell(), x.Codigo ?? "", true);
                    Renglon(t.Cell(), x.Cantidad.ToString("0.##", CulturaEsCo), true);
                    Renglon(t.Cell(), x.UnidadMedida ?? "", true);
                    Renglon(t.Cell(), x.Descripcion, false);
                }
            });
        });

        pagina.Footer().Column(pie =>
        {
            pie.Item().PaddingBottom(2).Row(f =>
            {
                f.RelativeItem().Text(t =>
                {
                    t.Span("Despachado Por: ").Bold();
                    t.Span("______________________________________").Bold();
                });
                f.RelativeItem().Text(t =>
                {
                    t.Span("Recibido Por: ").Bold();
                    t.Span("______________________________________").Bold();
                });
            });
            pie.Item().Text("Nombre, cédula y firma de quien recibe. Al confirmar la entrega, tome la foto de la hoja completa con el código QR visible.")
                .FontSize(6.5f).Italic().FontColor(Colors.Grey.Darken1);
            pie.Item().PaddingTop(3).Background(Verde).PaddingHorizontal(4).PaddingVertical(2).Column(v =>
            {
                v.Item().AlignCenter().Text(AvisoCambios).FontSize(8.5f).Bold().Italic().Underline().FontColor(Colors.White);
                v.Item().AlignCenter().Text(Contacto).FontSize(8.5f).Bold().FontColor(Colors.White);
            });
            pie.Item().PaddingTop(2).AlignCenter().Text(Tributario).FontSize(8).Bold().Underline();
        });
    }

    // Item, Código, Cantidad, Medida (Descripción ocupa el resto).
    private static readonly float[] AnchosColumnas = [44, 98, 54, 44];

    private static void Renglon(IContainer c, string texto, bool centrado)
    {
        var celda = c.PaddingHorizontal(3).PaddingVertical(2);
        (centrado ? celda.AlignCenter() : celda).Text(texto).FontSize(9);
    }

    private static IContainer Etiqueta(IContainer c) =>
        c.Background(Verde).PaddingHorizontal(2).PaddingVertical(1)
            .DefaultTextStyle(x => x.FontColor(Colors.White).FontSize(8).Bold());

    private static IContainer EtiquetaCentrada(IContainer c) =>
        c.Background(Verde).BorderBottom(1).BorderColor(Colors.White).PaddingVertical(1).AlignCenter()
            .DefaultTextStyle(x => x.FontColor(Colors.White).FontSize(8).Bold());

    private static IContainer Valor(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingHorizontal(3).PaddingVertical(1)
            .DefaultTextStyle(x => x.FontSize(8.5f));

    private static IContainer ValorCentrado(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(1).AlignCenter()
            .DefaultTextStyle(x => x.FontSize(8.5f));
}
