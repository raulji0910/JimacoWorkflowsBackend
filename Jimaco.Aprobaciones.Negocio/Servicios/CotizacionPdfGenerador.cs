using System.Globalization;
using System.Reflection;
using Jimaco.Aprobaciones.Negocio.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// PDF de una cotización del portal calcado de la impresión "Imprimir Cotización" de World Office
/// (2026-11-26, plantilla tomada de la PRUEB 5): carta, franjas verdes, logos SGS y JIMACO, tabla de
/// ítems con "Tiem. Entrega" (= nota del renglón), totales y los textos del pie tal cual.
/// </summary>
public static class CotizacionPdfGenerador
{
    private const string Verde = "#009501";
    private static readonly CultureInfo CulturaEsCo = CultureInfo.GetCultureInfo("es-CO");
    // Las dos imágenes son las mismas que trae la impresión de WO (sacadas del PDF de la PRUEB 5).
    private static readonly byte[] LogoJimaco = Cargar("logo-jimaco-cotizacion.jpg");
    private static readonly byte[] LogoSgs = Cargar("logo-sgs.jpg");

    public const string TextoSolicitud =
        "Atendiendo su amable solicitud estamos enviando cotización de los productos requeridos, Para nosotros es un placer poner nuestra compañía a su servicio.";
    public const string TextoValidez =
        "Precios NO incluido IVA, validez de la oferta 5 días, precios pueden variar al término de la fecha de vencimiento de esta cotización. Cualquier inquietud o inconformidad no dude en comunicarse con nosotros.";
    public const string TextoTributario =
        "Somos Grandes Contribuyentes de ICA Según Resolucion DDI 029334 de 31 de octubre de 2025 - No Somos Auto Retenedores de Renta - Actividad Económica 4663 - ICA 6,9 X 1000 IVA Régimen Común No somos Agentes de Retención de IVA";

