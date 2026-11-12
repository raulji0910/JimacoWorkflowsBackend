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
/// PDF de entrega de una o varias Remisiones (una por página) — mismo estilo visual que el PDF de
/// la OC (logo, franjas verdes), SIN precios (es el soporte que lleva el conductor) y con:
/// - un QR con el código de la remisión (<see cref="RemisionService.CodigoQR"/>): al subir la foto
///   del soporte firmado, el navegador lo lee y confirma que es esta remisión;
/// - la zona de "Recibido por" para la firma del cliente.
/// </summary>
public class RemisionPdfGenerador : IRemisionPdfGenerador
{
    private static readonly CultureInfo CulturaEsCo = CultureInfo.GetCultureInfo("es-CO");
    private static readonly byte[] Logo = CargarLogo();
    private static readonly string VerdeEtiqueta = Colors.Green.Medium;

    private const string PiePagina = "Pbx: 601 378 29 33  --  Calle 23 # 25 - 06   -   www.jimaco.com.co";

    private static byte[] CargarLogo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Jimaco.Aprobaciones.Negocio.Recursos.logo-jimaco.png")
            ?? throw new InvalidOperationException("No se encontró el logo embebido.");
        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }

    internal static byte[] GenerarQR(string texto)
    {
        using var generador = new QRCodeGenerator();
        // Nivel Q (25 % de corrección): aguanta arrugas, sombras y algo de la firma encima.
        using var datos = generador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(datos).GetGraphic(10);
    }

    public byte[] Generar(IReadOnlyList<RemisionDetalleDto> remisiones)
    {
        var documento = Document.Create(contenedor =>
        {
            foreach (var r in remisiones)
                contenedor.Page(pagina => Pagina(pagina, r));
        });
        return documento.GeneratePdf();
    }

    private static void Pagina(PageDescriptor pagina, RemisionDetalleDto r)
    {
        var qr = GenerarQR(r.CodigoQR);

        pagina.Size(PageSizes.Letter);
        pagina.Margin(24);
        pagina.DefaultTextStyle(x => x.FontSize(9).FontFamily("DejaVu Sans"));

        pagina.Content().Column(col =>
        {
            col.Spacing(6);

            // ---- Encabezado: logo · título y número · QR ----
            col.Item().Row(fila =>
            {
                fila.ConstantItem(190).Height(75).Image(Logo).FitArea();
                fila.RelativeItem().PaddingHorizontal(8).AlignMiddle().Column(c =>
                {
                    c.Item().AlignCenter().Text("REMISIÓN").FontSize(18).Bold().FontColor(VerdeEtiqueta);
                    c.Item().AlignCenter().Text($"{r.Prefijo} {r.Numero}").FontSize(16).Bold();
                    c.Item().AlignCenter().Text(r.Fecha.ToString("d 'de' MMMM 'de' yyyy", CulturaEsCo)).FontSize(9);
                });
                fila.ConstantItem(95).Column(c =>
                {
                    c.Item().Width(95).Height(95).Image(qr).FitArea();
                    c.Item().AlignCenter().Text(r.CodigoQR).FontSize(6.5f);
                });
            });

            // ---- Cliente ----
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.1f);
                    c.RelativeColumn(2.4f);
                    c.RelativeColumn(0.8f);
                    c.RelativeColumn(1.4f);
                });
                Par(t, "CLIENTE", r.Cliente);
                Par(t, "NIT", r.NitCliente);
                Par(t, "DIRECCIÓN", r.Direccion);
                Par(t, "CIUDAD", r.Ciudad);
                Par(t, "TELÉFONO", r.Telefono);
                Par(t, "VENDEDOR", r.Vendedor);
            });

            // ---- Entrega ----
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.1f);
                    c.RelativeColumn(2.4f);
                    c.RelativeColumn(0.8f);
                    c.RelativeColumn(1.4f);
                });
                Par(t, "DIRECCIÓN ENTREGA", r.DireccionEntrega);
                Par(t, "COTIZACIÓN", r.Cotizacion);
                Par(t, "CAMPO ENTREGA", r.CampoEntrega);
                Par(t, "OC CLIENTE", r.OrdenCompraCliente);
            });

            if (!string.IsNullOrWhiteSpace(r.Nota) && !string.Equals(r.Nota.Trim(), "REMISIÓN", StringComparison.OrdinalIgnoreCase))
            {
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => c.RelativeColumn());
                    t.Cell().Element(EtiquetaVerde).Text("OBSERVACIONES");
                    t.Cell().Element(ValorConBorde).Text(r.Nota);
                });
            }

            // ---- Renglones (sin precios) ----
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(24);
                    c.ConstantColumn(80);
                    c.RelativeColumn();
                    c.ConstantColumn(50);
                    c.ConstantColumn(60);
                    c.ConstantColumn(70);
                });
                t.Header(h =>
                {
                    Encabezado(h.Cell(), "#");
                    Encabezado(h.Cell(), "Código");
                    Encabezado(h.Cell(), "Descripción");
                    Encabezado(h.Cell(), "Unidad");
                    Encabezado(h.Cell(), "Cantidad");
                    Encabezado(h.Cell(), "Recibido ✓");
                });
                foreach (var x in r.Renglones)
                {
                    Dato(t.Cell(), x.Orden.ToString(CulturaEsCo), centrado: true);
                    Dato(t.Cell(), x.Codigo ?? "");
                    Dato(t.Cell(), x.Descripcion);
                    Dato(t.Cell(), x.UnidadMedida ?? "", centrado: true);
                    Dato(t.Cell(), x.Cantidad.ToString("0.##", CulturaEsCo), centrado: true);
                    Dato(t.Cell(), "", centrado: true);
                }
            });

            // ---- Recibido por (firma del cliente) ----
            col.Item().PaddingTop(10).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn(1.2f);
                    c.RelativeColumn(1);
                });
                t.Cell().ColumnSpan(3).Element(EtiquetaVerde).Text("RECIBIDO A SATISFACCIÓN POR EL CLIENTE");
                Firma(t.Cell(), "Nombre");
                Firma(t.Cell(), "Cédula");
                Firma(t.Cell(), "Fecha");
                t.Cell().ColumnSpan(3).Element(c => ValorConBorde(c).Height(60)).AlignBottom()
                    .Text("Firma y sello").FontSize(7).FontColor(Colors.Grey.Darken1);
            });

            col.Item().AlignCenter().Text("Al confirmar la entrega, tome la foto de esta hoja completa, con el código QR visible.")
                .FontSize(7.5f).Italic().FontColor(Colors.Grey.Darken1);
        });

        pagina.Footer().AlignCenter().Text(PiePagina).FontSize(7).FontColor(Colors.Grey.Darken1);
    }

    private static void Par(TableDescriptor t, string etiqueta, string? valor)
    {
        t.Cell().Element(EtiquetaVerde).Text(etiqueta);
        t.Cell().Element(ValorConBorde).Text(string.IsNullOrWhiteSpace(valor) ? "—" : valor);
    }

    private static void Encabezado(IContainer c, string texto) =>
        c.Border(0.75f).Background(Colors.Green.Lighten3).Padding(3).AlignCenter().Text(texto).Bold().FontSize(8);

    private static void Dato(IContainer c, string texto, bool centrado = false)
    {
        var celda = c.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(3).MinHeight(16);
        (centrado ? celda.AlignCenter() : celda).Text(texto).FontSize(8.5f);
    }

    private static void Firma(IContainer c, string etiqueta) =>
        ValorConBorde(c).Height(34).AlignBottom().Text(etiqueta).FontSize(7).FontColor(Colors.Grey.Darken1);

    private static IContainer EtiquetaVerde(IContainer c) =>
        c.Border(0.75f).Background(VerdeEtiqueta).Padding(4).DefaultTextStyle(x => x.FontColor(Colors.White).FontSize(8).Bold());

    private static IContainer ValorConBorde(IContainer c) =>
        c.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(4);
}
