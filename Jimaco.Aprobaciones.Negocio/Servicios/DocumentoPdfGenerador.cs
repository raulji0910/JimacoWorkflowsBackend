using System.Globalization;
using System.Reflection;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Genera un PDF calcado del formato real de la Orden de Compra de World Office (logo, cajas con
/// borde, franjas verdes de etiqueta, pie de página) — pero genérico para cualquier
/// <see cref="TipoDocumento"/> y con un agregado real: "Aprobado Por" sale relleno con el nombre
/// de quien de verdad aprobó (según nuestro historial), no una línea en blanco para firmar a mano
/// como en el original.
/// </summary>
public class DocumentoPdfGenerador : IDocumentoPdfGenerador
{
    private static readonly CultureInfo CulturaEsCo = CultureInfo.GetCultureInfo("es-CO");
    private static readonly byte[] Logo = CargarLogo();
    private static readonly string VerdeEtiqueta = Colors.Green.Medium;

    private const string PiePagina = "Pbx: 601 378 29 33  --  Calle 23 # 25 - 06   compras@jimaco.com.co   -   www.jimaco.com.co";

    // Claves conocidas de Datos que este formato ubica en su lugar "de siempre" (mismo layout que
    // World Office); cualquier otra clave que traiga el documento se muestra igual, más abajo, en
    // una sección genérica — así un TipoDocumento con campos distintos no pierde información.
    private const string ClaveDocExterno = "cotizacionOrigen";
    private const string ClaveElaboradoPor = "elaboradoPor";
    private const string ClaveFormaDePago = "formaDePago";
    private const string ClaveObservaciones = "observaciones";
    private const string ClaveNit = "nit";
    private const string ClaveDireccionProveedor = "direccionProveedor";

    private static byte[] CargarLogo()
    {
        var ensamblado = Assembly.GetExecutingAssembly();
        using var stream = ensamblado.GetManifestResourceStream("Jimaco.Aprobaciones.Negocio.Recursos.logo-jimaco.png")
            ?? throw new InvalidOperationException("No se encontró el logo embebido.");
        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }

    public byte[] Generar(InstanciaDocumentoDetalleDto d)
    {
        var datos = d.Datos ?? [];
        var subtotal = d.Renglones.Sum(r => r.Total);
        var totalIva = d.Renglones.Sum(r => r.Total * r.PorcentajeIva);
        var total = subtotal + totalIva;
        var aprobadoPor = d.Historial.LastOrDefault(h => h.Accion == TipoAccion.Aprobado);

        var otrosDatos = datos
            .Where(kv => kv.Key is not (ClaveDocExterno or ClaveElaboradoPor or ClaveFormaDePago or ClaveObservaciones or ClaveNit or ClaveDireccionProveedor))
            .ToList();

        var documento = Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                pagina.Size(PageSizes.A4);
                pagina.Margin(24);
                pagina.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("DejaVu Sans"));

                pagina.Content().Column(col =>
                {
                    col.Spacing(0);

                    // ---- Encabezado: logo + caja de título/número, estilo World Office ----
                    col.Item().Row(fila =>
                    {
                        fila.ConstantItem(150).Height(60).Image(Logo).FitArea();
                        fila.RelativeItem();
                        fila.ConstantItem(180).Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn();
                            });
                            tabla.Cell().Element(EtiquetaVerde).Text(d.TipoDocumentoNombre.ToUpperInvariant() + " No.").FontSize(9).Bold();
                            tabla.Cell().Element(ValorConBorde).AlignCenter().Text(d.NumeroReferencia ?? "—").Bold();
                        });
                    });

                    // ---- Proveedor / NIT / Dirección ----
                    col.Item().Table(tabla =>
                    {
                        tabla.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn(2);
                        });

                        tabla.Cell().Element(EtiquetaVerde).Text("PROVEEDOR");
                        tabla.Cell().Element(ValorConBorde).Text(d.Proveedor ?? "—");

                        if (datos.TryGetValue(ClaveNit, out var nit))
                        {
                            tabla.Cell().Element(EtiquetaVerde).Text("NIT");
                            tabla.Cell().Element(ValorConBorde).Text(nit);
                        }

                        if (datos.TryGetValue(ClaveDireccionProveedor, out var direccion))
                        {
                            tabla.Cell().ColumnSpan(2).Element(EtiquetaVerde).AlignCenter().Text("DIRECCIÓN PROVEEDOR");
                            tabla.Cell().ColumnSpan(2).Element(ValorConBorde).AlignCenter().Text(direccion);
                        }
                    });

                    // ---- Fecha / Doc externo / Elaborado por / Forma de pago ----
                    col.Item().Table(tabla =>
                    {
                        tabla.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        tabla.Cell().Element(EtiquetaVerde).AlignCenter().Text("FECHA DOCUMENTO");
                        tabla.Cell().Element(EtiquetaVerde).AlignCenter().Text("DOC EXTERNO");
                        tabla.Cell().Element(EtiquetaVerde).AlignCenter().Text("ELABORADO POR");
                        tabla.Cell().Element(EtiquetaVerde).AlignCenter().Text("FORMA DE PAGO");

                        tabla.Cell().Element(ValorConBorde).AlignCenter()
                            .Text(d.FechaDocumento?.ToString("d 'de' MMMM 'de' yyyy", CulturaEsCo) ?? "—");
                        tabla.Cell().Element(ValorConBorde).AlignCenter().Text(datos.GetValueOrDefault(ClaveDocExterno, "—"));
                        tabla.Cell().Element(ValorConBorde).AlignCenter().Text(datos.GetValueOrDefault(ClaveElaboradoPor, d.CreadoPorNombre));
                        tabla.Cell().Element(ValorConBorde).AlignCenter().Text(datos.GetValueOrDefault(ClaveFormaDePago, "—"));
                    });

                    // ---- Observaciones ----
                    col.Item().Table(tabla =>
                    {
                        tabla.ColumnsDefinition(c => c.RelativeColumn());
                        tabla.Cell().Element(EtiquetaVerde).Text("OBSERVACIONES");
                        tabla.Cell().Element(c => ValorConBorde(c).MinHeight(20)).Text(datos.GetValueOrDefault(ClaveObservaciones, ""));
                    });

                    // ---- Otros campos propios del tipo de documento, si los hay ----
                    if (otrosDatos.Count > 0)
                    {
                        col.Item().PaddingTop(4).Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn(2);
                            });
                            foreach (var (clave, valor) in otrosDatos)
                            {
                                tabla.Cell().Element(EtiquetaVerde).Text(clave);
                                tabla.Cell().Element(ValorConBorde).Text(valor);
                            }
                        });
                    }

                    // ---- Renglones ----
                    if (d.Renglones.Count > 0)
                    {
                        col.Item().PaddingTop(6).Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(22);
                                c.ConstantColumn(55);
                                c.RelativeColumn(3);
                                c.ConstantColumn(40);
                                c.ConstantColumn(40);
                                c.ConstantColumn(65);
                                c.ConstantColumn(32);
                                c.ConstantColumn(65);
                            });

                            tabla.Header(encabezado =>
                            {
                                CeldaEncabezado(encabezado.Cell(), "#");
                                CeldaEncabezado(encabezado.Cell(), "Código");
                                CeldaEncabezado(encabezado.Cell(), "Descripción");
                                CeldaEncabezado(encabezado.Cell(), "Cant.");
                                CeldaEncabezado(encabezado.Cell(), "U Med");
                                CeldaEncabezado(encabezado.Cell(), "Vlr. Unit.");
                                CeldaEncabezado(encabezado.Cell(), "IVA");
                                CeldaEncabezado(encabezado.Cell(), "Total");
                            });

                            var numero = 0;
                            foreach (var r in d.Renglones)
                            {
                                numero++;
                                CeldaDato(tabla.Cell(), numero.ToString());
                                CeldaDato(tabla.Cell(), r.Codigo ?? "");
                                CeldaDato(tabla.Cell(), r.Descripcion);
                                CeldaDato(tabla.Cell(), $"{r.Cantidad:0.##}");
                                CeldaDato(tabla.Cell(), r.UnidadMedida ?? "");
                                CeldaDato(tabla.Cell(), FormatoMoneda(r.ValorUnitario));
                                CeldaDato(tabla.Cell(), $"{r.PorcentajeIva:0%}");
                                CeldaDato(tabla.Cell(), FormatoMoneda(r.Total));
                            }
                        });

                        // ---- Totales ----
                        col.Item().AlignRight().PaddingTop(6).Width(220).Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.RelativeColumn();
                            });
                            AgregarTotal(tabla, "SUBTOTAL", subtotal, false);
                            AgregarTotal(tabla, "DESCUENTO", 0, false);
                            AgregarTotal(tabla, "IVA", totalIva, false);
                            AgregarTotal(tabla, "TOTAL DOCUMENTO", total, true);
                        });
                    }

                    // ---- Elaborado por / Aprobado por ----
                    col.Item().PaddingTop(10).Table(tabla =>
                    {
                        tabla.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });
                        tabla.Cell().Element(EtiquetaVerde).Text("ELABORADO POR:");
                        tabla.Cell().Element(EtiquetaVerde).Text("APROBADO POR:");
                        tabla.Cell().Element(ValorConBorde).Text(d.CreadoPorNombre);
                        tabla.Cell().Element(ValorConBorde)
                            .Text(aprobadoPor is null ? "Pendiente" : $"{aprobadoPor.UsuarioNombre} ({aprobadoPor.Fecha.ToString("d MMM yyyy", CulturaEsCo)})");
                    });

                    // ---- Pie de página con datos de contacto, como en el original ----
                    col.Item().PaddingTop(8).Background(Colors.Green.Darken2).Padding(6)
                        .AlignCenter().Text(PiePagina).FontColor(Colors.White).FontSize(8);

                    // ---- Historial de aprobación: esto SÍ es propio de Jimaco Aprobaciones, no existe en World Office ----
                    if (d.Historial.Count > 0)
                    {
                        col.Item().PaddingTop(14).Text("Historial de aprobación").Bold().FontSize(9);
                        col.Item().Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(80);
                                c.RelativeColumn(2);
                                c.RelativeColumn();
                                c.RelativeColumn(3);
                            });
                            tabla.Header(encabezado =>
                            {
                                CeldaEncabezado(encabezado.Cell(), "Fecha");
                                CeldaEncabezado(encabezado.Cell(), "Usuario");
                                CeldaEncabezado(encabezado.Cell(), "Acción");
                                CeldaEncabezado(encabezado.Cell(), "Comentario");
                            });
                            foreach (var h in d.Historial)
                            {
                                CeldaDato(tabla.Cell(), h.Fecha.ToString("d MMM yyyy HH:mm", CulturaEsCo));
                                CeldaDato(tabla.Cell(), h.UsuarioNombre);
                                CeldaDato(tabla.Cell(), h.Accion.ToString());
                                CeldaDato(tabla.Cell(), h.Comentario ?? "");
                            }
                        });
                    }
                });
            });
        });

        return documento.GeneratePdf();
    }

    private static void AgregarTotal(TableDescriptor tabla, string etiqueta, decimal valor, bool destacado)
    {
        var celdaEtiqueta = tabla.Cell().Element(EtiquetaVerde).Text(etiqueta).FontSize(8);
        var celdaValor = tabla.Cell().Element(ValorConBorde).AlignRight().Text(FormatoMoneda(valor));
        if (destacado)
        {
            celdaEtiqueta.Bold();
            celdaValor.Bold();
        }
    }

    private static void CeldaEncabezado(IContainer contenedor, string texto) =>
        contenedor.Border(0.75f).Background(Colors.Green.Lighten3).Padding(3).Text(texto).Bold().FontSize(7.5f);

    private static void CeldaDato(IContainer contenedor, string texto) =>
        contenedor.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(texto).FontSize(7.5f);

    // Franja verde de etiqueta, igual que las cabeceras de sección de World Office (PROVEEDOR,
    // FECHA DOCUMENTO, etc.) — texto blanco en negrita sobre fondo verde, con borde fino.
    private static IContainer EtiquetaVerde(IContainer contenedor) =>
        contenedor.Border(0.75f).Background(VerdeEtiqueta).Padding(4).DefaultTextStyle(x => x.FontColor(Colors.White).FontSize(8).Bold());

    // Celda de valor: blanca con borde, como en el original (a diferencia de la primera versión,
    // que usaba fondo gris liso sin bordes).
    private static IContainer ValorConBorde(IContainer contenedor) =>
        contenedor.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(4);

    private static string FormatoMoneda(decimal valor) => $"$ {valor:N0}";
}