    private static byte[] Cargar(string archivo)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Jimaco.Aprobaciones.Negocio.Recursos.{archivo}")
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido {archivo}.");
        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }

    public static byte[] Generar(CotizacionImpresionDto c) => Document.Create(contenedor =>
    {
        contenedor.Page(pagina =>
        {
            pagina.Size(PageSizes.Letter);
            pagina.MarginHorizontal(23);
            pagina.MarginVertical(28);
            pagina.DefaultTextStyle(x => x.FontSize(7.5f).FontFamily("DejaVu Sans"));

            pagina.Header().Element(e => Encabezado(e, c));
            pagina.Content().PaddingTop(4).Element(e => Renglones(e, c));
            pagina.Footer().Column(col =>
            {
                col.Item().Background(Verde).PaddingVertical(3).PaddingHorizontal(6).AlignCenter()
                    .Text(TextoValidez).FontColor(Colors.White).Bold().FontSize(7.5f);
                col.Item().PaddingTop(2).AlignCenter().Text(TextoTributario).Bold().FontSize(6.8f);
                col.Item().PaddingTop(2).AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(6).FontColor(Colors.Grey.Darken1));
                    t.Span("Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            });
        });
    }).GeneratePdf();

    private static void Encabezado(IContainer contenedor, CotizacionImpresionDto c)
    {
        contenedor.Column(col =>
        {
            col.Item().Row(fila =>
            {
                // Bloque de datos (izquierda), como en WO.
                fila.ConstantItem(275).Column(datos =>
                {
                    datos.Item().Row(r =>
                    {
                        r.ConstantItem(120).Background(Verde).Border(0.5f).PaddingHorizontal(3).PaddingVertical(2)
                            .Text("COTIZACION No").FontColor(Colors.White).Bold().FontSize(11);
                        r.ConstantItem(65).Border(0.5f).PaddingVertical(2).AlignCenter().Text(c.Prefijo).Bold().FontSize(11);
                        r.RelativeItem().Border(0.5f).PaddingVertical(2).AlignCenter().Text(c.Numero).Bold().FontSize(11);
                    });
                    datos.Item().PaddingTop(4).Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.ConstantColumn(40); cd.RelativeColumn(); });
                        t.Cell().Background(Verde).PaddingHorizontal(2).Text("CLIENTE").FontColor(Colors.White).Bold();
                        t.Cell().Border(0.5f).PaddingHorizontal(3).Text(c.Cliente);
                        t.Cell().Background(Verde).PaddingHorizontal(2).Text("NIT").FontColor(Colors.White).Bold();
                        t.Cell().Border(0.5f).PaddingHorizontal(3).Text(c.Nit);
                    });
                    datos.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(151); cd.RelativeColumn(124); });
                        t.Cell().Element(Etiqueta).Text("FECHA DOCUMENTO");
                        t.Cell().Element(Etiqueta).Text("CONTACTO");
                        t.Cell().Element(Valor).Text(c.FechaDocumento);
                        t.Cell().Element(Valor).Text(c.Contacto ?? "");
                    });
                    datos.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(189); cd.RelativeColumn(86); });
                        t.Cell().Element(Etiqueta).Text("VENDEDOR");
                        t.Cell().Element(Etiqueta).Text("FORMA DE PAGO");
                        t.Cell().Element(Valor).Text(c.Vendedor ?? "");
                        t.Cell().Element(Valor).Text(c.FormaPago ?? "");
                    });
                });
                fila.ConstantItem(125).Height(100).AlignCenter().Image(LogoSgs).FitArea();
                fila.RelativeItem().PaddingTop(18).Height(66).AlignCenter().Image(LogoJimaco).FitArea();
            });

            // "2 OBSERVACIONES" de Más Datos (p. ej. el lugar de entrega).
            col.Item().PaddingTop(2).Border(0.5f).MinHeight(16).PaddingHorizontal(3).PaddingVertical(2)
                .Text(c.Observaciones ?? "").Italic().FontSize(8.5f);
        });
    }

    private static readonly (string Titulo, float Ancho)[] Columnas =
    [
        ("Item", 20), ("Descripción", 244), ("Tiem. Entrega", 62), ("Cantidad", 44), ("Und", 28), ("Valor Unt", 66), ("IVA", 24), ("Total", 70)
    ];

    private static void Renglones(IContainer contenedor, CotizacionImpresionDto c)
    {
        contenedor.Column(col =>
        {
            // Como en WO, las líneas verticales de las columnas bajan hasta el final del recuadro: se dibujan
            // como capa de fondo de toda la altura. Solo en cotizaciones de una página (con muchas
            // renglones la tabla pasa de página y cada renglón ya trae sus líneas).
            var unaPagina = c.Renglones.Count <= 25;
            col.Item().Border(0.5f).Layers(capas =>
            {
                if (unaPagina)
                    capas.Layer().Row(fondo =>
                    {
                        foreach (var (_, ancho) in Columnas) fondo.RelativeItem(ancho).BorderRight(0.5f);
                    });
                capas.PrimaryLayer().MinHeight(unaPagina ? 470 : 0).Element(e => Tabla(e, c));
            });


            col.Item().PaddingTop(3).Row(fila =>
            {
                fila.RelativeItem().PaddingRight(12).AlignCenter()
                    .Text(TextoSolicitud).Bold().Underline().FontSize(10).AlignCenter();
                fila.ConstantItem(212).Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(); cd.ConstantColumn(105); });
                    Total(t, "SUBTOTAL", c.Subtotal);
                    Total(t, "DESCUENTO", c.Descuento);
                    Total(t, "IVA", c.Iva);
                    Total(t, "TOTAL", c.Total);
                });
            });
        });
    }

    private static void Tabla(IContainer contenedor, CotizacionImpresionDto c)
    {
        contenedor.Table(t =>
        {
            t.ColumnsDefinition(cd => { foreach (var (_, ancho) in Columnas) cd.RelativeColumn(ancho); });
            t.Header(h =>
            {
                foreach (var (titulo, _) in Columnas)
                    h.Cell().Background(Verde).BorderBottom(0.5f).BorderRight(0.5f).BorderColor(Colors.Black).PaddingVertical(2)
                        .AlignCenter().Text(titulo).FontColor(Colors.White).Bold();
            });
            foreach (var r in c.Renglones)
            {
                t.Cell().Element(Celda).AlignCenter().Text(r.Item.ToString(CulturaEsCo));
                t.Cell().Element(Celda).Text(r.Descripcion);
                t.Cell().Element(Celda).Text(r.TiempoEntrega ?? "");
                t.Cell().Element(Celda).AlignCenter().Text(Numero(r.Cantidad));
                t.Cell().Element(Celda).Text(r.Unidad);
                t.Cell().Element(Celda).AlignRight().Text(Pesos(r.ValorUnitario));
                t.Cell().Element(Celda).Text(Porcentaje(r.Iva));
                t.Cell().Element(Celda).AlignRight().Text(Pesos(r.Total));
            }
        });
    }

    private static void Total(TableDescriptor t, string etiqueta, decimal valor)
    {
        t.Cell().Background(Verde).BorderBottom(0.5f).BorderColor(Colors.White).PaddingRight(3).AlignRight().Text(etiqueta).FontColor(Colors.White).Bold();
        t.Cell().BorderBottom(0.5f).PaddingRight(3).AlignRight().Text(Pesos(valor)).Bold();
    }

    private static IContainer Etiqueta(IContainer c) => c.Background(Verde).Border(0.5f).AlignCenter().PaddingVertical(1).DefaultTextStyle(x => x.FontColor(Colors.White).Bold());
    private static IContainer Valor(IContainer c) => c.Border(0.5f).AlignCenter().PaddingVertical(1);
    private static IContainer Celda(IContainer c) => c.BorderRight(0.5f).PaddingHorizontal(2).PaddingVertical(1.5f);

    private static string Pesos(decimal valor) => "$ " + valor.ToString("#,##0", CulturaEsCo);
    private static string Numero(decimal valor) => valor.ToString("#,##0.##", CulturaEsCo);
    private static string Porcentaje(decimal fraccion) => (fraccion * 100).ToString("0.##", CulturaEsCo) + "%";

    /// <summary>
    /// Dígito de verificación de un NIT (algoritmo DIAN, pesos 3,7,13,17…). WO imprime "901115366 3":
    /// solo se agrega a NIT de empresa (9 dígitos que empiezan por 8 o 9), no a cédulas.
    /// </summary>
    public static string NitConDigito(string? identificacion)
    {
        var nit = (identificacion ?? "").Trim();
        if (nit.Length != 9 || !nit.All(char.IsDigit) || nit[0] is not ('8' or '9')) return nit;
        int[] pesos = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];
        var suma = 0;
        for (var i = 0; i < nit.Length; i++)
            suma += (nit[nit.Length - 1 - i] - '0') * pesos[i];
        var residuo = suma % 11;
        return $"{nit} {(residuo > 1 ? 11 - residuo : residuo)}";
    }
}
