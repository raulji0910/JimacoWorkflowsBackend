using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Revisa con Claude (visión) lo que se adjunta en Entregas:
/// <list type="bullet">
/// <item>La foto o el escaneo de una remisión entregada (2026-11-12): qué número de remisión se lee, si
/// hay firma manuscrita o sello del cliente (cualquiera de los dos vale) y si es legible.</item>
/// <item>Los documentos para facturar (2026-11-13): si el archivo es el tipo documental pedido (OC,
/// entrada de almacén, proforma...), si es del cliente y qué número de OC trae.</item>
/// </list>
/// Activo solo si hay clave (<c>ANTHROPIC_API_KEY</c> o <c>Anthropic:ApiKey</c>) y
/// <c>Entregas:VerificacionIA:Activo</c> no es false (además, el admin la enciende en la página).
/// Nunca lanza: un error técnico vuelve como "no analizado" con el motivo.
/// </summary>
public class VerificadorSoporteEntregaIA : IVerificadorSoporteEntrega
{
    private const string ModeloPorDefecto = "claude-opus-5-5";

    private static readonly string[] ImagenesSoportadas = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    private const string InstruccionesEntrega = """
        Revisas el soporte de entrega de una remisión de FERRETERIA INDUSTRIAL JIMACO: una foto o un
        escaneo de la hoja "REMISION No." que el conductor le hace firmar al cliente al entregar.
        Respondé solo con lo que se ve en la imagen, sin suponer nada:
        - numero_remision: el número que aparece junto a "REMISION No." (solo dígitos), o null si no se lee.
        - es_remision_jimaco: true si la hoja es una remisión de Jimaco (logo JIMACO, título REMISION No.).
        - hay_firma_cliente: true solo si hay una firma o rúbrica manuscrita del cliente (en "Recibido Por",
          en la zona de firma o en otra parte de la hoja). Un nombre impreso por el sistema no es firma;
          un garabato, rúbrica o nombre escrito a mano sí lo es.
        - hay_sello_cliente: true solo si hay un sello estampado (de tinta, de caucho) de la empresa o
          del cliente que recibe — suele traer su nombre, NIT, "RECIBIDO" y/o una fecha. NO cuentan los
          sellos y logos impresos en el formato de la remisión: el logo de JIMACO y el sello de
          certificación SGS / ISO 9001 que trae la hoja impresa.
        - hay_nombre_o_cedula: true si se ve a mano el nombre o la cédula de quien recibe.
        - legible: false si la imagen está tan borrosa, oscura o cortada que no se puede revisar.
        - confianza: "alta", "media" o "baja".
        - observacion: una frase corta en español para el operario de logística (qué falta o qué se ve;
          si hay sello, decí de quién es si se lee).
        """;

    private const string InstruccionesDocumento = """
        Revisas un documento que una vendedora de FERRETERIA INDUSTRIAL JIMACO (el proveedor) adjunta
        para que contabilidad pueda facturar una remisión ya entregada a un cliente. Te digo qué tipo de
        documento se pidió y los datos de la remisión. Respondé solo con lo que se ve en el documento,
        sin suponer nada:
        - tipo_documento_detectado: qué documento es, en pocas palabras (ej. "Orden de compra",
          "Entrada de almacén", "Proforma", "Factura", "Remisión", "Cotización", "Foto sin documento").
        - es_tipo_esperado: true si el documento es del tipo pedido (aunque el formato del cliente sea
          distinto o tenga otro nombre equivalente, p. ej. "Recibo de mercancía" = entrada de almacén,
          "Pedido de compra" = orden de compra).
        - cliente_coincide: true si el documento es de la empresa del cliente indicado (por nombre o NIT),
          false si es claramente de otra empresa, null si no se puede saber.
        - numero_orden_compra: el número de orden de compra / pedido que aparezca en el documento, o null.
        - legible: false si está tan borroso, oscuro o cortado que no se puede revisar.
        - observacion: una frase corta en español para la vendedora (qué es y, si algo no cuadra, qué).
        """;

