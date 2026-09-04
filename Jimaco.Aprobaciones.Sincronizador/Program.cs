using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Sincronizador;
using Microsoft.Extensions.Configuration;

// Pensado para correr como una Tarea Programada de Windows en la red local de World Office
// (cada N minutos) — una corrida = un pase completo de "traer lo nuevo" y salir. No es un
// servicio de larga duración; ver CLAUDE.md de este proyecto para el porqué.

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Local.json", optional: true) // credenciales reales, nunca commiteado
    .AddEnvironmentVariables()
    .Build();

var connectionStringWorldOffice = config["WorldOffice:ConnectionString"]
    ?? throw new InvalidOperationException("Falta configurar WorldOffice:ConnectionString.");
var jimacoApiUrl = config["Jimaco:ApiBaseUrl"]
    ?? throw new InvalidOperationException("Falta configurar Jimaco:ApiBaseUrl.");
var jimacoEmail = config["Jimaco:UsuarioServicio:Email"]
    ?? throw new InvalidOperationException("Falta configurar Jimaco:UsuarioServicio:Email.");
var jimacoPassword = config["Jimaco:UsuarioServicio:Password"]
    ?? throw new InvalidOperationException("Falta configurar Jimaco:UsuarioServicio:Password.");
var tipoDocumentoOrdenCompraId = int.Parse(config["Jimaco:TipoDocumentoOrdenCompraId"]
    ?? throw new InvalidOperationException("Falta configurar Jimaco:TipoDocumentoOrdenCompraId."));
var rutaMarcaDeAgua = config["Sincronizador:RutaMarcaDeAgua"] ?? "marca-de-agua.json";

var lector = new WorldOfficeReader(connectionStringWorldOffice);
var marcaDeAgua = new WatermarkStore(rutaMarcaDeAgua);

using var http = new HttpClient { BaseAddress = new Uri(jimacoApiUrl) };
var jimaco = new JimacoAprobacionesClient(http, jimacoEmail, jimacoPassword);

var ultimoIdProcesado = marcaDeAgua.Leer();
Console.WriteLine($"[{DateTime.Now:s}] Buscando OC nuevas desde IdAsientoContable > {ultimoIdProcesado}...");

var ocNuevas = await lector.ObtenerOcNuevasAsync(ultimoIdProcesado);
Console.WriteLine($"[{DateTime.Now:s}] Encontradas {ocNuevas.Count} OC nuevas.");

foreach (var oc in ocNuevas)
{
    try
    {
        var proveedor = oc.IdTerceroExterno is int idProveedor
            ? await lector.ObtenerTerceroAsync(idProveedor)
            : null;
        var elaboradoPor = oc.IdTerceroInterno is int idElaborador
            ? await lector.ObtenerTerceroAsync(idElaborador)
            : null;
        var valorTotal = await lector.ObtenerValorTotalAsync(oc.IdAsientoContable);

        var datos = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(oc.NumDocumentoExterno))
            datos["cotizacionOrigen"] = oc.NumDocumentoExterno;
        if (!string.IsNullOrWhiteSpace(elaboradoPor?.NombreCompleto))
            datos["elaboradoPor"] = elaboradoPor.NombreCompleto;

        var dto = new CrearInstanciaDocumentoDto(
            TipoDocumentoId: tipoDocumentoOrdenCompraId,
            NumeroReferencia: oc.DocumentoNumero.ToString("0"),
            Proveedor: proveedor?.NombreCompleto,
            Valor: valorTotal,
            FechaDocumento: oc.Fecha,
            Datos: datos.Count > 0 ? datos : null);

        var creado = await jimaco.CrearDocumentoAsync(dto);
        Console.WriteLine($"[{DateTime.Now:s}]   OC {dto.NumeroReferencia} -> creada como documento #{creado.Id} ({proveedor?.NombreCompleto ?? "proveedor desconocido"}, ${valorTotal:N0})");

        // Solo avanza la marca de agua tras un éxito confirmado — si algo falla más adelante en
        // el lote, la próxima corrida vuelve a intentar desde la última que sí quedó guardada.
        marcaDeAgua.Guardar(oc.IdAsientoContable);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR procesando OC {oc.DocumentoNumero} (IdAsientoContable={oc.IdAsientoContable}): {ex.Message}");
        Console.Error.WriteLine("Deteniendo el lote acá — la próxima corrida reintenta desde este mismo punto.");
        Environment.ExitCode = 1;
        break;
    }
}

Console.WriteLine($"[{DateTime.Now:s}] Listo.");
