using System.Globalization;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Genera un PDF con el mismo formato visual que la Orden de Compra real de World Office
/// (ver los 4 PDF de ejemplo y esquema-worldoffice-oc.md) — pero genérico para cualquier
/// <see cref="Modelo.Entidades.TipoDocumento"/>, no hardcodeado a "Orden de Compra": el título sale
/// del propio documento. A diferencia del PDF de World Office, este además incluye el historial de
/// aprobación completo, que es justo la parte que World Office no tiene.
/// </summary>
public class DocumentoPdfGenerador : IDocumentoPdfGenerador
{
    // Explícito en vez de confiar en la cultura del hilo actual (que en el contenedor Linux
    // arranca en invariant/en-US) — sin esto, los nombres de mes salían en inglés.
    private static readonly CultureInfo CulturaEsCo = CultureInfo.GetCultureInfo("es-CO");

    public byte[] Generar(InstanciaDocumentoDetalleDto d)
    {
        var subtotal = d.Renglones.Sum(r => r.Total);
        var totalIva = d.Renglones.Sum(r => r.Total * r.PorcentajeIva);
        var total = subtotal + totalIva;

        var documento = Document.Create(contenedor =>
        {
            contenedor.Page(pagina =>
            {
                pagina.Size(PageSizes.A4);
                pagina.Margin(30);
                // Fuente explícita en vez de dejar que Skia elija: en Linux sin fontconfig bien
                // configurado, la resolución automática de glifos puede fallar silenciosamente
                // y "comerse" letras (ver el Dockerfile de la Api — instala DejaVu justamente
                // para que este nombre exista de verdad en el contenedor).
                pagina.DefaultTextStyle(x => x.FontSize(9).FontFamily("DejaVu Sans"));

                pagina.Header().Background(Colors.Green.Darken2).Padding(14).Row(fila =>
                {
                    fila.RelativeItem().Column(col =>
                    {
                        col.Item().Text("JIMACO").FontSize(20).Bold().FontColor(Colors.White);
                        col.Item().Text(d.TipoDocumentoNombre.ToUpperInvariant()).FontSize(11).FontColor(Colors.White);
                    });
                    fila.ConstantItem(140).Column(col =>
                    {
                        col.Item().AlignRight().Text($"No. {d.NumeroReferencia}").FontSize(14).Bold().FontColor(Colors.White);
                        col.Item().AlignRight().Text(EtiquetaEstado(d.Estado)).FontSize(9).FontColor(Colors.White);
                    });
                });

                pagina.Content().PaddingTop(16).Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Table(tabla =>
                    {
                        tabla.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        AgregarDato(tabla, "Proveedor", d.Proveedor ?? "—");
                        AgregarDato(tabla, "Fecha del documento", d.FechaDocumento?.ToString("d 'de' MMMM 'de' yyyy", CulturaEsCo) ?? "—");
                        AgregarDato(tabla, "Emitido por", d.CreadoPorNombre);
                        AgregarDato(tabla, "Paso actual", d.PasoActualNombre ?? "— (finalizado)");

                        if (d.Datos is not null)
                            foreach (var (clave, valor) in d.Datos)
                                AgregarDato(tabla, clave, valor);
                    });

                    if (d.Renglones.Count > 0)
                    {
                        col.Item().Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(24);
                                c.ConstantColumn(60);
                                c.RelativeColumn(3);
                                c.ConstantColumn(45);
                                c.ConstantColumn(70);
                                c.ConstantColumn(35);
                                c.ConstantColumn(70);
                            });

                            tabla.Header(encabezado =>
                            {
                                CeldaEncabezado(encabezado.Cell(), "#");
                                CeldaEncabezado(encabezado.Cell(), "Código");
                                CeldaEncabezado(encabezado.Cell(), "Descripción");
                                CeldaEncabezado(encabezado.Cell(), "Cant.");
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
                                CeldaDato(tabla.Cell(), $"{r.Cantidad:0.##} {r.UnidadMedida}");
                                CeldaDato(tabla.Cell(), FormatoMoneda(r.ValorUnitario));
                                CeldaDato(tabla.Cell(), $"{r.PorcentajeIva:0%}");
                                CeldaDato(tabla.Cell(), FormatoMoneda(r.Total));
                            }
                        });

                        col.Item().AlignRight().Width(220).Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.ConstantColumn(100);
                            });
                            AgregarTotal(tabla, "Subtotal", subtotal, false);
                            AgregarTotal(tabla, "IVA", totalIva, false);
                            AgregarTotal(tabla, "Total documento", total, true);
                        });
                    }

                    if (d.Historial.Count > 0)
                    {
                        col.Item().Column(historial =>
                        {
                            historial.Item().Text("Historial de aprobación").Bold().FontSize(10);
                            historial.Item().Table(tabla =>
                            {
                                tabla.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(90);
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
                        });
                    }
                });

                pagina.Footer().AlignCenter().Text("Generado por Jimaco Aprobaciones").FontSize(7).FontColor(Colors.Grey.Medium);
            });
        });

        return documento.GeneratePdf();
    }

    private static void AgregarDato(TableDescriptor tabla, string etiqueta, string valor)
    {
        tabla.Cell().Element(CeldaEtiquetaEstilo).Text(etiqueta);
        tabla.Cell().Element(CeldaValorEstilo).Text(valor);
    }

    private static void AgregarTotal(TableDescriptor tabla, string etiqueta, decimal valor, bool destacado)
    {
        var etiquetaCelda = tabla.Cell().Element(c => c.PaddingVertical(2)).AlignRight().Text(etiqueta);
        var valorCelda = tabla.Cell().Element(c => c.PaddingVertical(2)).AlignRight().Text(FormatoMoneda(valor));
        if (destacado)
        {
            etiquetaCelda.Bold();
            valorCelda.Bold();
        }
    }

    private static void CeldaEncabezado(IContainer contenedor, string texto) =>
        contenedor.Background(Colors.Green.Lighten4).Padding(4).Text(texto).Bold().FontSize(8);

    private static void CeldaDato(IContainer contenedor, string texto) =>
        contenedor.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(texto).FontSize(8);

    private static IContainer CeldaEtiquetaEstilo(IContainer contenedor) =>
        contenedor.Background(Colors.Grey.Lighten4).Padding(5);

    private static IContainer CeldaValorEstilo(IContainer contenedor) =>
        contenedor.Padding(5);

    private static string FormatoMoneda(decimal valor) => $"$ {valor:N0}";

    private static string EtiquetaEstado(Modelo.Entidades.EstadoInstanciaDocumento estado) => estado switch
    {
        Modelo.Entidades.EstadoInstanciaDocumento.EnProceso => "En proceso",
        Modelo.Entidades.EstadoInstanciaDocumento.Devuelto => "Devuelto",
        Modelo.Entidades.EstadoInstanciaDocumento.Completado => "Completado",
        Modelo.Entidades.EstadoInstanciaDocumento.Rechazado => "Rechazado",
        _ => estado.ToString()
    };
}