    private readonly IConfiguration configuration;
    private readonly ILogger<VerificadorSoporteEntregaIA> logger;
    private readonly AnthropicClient? cliente;

    public VerificadorSoporteEntregaIA(IConfiguration configuration, ILogger<VerificadorSoporteEntregaIA> logger)
    {
        this.configuration = configuration;
        this.logger = logger;
        var clave = configuration["Anthropic:ApiKey"] ?? configuration["ANTHROPIC_API_KEY"];
        var activo = !(bool.TryParse(configuration["Entregas:VerificacionIA:Activo"], out var a) && !a);
        if (activo && !string.IsNullOrWhiteSpace(clave))
            cliente = new AnthropicClient { ApiKey = clave };
    }

    public bool Activo => cliente is not null;

    private string Modelo => configuration["Entregas:VerificacionIA:Modelo"] ?? ModeloPorDefecto;

    public async Task<ResultadoVerificacionIA> VerificarAsync(byte[] contenido, string? contentType, CancellationToken ct = default)
    {
        var (datos, error) = await PreguntarAsync<RespuestaEntrega>(contenido, contentType, InstruccionesEntrega,
            "Revisá este soporte de entrega.", EsquemaEntrega(), ct);
        if (datos is null)
            return ResultadoVerificacionIA.NoAnalizado(error!);

        return new ResultadoVerificacionIA(
            Analizado: true,
            NumeroLeido: string.IsNullOrWhiteSpace(datos.numero_remision) ? null : new string(datos.numero_remision.Where(char.IsDigit).ToArray()),
            EsRemisionJimaco: datos.es_remision_jimaco,
            HayFirma: datos.hay_firma_cliente,
            HayNombreOCedula: datos.hay_nombre_o_cedula,
            Legible: datos.legible,
            Confianza: datos.confianza ?? "baja",
            Observacion: datos.observacion,
            Error: null,
            HaySello: datos.hay_sello_cliente);
    }

    public async Task<ResultadoDocumentoFacturacionIA> VerificarDocumentoAsync(byte[] contenido, string? contentType,
        ContextoDocumentoFacturacion contexto, CancellationToken ct = default)
    {
        var pregunta = $"""
            Documento pedido: {contexto.TipoEsperado}{(string.IsNullOrWhiteSpace(contexto.DescripcionTipo) ? "" : $" ({contexto.DescripcionTipo})")}.
            Remisión de Jimaco: {contexto.NumeroRemision}.
            Cliente: {contexto.Cliente ?? "(sin dato)"}{(string.IsNullOrWhiteSpace(contexto.NitCliente) ? "" : $", NIT {contexto.NitCliente}")}.
            Orden de compra del cliente según la remisión: {(string.IsNullOrWhiteSpace(contexto.OrdenCompraCliente) ? "(sin dato)" : contexto.OrdenCompraCliente)}.
            Revisá el documento adjunto.
            """;
        var (datos, error) = await PreguntarAsync<RespuestaDocumento>(contenido, contentType, InstruccionesDocumento,
            pregunta, EsquemaDocumento(), ct);
        if (datos is null)
            return ResultadoDocumentoFacturacionIA.NoAnalizado(error!);

        return new ResultadoDocumentoFacturacionIA(
            Analizado: true,
            EsTipoEsperado: datos.es_tipo_esperado,
            TipoDetectado: datos.tipo_documento_detectado,
            ClienteCoincide: datos.cliente_coincide,
            NumeroOrdenCompraLeido: string.IsNullOrWhiteSpace(datos.numero_orden_compra) ? null : datos.numero_orden_compra.Trim(),
            Legible: datos.legible,
            Observacion: datos.observacion,
            Error: null);
    }

