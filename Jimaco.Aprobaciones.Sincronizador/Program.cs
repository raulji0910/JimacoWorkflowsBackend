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
var rutaMarcaDeAgua = config["Sincronizador:RutaMarcaDeAgua"] ?? "marca-de-agua.json";

var lector = new WorldOfficeReader(connectionStringWorldOffice);
var marcaDeAgua = new WatermarkStore(rutaMarcaDeAgua);

using var http = new HttpClient { BaseAddress = new Uri(jimacoApiUrl) };
var jimaco = new JimacoAprobacionesClient(http, jimacoEmail, jimacoPassword);

// Qué prefijos de World Office importan, y a qué TipoDocumento de Jimaco Aprobaciones corresponde
// cada uno, sale de la configuración real (admin → Tipos de documento → "Prefijo en World
// Office") — nada hardcodeado acá. Un prefijo de pruebas sin TipoDocumento configurado
// simplemente no se sincroniza, en vez de mezclarse por accidente con el real.
var tiposDocumento = await jimaco.ListarTiposDocumentoAsync();
// OJO si agregás un prefijo nuevo más adelante: la marca de agua es global (un solo
// IdAsientoContable "hasta acá ya se revisó todo"), así que documentos viejos de un prefijo
// recién agregado, anteriores a la marca actual, no se van a traer solos — hace falta un
// backfill puntual (bajar la marca de agua a mano, o correr el prefijo nuevo con un query
// aparte) la primera vez.
var tipoIdPorPrefijo = tiposDocumento
    .Where(t => t.Activo && !string.IsNullOrWhiteSpace(t.PrefijoWorldOffice))
    .ToDictionary(t => t.PrefijoWorldOffice, t => t.Id);

Console.WriteLine($"[{DateTime.Now:s}] Prefijos configurados: {string.Join(", ", tipoIdPorPrefijo.Keys)}");

var ultimoIdProcesado = marcaDeAgua.Leer();
Console.WriteLine($"[{DateTime.Now:s}] Buscando documentos nuevos desde IdAsientoContable > {ultimoIdProcesado}...");

var documentosNuevos = await lector.ObtenerDocumentosNuevosAsync(ultimoIdProcesado, tipoIdPorPrefijo.Keys.ToList());
Console.WriteLine($"[{DateTime.Now:s}] Encontrados {documentosNuevos.Count} documento(s) nuevo(s).");

foreach (var oc in documentosNuevos)
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
            TipoDocumentoId: tipoIdPorPrefijo[oc.Prefijo],
            NumeroReferencia: oc.DocumentoNumero.ToString("0"),
            Proveedor: proveedor?.NombreCompleto,
            Valor: valorTotal,
            FechaDocumento: oc.Fecha,
            Datos: datos.Count > 0 ? datos : null,
            IdAsientoContableOrigen: oc.IdAsientoContable,
            PrefijoOrigen: oc.Prefijo);

        var creado = await jimaco.CrearDocumentoAsync(dto);
        Console.WriteLine($"[{DateTime.Now:s}]   {oc.Prefijo} {dto.NumeroReferencia} -> creado como documento #{creado.Id} ({proveedor?.NombreCompleto ?? "proveedor desconocido"}, ${valorTotal:N0})");

        // Solo avanza la marca de agua tras un éxito confirmado — si algo falla más adelante en
        // el lote, la próxima corrida vuelve a intentar desde la última que sí quedó guardada.
        marcaDeAgua.Guardar(oc.IdAsientoContable);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR procesando {oc.Prefijo} {oc.DocumentoNumero} (IdAsientoContable={oc.IdAsientoContable}): {ex.Message}");
        Console.Error.WriteLine("Deteniendo el lote acá — la próxima corrida reintenta desde este mismo punto.");
        Environment.ExitCode = 1;
        break;
    }
}

// ---- Fase 2: escribir de vuelta en World Office las aprobaciones del primer paso ----
// Opt-in a propósito: si no está configurada la connection string de escritura, se omite esta
// fase entera (permite seguir usando el Sincronizador solo para lectura hasta que WO tenga
// listo un login con permiso de escritura — ver exploracion-worldoffice.sql).
var connectionStringEscritura = config["WorldOffice:ConnectionStringEscritura"];
if (string.IsNullOrWhiteSpace(connectionStringEscritura))
{
    Console.WriteLine($"[{DateTime.Now:s}] WorldOffice:ConnectionStringEscritura no configurado — se omite la escritura de vuelta a WO.");
}
else
{
    var escritor = new WorldOfficeWriter(connectionStringEscritura);

    Console.WriteLine($"[{DateTime.Now:s}] Revisando aprobaciones pendientes de reflejar en World Office...");
    var pendientes = await jimaco.ListarPendientesEscrituraWOAsync();
    Console.WriteLine($"[{DateTime.Now:s}] {pendientes.Count} documento(s) pendiente(s) de escribir en WO.");

    foreach (var p in pendientes)
    {
        try
        {
            var resultado = await escritor.AprobarPrimerPasoAsync(p.PrefijoOrigen, p.IdAsientoContableOrigen, p.UsuarioWO);

            switch (resultado)
            {
                case ResultadoEscrituraWO.Escrito:
                case ResultadoEscrituraWO.YaEstabaAprobado:
                    await jimaco.ConfirmarEscrituraWOAsync(p.InstanciaDocumentoId);
                    Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} ({p.NumeroReferencia}) -> {resultado} en WO.");
                    break;

                case ResultadoEscrituraWO.Anulado:
                    await jimaco.ReportarConflictoWOAsync(p.InstanciaDocumentoId,
                        "El documento ya estaba anulado en World Office al momento de sincronizar la aprobación.");
                    Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} -> CONFLICTO: anulado en WO, no se escribió nada.");
                    break;

                case ResultadoEscrituraWO.NoEncontrado:
                    await jimaco.ReportarConflictoWOAsync(p.InstanciaDocumentoId,
                        $"No se encontró en World Office la fila prefijo={p.PrefijoOrigen} IdAsientoContable={p.IdAsientoContableOrigen}.");
                    Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} -> CONFLICTO: no encontrado en WO.");
                    break;
            }
        }
        catch (Exception ex)
        {
            // A diferencia del lote de OC nuevas (que se detiene entero ante un error), acá cada
            // documento pendiente es independiente — uno que falla no bloquea a los demás, y
            // sigue en la cola para que la próxima corrida lo reintente solo.
            Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR escribiendo en WO el documento #{p.InstanciaDocumentoId}: {ex.Message}");
        }
    }
}

Console.WriteLine($"[{DateTime.Now:s}] Listo.");
