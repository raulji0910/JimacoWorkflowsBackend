using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Comprobante de un giro de caja menor (2026-11-27): reemplaza el vale de papel. Media carta, para
/// imprimir y que la persona firme al recibir el efectivo. Muestra lo que se sabe hasta el momento
/// (legalización y vueltas si ya las hubo).
/// </summary>
public static class GiroCajaPdfGenerador
{
    private const string Gris = "#555555";

    public static byte[] Generar(GiroCajaDto g) => Document.Create(contenedor =>
    {
        contenedor.Page(pagina =>
        {
            // Media carta horizontal (21,59 × 13,97 cm): cabe en la mitad de una hoja.
            pagina.Size(new PageSize(612, 396));
            pagina.Margin(28);
            pagina.DefaultTextStyle(t => t.FontSize(10));

            pagina.Content().Column(col =>
            {
                col.Spacing(6);
                col.Item().Row(fila =>
                {
                    fila.RelativeItem().Column(c =>
                    {
                        c.Item().Text(g.Empresa == Empresa.Ferrealiados ? "FERREALIADOS JV" : "FERRETERIA INDUSTRIAL JIMACO").Bold().FontSize(12);
                        c.Item().Text("Caja menor — comprobante de giro").FontColor(Gris);
                    });
                    fila.ConstantItem(170).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text($"GIRO No. {g.Numero}").Bold().FontSize(14);
                        c.Item().AlignRight().Text(Fecha(g.Fecha)).FontColor(Gris);
                        if (g.Estado == EstadoGiroCaja.Anulado)
                            c.Item().AlignRight().Text("ANULADO").Bold().FontColor(Colors.Red.Darken2);
                    });
                });

                col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(110); c.RelativeColumn(); c.ConstantColumn(90); c.RelativeColumn(); });
                    Celda(t, "Entregado a"); Valor(t, g.Beneficiario.Nombre);
                    Celda(t, "Cédula"); Valor(t, g.Beneficiario.Cedula);
                    Celda(t, "Concepto"); Valor(t, g.Concepto.Nombre);
                    Celda(t, "Girado por"); Valor(t, g.GiradoPor);
                    Celda(t, "Valor entregado"); t.Cell().PaddingVertical(2).Text(Pesos(g.ValorEntregado)).Bold().FontSize(12);
                    Celda(t, "Estado"); Valor(t, g.Estado switch
                    {
                        EstadoGiroCaja.Abierto => "Por legalizar",
                        EstadoGiroCaja.VueltaPendiente => "Vuelta pendiente",
                        EstadoGiroCaja.Cerrado => "Cerrado",
                        _ => "Anulado"
                    });
                    if (g.OrdenesCompra.Count > 0)
                    {
                        Celda(t, "Paga las OC");
                        t.Cell().ColumnSpan(3).PaddingVertical(2).Text(string.Join(", ",
                            g.OrdenesCompra.Select(o => $"OC {o.NumeroReferencia} {o.Proveedor} ({Pesos(o.Valor)})")));
                    }
                    if (!string.IsNullOrWhiteSpace(g.Nota))
                    {
                        Celda(t, "Nota");
                        t.Cell().ColumnSpan(3).PaddingVertical(2).Text(g.Nota);
                    }
                    if (g.ValorLegalizado is { } gastado)
                    {
                        Celda(t, "Gastado");
                        Valor(t, Pesos(gastado));
                        Celda(t, g.VueltaPendiente > 0 ? "Vuelta pendiente" : "Vueltas recibidas");
                        Valor(t, Pesos(g.VueltaPendiente > 0 ? g.VueltaPendiente : g.ValorVueltasRecibidas));
                    }
                });

                col.Item().PaddingTop(36).Row(fila =>
                {
                    Firma(fila.RelativeItem(), "Recibí conforme", $"{g.Beneficiario.Nombre} — C.C. {g.Beneficiario.Cedula}");
                    fila.ConstantItem(40);
                    Firma(fila.RelativeItem(), "Entregó", g.GiradoPor);
                });
            });

            pagina.Footer().AlignCenter().Text(t =>
            {
                t.Span("Jimaco Aprobaciones — este giro debe legalizarse (gasto y vueltas) en el portal.").FontSize(7).FontColor(Gris);
            });
        });
    }).GeneratePdf();

    private static void Celda(TableDescriptor t, string texto) => t.Cell().PaddingVertical(2).Text(texto).FontColor(Gris);

    private static void Valor(TableDescriptor t, string texto) => t.Cell().PaddingVertical(2).Text(texto);

    private static void Firma(IContainer contenedor, string titulo, string nombre) => contenedor.Column(c =>
    {
        c.Item().LineHorizontal(0.8f);
        c.Item().Text(titulo).Bold().FontSize(9);
        c.Item().Text(nombre).FontSize(8).FontColor(Gris);
    });

    private static string Pesos(decimal valor) => "$ " + FormatoPesos.Miles(valor);

    // Hora de Colombia (UTC-5, sin horario de verano).
    private static string Fecha(DateTime utc) => utc.AddHours(-5).ToString("dd/MM/yyyy hh:mm tt");
}