    /// <summary>Una llamada a Claude con el archivo + pregunta, respuesta JSON con el esquema dado.</summary>
    private async Task<(T? Datos, string? Error)> PreguntarAsync<T>(byte[] contenido, string? contentType,
        string instrucciones, string pregunta, Dictionary<string, JsonElement> esquema, CancellationToken ct) where T : class
    {
        if (cliente is null)
            return (null, "La revisión con IA no está configurada.");

        var tipo = (contentType ?? "").ToLowerInvariant();
        BetaContentBlockParam bloqueArchivo;
        if (tipo == "application/pdf")
        {
            bloqueArchivo = new BetaRequestDocumentBlock { Source = new BetaBase64PdfSource { Data = Convert.ToBase64String(contenido) } };
        }
        else if (ImagenesSoportadas.Contains(tipo))
        {
            bloqueArchivo = new BetaImageBlockParam
            {
                Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(contenido), MediaType = tipo }
            };
        }
        else
        {
            return (null, $"Formato no soportado para la revisión con IA ({contentType}). Usá foto JPG/PNG o PDF.");
        }

        try
        {
            var respuesta = await cliente.Beta.Messages.Create(new MessageCreateParams
            {
                Model = Modelo,
                MaxTokens = 2048,
                // Si el modelo declina la solicitud por políticas, el servidor la reintenta con el
                // modelo de respaldo recomendado (fallbacks "default").
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
                // Tarea de extracción simple: esfuerzo bajo = más rápido y barato.
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.Low,
                    Format = new BetaJsonOutputFormat { Schema = esquema },
                },
                System = instrucciones,
                Messages =
                [
                    new BetaUserMessageParam
                    {
                        Content = new List<BetaContentBlockParam>
                        {
                            bloqueArchivo,
                            new BetaTextBlockParam { Text = pregunta },
                        },
                    },
                ],
            }, cancellationToken: ct);

            if (respuesta.StopReason == "refusal")
                return (null, "La IA no pudo revisar este archivo.");

            var texto = string.Concat(respuesta.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            var datos = JsonSerializer.Deserialize<T>(texto, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return datos is null ? (null, "La IA devolvió una respuesta vacía.") : (datos, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falló la revisión con IA de un archivo de Entregas");
            return (null, "No se pudo revisar con IA en este momento. Intentá de nuevo en un minuto.");
        }
    }

    private static Dictionary<string, JsonElement> Objeto(object propiedades, string[] requeridos) => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(propiedades),
        ["required"] = JsonSerializer.SerializeToElement(requeridos),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    private static Dictionary<string, JsonElement> EsquemaEntrega() => Objeto(new
    {
        numero_remision = new { type = new[] { "string", "null" } },
        es_remision_jimaco = new { type = "boolean" },
        hay_firma_cliente = new { type = "boolean" },
        hay_sello_cliente = new { type = "boolean" },
        hay_nombre_o_cedula = new { type = "boolean" },
        legible = new { type = "boolean" },
        confianza = new { type = "string", @enum = new[] { "alta", "media", "baja" } },
        observacion = new { type = "string" },
    }, ["numero_remision", "es_remision_jimaco", "hay_firma_cliente", "hay_sello_cliente", "hay_nombre_o_cedula", "legible", "confianza", "observacion"]);

    private static Dictionary<string, JsonElement> EsquemaDocumento() => Objeto(new
    {
        tipo_documento_detectado = new { type = "string" },
        es_tipo_esperado = new { type = "boolean" },
        cliente_coincide = new { type = new[] { "boolean", "null" } },
        numero_orden_compra = new { type = new[] { "string", "null" } },
        legible = new { type = "boolean" },
        observacion = new { type = "string" },
    }, ["tipo_documento_detectado", "es_tipo_esperado", "cliente_coincide", "numero_orden_compra", "legible", "observacion"]);

    // Nombres en snake_case = los de los esquemas JSON que devuelve el modelo.
    private sealed record RespuestaEntrega(
        string? numero_remision, bool es_remision_jimaco, bool hay_firma_cliente, bool hay_sello_cliente, bool hay_nombre_o_cedula,
        bool legible, string? confianza, string? observacion);

    private sealed record RespuestaDocumento(
        string? tipo_documento_detectado, bool es_tipo_esperado, bool? cliente_coincide, string? numero_orden_compra,
        bool legible, string? observacion);
}
