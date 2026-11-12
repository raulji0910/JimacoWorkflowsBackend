using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Revisa con Claude (visión) la foto o el escaneo de una remisión firmada (2026-11-12, pedido del
/// usuario): qué número de remisión se lee, si hay firma manuscrita o sello del cliente (cualquiera
/// de los dos vale) y si es legible.
/// Activo solo si hay clave (<c>ANTHROPIC_API_KEY</c> o <c>Anthropic:ApiKey</c>) y
/// <c>Entregas:VerificacionIA:Activo</c> no es false. Nunca lanza: un error técnico vuelve como
/// <see cref="ResultadoVerificacionIA.Analizado"/> = false con el motivo.
/// </summary>
public class VerificadorSoporteEntregaIA : IVerificadorSoporteEntrega
{
    private const string ModeloPorDefecto = "claude-opus-5-5";

    private static readonly string[] ImagenesSoportadas = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    private const string Instrucciones = """
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
        if (cliente is null)
            return ResultadoVerificacionIA.NoAnalizado("La revisión con IA no está configurada.");

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
            return ResultadoVerificacionIA.NoAnalizado($"Formato no soportado para la revisión con IA ({contentType}). Usá foto JPG/PNG o PDF.");
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
                    Format = new BetaJsonOutputFormat { Schema = Esquema() },
                },
                System = Instrucciones,
                Messages =
                [
                    new BetaUserMessageParam
                    {
                        Content = new List<BetaContentBlockParam>
                        {
                            bloqueArchivo,
                            new BetaTextBlockParam { Text = "Revisá este soporte de entrega." },
                        },
                    },
                ],
            }, cancellationToken: ct);

            if (respuesta.StopReason == "refusal")
                return ResultadoVerificacionIA.NoAnalizado("La IA no pudo revisar este archivo.");

            var texto = string.Concat(respuesta.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            var datos = JsonSerializer.Deserialize<RespuestaIA>(texto, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (datos is null)
                return ResultadoVerificacionIA.NoAnalizado("La IA devolvió una respuesta vacía.");

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falló la revisión con IA del soporte de entrega");
            return ResultadoVerificacionIA.NoAnalizado("No se pudo revisar con IA en este momento. Intentá de nuevo en un minuto.");
        }
    }

    private static Dictionary<string, JsonElement> Esquema() => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            numero_remision = new { type = new[] { "string", "null" } },
            es_remision_jimaco = new { type = "boolean" },
            hay_firma_cliente = new { type = "boolean" },
            hay_sello_cliente = new { type = "boolean" },
            hay_nombre_o_cedula = new { type = "boolean" },
            legible = new { type = "boolean" },
            confianza = new { type = "string", @enum = new[] { "alta", "media", "baja" } },
            observacion = new { type = "string" },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[]
            { "numero_remision", "es_remision_jimaco", "hay_firma_cliente", "hay_sello_cliente", "hay_nombre_o_cedula", "legible", "confianza", "observacion" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    // Nombres en snake_case = los del esquema JSON que devuelve el modelo.
    private sealed record RespuestaIA(
        string? numero_remision, bool es_remision_jimaco, bool hay_firma_cliente, bool hay_sello_cliente, bool hay_nombre_o_cedula,
        bool legible, string? confianza, string? observacion);
}
