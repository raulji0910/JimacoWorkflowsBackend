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
    /// <summary>
    /// Logo, nombre, NIT, pie y colores de cada empresa del grupo (2026-11-13, FERREALIADOS JV).
    /// ColorEtiqueta = cajas de título/etiquetas; ColorClaro = encabezado de tablas; ColorPie = barra del pie.
    /// </summary>
    private sealed record Marca(byte[] Logo, string NombreEmpresa, string Nit, string PiePagina,
        string ColorEtiqueta, string ColorClaro, string ColorPie);

    private static readonly Marca MarcaJimaco = new(
        CargarLogo("logo-jimaco.png"), "FERRETERIA INDUSTRIAL JIMACO", "900632353",
        "Pbx: 601 378 29 33  --  Calle 23 # 25 - 06   compras@jimaco.com.co   -   www.jimaco.com.co",
        Colors.Green.Medium, Colors.Green.Lighten3, Colors.Green.Darken2);

    // Datos y colores tomados de Ferrealiados Cotizaciones (appsettings "DatosEmpresa" y
    // CotizacionPdfBuilder: azul #0A578D, naranja #F38138, azul claro #EAF1F5). Sin teléfono (allá
    // también está vacío).
    private static readonly Marca MarcaFerrealiados = new(
        CargarLogo("logo-ferrealiados.png"), "FERREALIADOS JV", "901.342.748-6",
        "CLL 15 25 20   -   Bogotá - Colombia   -   NIT 901.342.748-6",
        "#0A578D", "#EAF1F5", "#F38138");

    // La marca del documento que se está generando (el generador es singleton: se pasa por llamada).
    [ThreadStatic] private static Marca? marcaActual;
    private static Marca M => marcaActual ?? MarcaJimaco;

    // Claves conocidas de Datos que este formato ubica en su lugar "de siempre" (mismo layout que
    // World Office); cualquier otra clave que traiga el documento se muestra igual, más abajo, en
    // una sección genérica — así un TipoDocumento con campos distintos no pierde información.
    private const string ClaveDocExterno = "cotizacionOrigen";
    private const string ClaveElaboradoPor = "elaboradoPor";
    private const string ClaveFormaDePago = "formaDePago";
    private const string ClaveObservaciones = "observaciones";
    private const string ClaveObservacionesAdicionales = "observacionesAdicionales";
    private const string ClaveNit = "nit";
    private const string ClaveDireccionProveedor = "direccionProveedor";

    private static byte[] CargarLogo(string archivo)
    {
        var ensamblado = Assembly.GetExecutingAssembly();
        using var stream = ensamblado.GetManifestResourceStream($"Jimaco.Aprobaciones.Negocio.Recursos.{archivo}")
            ?? throw new InvalidOperationException("No se encontró el logo embebido.");
        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }

    public byte[] Generar(InstanciaDocumentoDetalleDto d)
    {
        marcaActual = d.Empresa == "Ferrealiados" ? MarcaFerrealiados : MarcaJimaco;
        try
        {
            return GenerarConMarca(d);
        }
        finally
        {
            marcaActual = null;
        }
    }

    private byte[] GenerarConMarca(InstanciaDocumentoDetalleDto d)
    {
        var datos = d.Datos ?? [];
        var subtotal = d.Renglones.Sum(r => r.Total);
        var totalIva = d.Renglones.Sum(r => r.Total * r.PorcentajeIva);
        var total = subtotal + totalIva;
        var aprobadoPor = d.Historial.LastOrDefault(h => h.Accion == TipoAccion.Aprobado);
        // Flujo sin pasos (crédito de Ferrealiados): nadie aprobó, quedó aprobado al llegar.
        var aprobadoAutomatico = d.Historial.LastOrDefault(h => h.Accion == TipoAccion.AprobadoAutomatico);

        var otrosDatos = datos
            .Where(kv => kv.Key is not (ClaveDocExterno or ClaveElaboradoPor or ClaveFormaDePago or ClaveObservaciones or ClaveObservacionesAdicionales or ClaveNit or ClaveDireccionProveedor))
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
                        fila.ConstantItem(220).Height(85).Image(M.Logo).FitArea();
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

                    // ---- Observaciones adicionales (campo libre de WO, distinto del de arriba —
                    // ver ClaveObservacionesAdicionales) — solo si el documento trae algo, para no
                    // agregar una caja vacía a los documentos viejos que no la tienen. ----
                    if (datos.TryGetValue(ClaveObservacionesAdicionales, out var observacionesAdicionales) && !string.IsNullOrWhiteSpace(observacionesAdicionales))
                    {
                        col.Item().Table(tabla =>
                        {
                            tabla.ColumnsDefinition(c => c.RelativeColumn());
                            tabla.Cell().Element(EtiquetaVerde).Text("OBSERVACIONES ADICIONALES");
                            tabla.Cell().Element(c => ValorConBorde(c).MinHeight(20)).Text(observacionesAdicionales);
                        });
                    }

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
                                c.ConstantColumn(22);
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
                                CeldaEncabezado(encabezado.Cell(), "Ver");
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
                                CeldaDatoCentrado(tabla.Cell(), "☐");
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

                    // ---- Elaborado por / Aprobado por: una sola franja por campo (etiqueta +
                    // valor en la misma fila), igual que el resto de secciones y que el original
                    // de World Office — antes iba en dos filas separadas (etiquetas arriba,
                    // valores abajo), que no calzaba con el formato real.
                    col.Item().PaddingTop(10).Table(tabla =>
                    {
                        tabla.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn(2);
                            c.RelativeColumn();
                            c.RelativeColumn(2);
                        });
                        // Mismo criterio que el campo "ELABORADO POR" de la cabecera: si el
                        // documento vino sincronizado desde World Office, Datos.elaboradoPor trae
                        // a la persona real que lo generó allá — d.CreadoPorNombre en ese caso es
                        // el usuario de servicio del Sincronizador, no un dato útil para mostrar acá.
                        tabla.Cell().Element(EtiquetaVerde).Text("ELABORADO POR:");
                        tabla.Cell().Element(ValorConBorde).Text(datos.GetValueOrDefault(ClaveElaboradoPor, d.CreadoPorNombre));
                        tabla.Cell().Element(EtiquetaVerde).Text("APROBADO POR:");
                        tabla.Cell().Element(ValorConBorde)
                            .Text(aprobadoPor is not null ? $"{aprobadoPor.UsuarioNombre} ({aprobadoPor.Fecha.ToString("d MMM yyyy", CulturaEsCo)})"
                                : aprobadoAutomatico is not null ? $"Automático, sin aprobación ({aprobadoAutomatico.Fecha.ToString("d MMM yyyy", CulturaEsCo)})"
                                : "Pendiente");
                    });

                    // ---- Pie de página con datos de contacto, como en el original ----
                    col.Item().PaddingTop(8).Background(M.ColorPie).Padding(6)
                        .AlignCenter().Text(M.PiePagina).FontColor(Colors.White).FontSize(8);

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
                                CeldaDato(tabla.Cell(), h.Accion == TipoAccion.AprobadoAutomatico ? "Aprobado automáticamente" : h.Accion.ToString());
                                CeldaDato(tabla.Cell(), h.Comentario ?? "");
                            }
                        });
                    }
                });
            });

            // ---- Hojas de Alistamiento: una página aparte por cada documento externo ligado
            // (ej. la(s) Cotización(es) que originaron esta OC) — mismo formato con el que
            // World Office las imprime, para que quien recibe la OC no tenga que ir a buscarlas
            // aparte en el sistema.
            foreach (var vinculado in d.DocumentosVinculados)
                contenedor.Page(pagina => AgregarHojaAlistamiento(pagina, vinculado));

            // ---- Comprobante de Egreso: una página aparte, calcada del que Contabilidad generó
            // en World Office (ver ComprobanteEgreso) — así Gerencia General lo ve sin tener que
            // entrar a WO a buscarlo.
            if (d.ComprobanteEgreso is not null)
                contenedor.Page(pagina => AgregarComprobanteEgreso(pagina, d.ComprobanteEgreso));
        });

        return documento.GeneratePdf();
    }

    private void AgregarHojaAlistamiento(PageDescriptor pagina, DocumentoVinculadoDto v)
    {
        var subtotal = v.Renglones.Sum(r => r.Total);
        var totalIva = v.Renglones.Sum(r => r.Total * r.PorcentajeIva);
        var total = subtotal + totalIva;

        pagina.Size(PageSizes.A4);
        pagina.Margin(24);
        pagina.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("DejaVu Sans"));

        pagina.Content().Column(col =>
        {
            col.Spacing(0);

            // ---- Encabezado: empresa / "HOJA ALISTAMIENTO" / número, plano en blanco y negro
            // (no lleva la marca verde de nuestro propio formato — es una réplica del reporte
            // real de World Office, que se imprime así) ----
            col.Item().Row(fila =>
            {
                fila.RelativeItem(1.6f).Text(M.NombreEmpresa).Bold().FontSize(10.5f);
                fila.RelativeItem().AlignCenter().Text("HOJA ALISTAMIENTO").Bold().FontSize(11);
                fila.RelativeItem().AlignRight().Text(v.NumeroReferencia).Bold().FontSize(11);
            });
            col.Item().PaddingBottom(4).Text($"Nit {M.Nit}").Bold();

            // ---- Cliente / NIT / Dirección / Ciudad / Teléfono (izquierda) + Campo de entrega (derecha) ----
            col.Item().Row(fila =>
            {
                fila.RelativeItem(65).Table(tabla =>
                {
                    tabla.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.RelativeColumn(2);
                    });
                    tabla.Cell().Element(EtiquetaPlana).Text("CLIENTE");
                    tabla.Cell().Element(ValorPlano).Text(v.Cliente ?? "—");
                    tabla.Cell().Element(EtiquetaPlana).Text("NIT");
                    tabla.Cell().Element(ValorPlano).Text(v.NitCliente ?? "—");
                    tabla.Cell().Element(EtiquetaPlana).Text("DIRECCION");
                    tabla.Cell().Element(ValorPlano).Text(v.Direccion ?? "—");
                    tabla.Cell().Element(EtiquetaPlana).Text("CIUDAD");
                    tabla.Cell().Element(ValorPlano).Text(v.Ciudad ?? "—");
                    tabla.Cell().Element(EtiquetaPlana).Text("TELEFONO");
                    tabla.Cell().Element(ValorPlano).Text(v.Telefono ?? "—");
                });

                fila.RelativeItem(35).PaddingLeft(4).Table(tabla =>
                {
                    tabla.ColumnsDefinition(c => c.RelativeColumn());
                    tabla.Cell().Element(EtiquetaPlana).AlignCenter().Text("CAMPO ENTREGA - CIUDAD - DIRECCION");
                    tabla.Cell().Element(c => ValorPlano(c).MinHeight(40)).Text(v.CampoEntrega ?? "");
                });
            });

            // ---- Fecha documento / Fecha vencimiento / Elaborado por / Forma de pago ----
            col.Item().PaddingTop(2).Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn(2);
                    c.RelativeColumn();
                });
                tabla.Cell().Element(EtiquetaPlana).AlignCenter().Text("FECHA DOCUMENTO");
                tabla.Cell().Element(EtiquetaPlana).AlignCenter().Text("FECHA VENCIMIENTO");
                tabla.Cell().Element(EtiquetaPlana).AlignCenter().Text("ELABORADO POR:");
                tabla.Cell().Element(EtiquetaPlana).AlignCenter().Text("FORMA DE PAGO");

                tabla.Cell().Element(ValorPlano).AlignCenter().Text(v.Fecha.ToString("d 'de' MMMM 'de' yyyy", CulturaEsCo));
                tabla.Cell().Element(ValorPlano).AlignCenter().Text(v.FechaVencimiento.ToString("d-MMM-yy", CulturaEsCo));
                tabla.Cell().Element(ValorPlano).AlignCenter().Text(v.ElaboradoPor ?? "—");
                tabla.Cell().Element(ValorPlano).AlignCenter().Text(v.FormaDePago ?? "—");
            });

            // ---- Renglones: Código / Descripción / DESP 1 / DESP 2 (para que alistamiento
            // marque a mano) / Cantidad / U Medida — sin precios, es una lista de picking, no
            // un documento de valores ----
            col.Item().PaddingTop(6).Table(tabla =>
            {
                tabla.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(55);
                    c.RelativeColumn(3);
                    c.ConstantColumn(55);
                    c.ConstantColumn(55);
                    c.ConstantColumn(45);
                    c.ConstantColumn(45);
                });

                tabla.Header(encabezado =>
                {
                    CeldaEncabezado(encabezado.Cell(), "Código");
                    CeldaEncabezado(encabezado.Cell(), "Descripción");
                    CeldaEncabezado(encabezado.Cell(), "DESP 1");
                    CeldaEncabezado(encabezado.Cell(), "DESP 2");
                    CeldaEncabezado(encabezado.Cell(), "Cantidad");
                    CeldaEncabezado(encabezado.Cell(), "U Medida");
                });

                foreach (var r in v.Renglones)
                {
                    CeldaDato(tabla.Cell(), r.Codigo ?? "");
                    CeldaDato(tabla.Cell(), r.Descripcion);
                    tabla.Cell().Element(c => ValorPlano(c).MinHeight(18)).Text("");
                    tabla.Cell().Element(c => ValorPlano(c).MinHeight(18)).Text("");
                    CeldaDato(tabla.Cell(), $"{r.Cantidad:0.##}");
                    CeldaDato(tabla.Cell(), r.UnidadMedida ?? "");
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
                AgregarTotalPlano(tabla, "SUBTOTAL", subtotal, false);
                AgregarTotalPlano(tabla, "DESCUENTO", 0, false);
                AgregarTotalPlano(tabla, "IVA", totalIva, false);
                AgregarTotalPlano(tabla, "TOTAL DOCUMENTO", total, true);
            });

            // ---- Firmas ----
            col.Item().PaddingTop(20).Text("Firma Responsable__________________________________________________");
            col.Item().PaddingTop(4).Text("Verificado Por______________________________________________________");
        });
    }

    /// <summary>
    /// Comprobante de Egreso de World Office (prefijo "CEJ") — mismo criterio plano/blanco y negro
    /// que la Hoja de Alistamiento (réplica del reporte real, no nuestro formato con franja verde):
    /// cabecera con beneficiario/fecha/concepto y una tabla de líneas débito/crédito con la cuenta
    /// contable real (código + nombre, resueltos por el Sincronizador contra el catálogo de WO).
    /// </summary>
    private void AgregarComprobanteEgreso(PageDescriptor pagina, ComprobanteEgresoDto c)
    {
        var totalDebito = c.Renglones.Sum(r => r.Debito);
        var totalCredito = c.Renglones.Sum(r => r.Credito);

        pagina.Size(PageSizes.A4);
        pagina.Margin(24);
        pagina.DefaultTextStyle(x => x.FontSize(8.5f).FontFamily("DejaVu Sans"));

        pagina.Content().Column(col =>
        {
            col.Spacing(0);

            col.Item().Row(fila =>
            {
                fila.RelativeItem(1.6f).Text(M.NombreEmpresa).Bold().FontSize(10.5f);
                fila.RelativeItem().AlignCenter().Text("COMPROBANTE DE EGRESO").Bold().FontSize(11);
                fila.RelativeItem().AlignRight().Text(c.NumeroReferencia).Bold().FontSize(11);
            });
            col.Item().PaddingBottom(4).Text($"Nit {M.Nit}").Bold();

            // ---- Beneficiario / NIT / Dirección / Ciudad / Teléfono ----
            col.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(c2 =>
                {
                    c2.RelativeColumn();
                    c2.RelativeColumn(2);
                });
                tabla.Cell().Element(EtiquetaPlana).Text("BENEFICIARIO");
                tabla.Cell().Element(ValorPlano).Text(c.Beneficiario ?? "—");
                tabla.Cell().Element(EtiquetaPlana).Text("NIT");
                tabla.Cell().Element(ValorPlano).Text(c.NitBeneficiario ?? "—");
                tabla.Cell().Element(EtiquetaPlana).Text("DIRECCION");
                tabla.Cell().Element(ValorPlano).Text(c.Direccion ?? "—");
                tabla.Cell().Element(EtiquetaPlana).Text("CIUDAD");
                tabla.Cell().Element(ValorPlano).Text(c.Ciudad ?? "—");
                tabla.Cell().Element(EtiquetaPlana).Text("TELEFONO");
                tabla.Cell().Element(ValorPlano).Text(c.Telefono ?? "—");
            });

            // ---- Fecha / Elaborado por / Valor ----
            col.Item().PaddingTop(2).Table(tabla =>
            {
                tabla.ColumnsDefinition(c2 =>
                {
                    c2.RelativeColumn();
                    c2.RelativeColumn(2);
                    c2.RelativeColumn();
                });
                tabla.Cell().Element(EtiquetaPlana).AlignCenter().Text("FECHA");
                tabla.Cell().Element(EtiquetaPlana).AlignCenter().Text("ELABORADO POR");
                tabla.Cell().Element(EtiquetaPlana).AlignCenter().Text("VALOR");

                tabla.Cell().Element(ValorPlano).AlignCenter().Text(c.Fecha.ToString("d 'de' MMMM 'de' yyyy", CulturaEsCo));
                tabla.Cell().Element(ValorPlano).AlignCenter().Text(c.ElaboradoPor ?? "—");
                tabla.Cell().Element(ValorPlano).AlignCenter().Text(FormatoMoneda(c.Valor)).Bold();
            });

            // ---- Concepto ----
            col.Item().Table(tabla =>
            {
                tabla.ColumnsDefinition(c2 => c2.RelativeColumn());
                tabla.Cell().Element(EtiquetaPlana).Text("CONCEPTO");
                tabla.Cell().Element(c2 => ValorPlano(c2).MinHeight(20)).Text(c.Concepto ?? "");
            });

            // ---- Líneas débito/crédito, con la cuenta contable real (código + nombre) ----
            col.Item().PaddingTop(6).Table(tabla =>
            {
                tabla.ColumnsDefinition(c2 =>
                {
                    c2.ConstantColumn(55);
                    c2.RelativeColumn(2);
                    c2.RelativeColumn(2);
                    c2.ConstantColumn(70);
                    c2.ConstantColumn(70);
                });

                tabla.Header(encabezado =>
                {
                    CeldaEncabezado(encabezado.Cell(), "Cuenta");
                    CeldaEncabezado(encabezado.Cell(), "Nombre cuenta");
                    CeldaEncabezado(encabezado.Cell(), "Concepto");
                    CeldaEncabezado(encabezado.Cell(), "Débito");
                    CeldaEncabezado(encabezado.Cell(), "Crédito");
                });

                foreach (var r in c.Renglones)
                {
                    CeldaDato(tabla.Cell(), r.CodigoCuenta ?? "");
                    CeldaDato(tabla.Cell(), r.NombreCuenta ?? "");
                    CeldaDato(tabla.Cell(), r.Concepto ?? "");
                    tabla.Cell().Element(ValorPlano).AlignRight().Text(r.Debito == 0 ? "" : FormatoMoneda(r.Debito)).FontSize(7.5f);
                    tabla.Cell().Element(ValorPlano).AlignRight().Text(r.Credito == 0 ? "" : FormatoMoneda(r.Credito)).FontSize(7.5f);
                }

                CeldaEncabezado(tabla.Cell().ColumnSpan(3), "TOTALES");
                tabla.Cell().Element(ValorPlano).AlignRight().Text(FormatoMoneda(totalDebito)).Bold().FontSize(7.5f);
                tabla.Cell().Element(ValorPlano).AlignRight().Text(FormatoMoneda(totalCredito)).Bold().FontSize(7.5f);
            });

            col.Item().PaddingTop(20).Text("Firma Responsable__________________________________________________");
            col.Item().PaddingTop(4).Text("Recibido Por________________________________________________________");
        });
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

    private static void AgregarTotalPlano(TableDescriptor tabla, string etiqueta, decimal valor, bool destacado)
    {
        var celdaEtiqueta = tabla.Cell().Element(EtiquetaPlana).Text(etiqueta).FontSize(8);
        var celdaValor = tabla.Cell().Element(ValorPlano).AlignRight().Text(FormatoMoneda(valor));
        if (destacado)
        {
            celdaEtiqueta.Bold();
            celdaValor.Bold();
        }
    }

    // Etiqueta/valor en blanco y negro, sin la franja verde de nuestra marca — para la Hoja de
    // Alistamiento, que replica el reporte plano real de World Office, no nuestro formato propio.
    private static IContainer EtiquetaPlana(IContainer contenedor) =>
        contenedor.Border(0.75f).Padding(3).DefaultTextStyle(x => x.FontSize(7.5f).Bold());

    private static IContainer ValorPlano(IContainer contenedor) =>
        contenedor.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(3).DefaultTextStyle(x => x.FontSize(7.5f));

    private static void CeldaEncabezado(IContainer contenedor, string texto) =>
        contenedor.Border(0.75f).Background(M.ColorClaro).Padding(3).Text(texto).Bold().FontSize(7.5f);

    private static void CeldaDato(IContainer contenedor, string texto) =>
        contenedor.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(3).Text(texto).FontSize(7.5f);

    private static void CeldaDatoCentrado(IContainer contenedor, string texto) =>
        contenedor.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(3).AlignCenter().Text(texto).FontSize(9);

    // Franja verde de etiqueta, igual que las cabeceras de sección de World Office (PROVEEDOR,
    // FECHA DOCUMENTO, etc.) — texto blanco en negrita sobre fondo verde, con borde fino.
    private static IContainer EtiquetaVerde(IContainer contenedor) =>
        contenedor.Border(0.75f).Background(M.ColorEtiqueta).Padding(4).DefaultTextStyle(x => x.FontColor(Colors.White).FontSize(8).Bold());

    // Celda de valor: blanca con borde, como en el original (a diferencia de la primera versión,
    // que usaba fondo gris liso sin bordes).
    private static IContainer ValorConBorde(IContainer contenedor) =>
        contenedor.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(4);

    private static string FormatoMoneda(decimal valor) => $"$ {valor.ToString("N0", CulturaEsCo)}";
}
