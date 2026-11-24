using System.Text.Json;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Microsoft.Extensions.Configuration;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Modo <c>--conciliacion</c> (2026-11-24): sube a la Api lo que necesita la conciliación de facturas
/// de proveedor — OC y FCJ de World Office + los XML del correo facturacion@jimaco.com.co — y sale.
/// Corre como una instalación y Tarea Programada APARTE del Sincronizador de aprobaciones (decisión
/// del usuario): si esto se demora o falla, las aprobaciones no se enteran; y su appsettings.Local.json
/// solo lleva el login de LECTURA de WO, el usuario de servicio y la clave de facturacion@.
///
/// <c>--conciliacion --carpeta "C:\ruta"</c>: carga única de ZIP/XML/EML de una carpeta (históricos).
///
/// Ventana: la primera corrida carga desde <c>Conciliacion:FechaInicial</c> (2026-01-01, pedido del
/// usuario: "cargar la información del 2026"); las siguientes, los últimos <c>DiasVentana</c> días (WO
/// no tiene fecha de modificación confiable y una OC se edita o anula después de creada: se manda la
/// ventana completa y la Api solo reescribe lo que cambió).
/// </summary>
public static class ConciliacionRunner
{
    private sealed record MarcaCargaInicial(DateOnly Desde, DateTime Fecha);

    public static async Task<int> EjecutarAsync(IConfiguration config, string[] args)
    {
        string Requerido(string clave) => config[clave] ?? throw new InvalidOperationException($"Falta configurar {clave}.");
        void Log(string texto) => Console.WriteLine($"[{DateTime.Now:s}] {texto}");

        using var http = new HttpClient { BaseAddress = new Uri(Requerido("Jimaco:ApiBaseUrl")), Timeout = TimeSpan.FromMinutes(10) };
        // Sin X-Empresa = Jimaco (la conciliación es solo de Jimaco; la Api la rechaza a otras empresas).
        var api = new JimacoAprobacionesClient(http, Requerido("Jimaco:UsuarioServicio:Email"), Requerido("Jimaco:UsuarioServicio:Password"));

        // ---- Carga única desde carpeta ----
        var indiceCarpeta = Array.IndexOf(args, "--carpeta");
        if (indiceCarpeta >= 0)
        {
            if (indiceCarpeta + 1 >= args.Length || !Directory.Exists(args[indiceCarpeta + 1]))
            {
                Console.WriteLine("Uso: Jimaco.Aprobaciones.Sincronizador.exe --conciliacion --carpeta \"C:\\ruta\\de\\la\\carpeta\"");
                return 1;
            }

            try
            {
                var carpeta = args[indiceCarpeta + 1];
                Log($"Leyendo facturas de la carpeta {carpeta}...");
                var xmls = CarpetaFacturasReader.Leer(carpeta);
                Log($"{xmls.Count} XML encontrados.");
                var (nuevas, duplicadas, noReconocidas, fallidas) = await SubirFacturasAsync(api, xmls, Log);
                Log($"Facturas: {nuevas} nuevas, {duplicadas} ya existían, {noReconocidas} XML no reconocidos" +
                    (fallidas > 0 ? $", {fallidas} no se pudieron subir." : "."));
                return 0;
            }
            catch (Exception ex)
            {
                Log($"ERROR cargando la carpeta: {ex.Message}");
                return 1;
            }
        }

        var huboError = false;
        var conexionWO = Requerido("WorldOffice:ConnectionString");
        var lector = new WorldOfficeConciliacionReader(conexionWO);

        // ---- Ventana: carga inicial (todo 2026) o últimos N días ----
        var rutaMarcaInicial = Path.Combine(AppContext.BaseDirectory, config["Conciliacion:RutaMarcaCargaInicial"] ?? "marca-conciliacion-inicial.json");
        var fechaInicial = DateOnly.TryParse(config["Conciliacion:FechaInicial"], out var fi) ? fi : new DateOnly(2026, 1, 1);
        var diasVentana = int.TryParse(config["Conciliacion:DiasVentana"], out var dv) ? dv : 150;
        var cargaInicialHecha = LeerMarcaInicial(rutaMarcaInicial)?.Desde <= fechaInicial;
        var ventana = DateOnly.FromDateTime(DateTime.Today).AddDays(-diasVentana);
        var desde = cargaInicialHecha || ventana < fechaInicial ? ventana : fechaInicial;
        if (!cargaInicialHecha && desde == fechaInicial)
            Log($"Carga inicial: OC y facturas de compra desde {fechaInicial:yyyy-MM-dd}.");
        var okOrdenes = false;
        var okFacturasCompra = false;

        // ---- Fase 1: OC ----
        try
        {
            var prefijo = config["Conciliacion:PrefijoOrdenCompra"] ?? "OC";
            Log($"Leyendo OC (prefijo {prefijo}) de World Office desde {desde:yyyy-MM-dd}...");
            var ordenes = await lector.ObtenerOrdenesCompraAsync(prefijo, desde);
            Log($"{ordenes.Count} OC leídas ({ordenes.Count(o => o.Anulada)} anuladas).");

            int nuevas = 0, actualizadas = 0;
            foreach (var lote in ordenes.Chunk(200))
            {
                var r = await api.SincronizarOrdenesConciliacionAsync(lote);
                nuevas += r.Nuevas;
                actualizadas += r.Actualizadas;
            }
            Log($"OC: {nuevas} nuevas, {actualizadas} actualizadas.");
            okOrdenes = true;
        }
        catch (Exception ex)
        {
            huboError = true;
            Log($"ERROR sincronizando OC: {ex.Message}");
        }

        // ---- Fase 2: facturas de compra de WO (FCJ) ----
        try
        {
            var prefijo = config["Conciliacion:PrefijoFacturaCompra"] ?? "FCJ";
            var facturasCompra = await lector.ObtenerFacturasCompraAsync(prefijo, desde);
            var r = await api.SincronizarFacturasCompraConciliacionAsync(new SincronizacionFacturasCompraEntradaDto(desde, facturasCompra));
            Log($"{facturasCompra.Count} {prefijo} leídas ({facturasCompra.Count(f => !string.IsNullOrWhiteSpace(f.ReferenciaOrdenes))} con OC): " +
                $"{r.Nuevas} nuevas, {r.Actualizadas} actualizadas, {r.Eliminadas} eliminadas.");
            okFacturasCompra = true;
        }
        catch (Exception ex)
        {
            huboError = true;
            Log($"ERROR sincronizando facturas de compra: {ex.Message}");
        }

        if (!cargaInicialHecha && okOrdenes && okFacturasCompra)
            File.WriteAllText(rutaMarcaInicial, JsonSerializer.Serialize(new MarcaCargaInicial(fechaInicial, DateTime.Now)));

        // ---- Fase 3: XML del correo de facturación ----
        // Solo lectura: NO marca nada como leído (Contabilidad usa ese buzón a mano). Se avanza la marca
        // de UID tanda por tanda (50 correos), solo después de que la Api confirmó.
        try
        {
            var correo = new CorreoFacturasWatcher(
                Requerido("CorreoFacturacion:Host"),
                int.TryParse(config["CorreoFacturacion:Puerto"], out var p) ? p : 993,
                Requerido("CorreoFacturacion:Usuario"),
                Requerido("CorreoFacturacion:Password"),
                config["CorreoFacturacion:Carpeta"]);
            var marcas = new MarcaCorreoStore(Path.Combine(AppContext.BaseDirectory,
                config["CorreoFacturacion:RutaMarca"] ?? "marca-correo-facturacion.json"));
            var desdeSinMarca = DateOnly.TryParse(config["CorreoFacturacion:DesdeFechaInicial"], out var df) ? df : fechaInicial;
            var maximo = int.TryParse(config["CorreoFacturacion:MaximoCorreosPorCorrida"], out var m) ? m : 300;

            var marca = marcas.Leer();
            Log($"Revisando bandeja de {config["CorreoFacturacion:Usuario"]} " +
                (marca is null ? $"desde {desdeSinMarca:yyyy-MM-dd} (primera corrida)..." : $"desde el UID {marca.UltimoUid}..."));

            int nuevas = 0, duplicadas = 0, noReconocidas = 0, fallidas = 0, conXml = 0, xmlsLeidos = 0;
            var revisados = await correo.RecorrerAsync(marca?.UidValidity, marca?.UltimoUid ?? 0, desdeSinMarca, maximo,
                async (uidValidity, correos) =>
                {
                    var xmls = correos.SelectMany(c => c.Xmls).ToList();
                    if (xmls.Count > 0)
                    {
                        var r = await SubirFacturasAsync(api, xmls, Log);
                        nuevas += r.Nuevas;
                        duplicadas += r.Duplicadas;
                        noReconocidas += r.NoReconocidas;
                        fallidas += r.Fallidas;
                    }
                    conXml += correos.Count(c => c.Xmls.Count > 0);
                    xmlsLeidos += xmls.Count;
                    // La marca avanza solo después de que la Api confirmó la tanda.
                    marcas.Guardar(new MarcaCorreoStore.Marca(uidValidity, correos.Max(c => c.Uid.Id)));
                    Log($"  ... UID {correos.Max(c => c.Uid.Id)}: {xmlsLeidos} XML leídos hasta ahora ({nuevas} facturas nuevas).");
                }, Log);
            Log($"{revisados} correo(s) revisado(s), {conXml} con XML, {xmlsLeidos} XML adjunto(s).");
            Log($"Facturas del correo: {nuevas} nuevas, {duplicadas} ya existían, {noReconocidas} XML no reconocidos" +
                (fallidas > 0 ? $", {fallidas} no se pudieron subir." : "."));
        }
        catch (Exception ex)
        {
            huboError = true;
            Log($"ERROR leyendo el correo de facturación: {ex.Message}");
        }

        return huboError ? 1 : 0;
    }

    private static async Task<(int Nuevas, int Duplicadas, int NoReconocidas, int Fallidas)> SubirFacturasAsync(
        JimacoAprobacionesClient api, IReadOnlyList<FacturaXmlEntradaDto> xmls, Action<string> log)
    {
        int nuevas = 0, duplicadas = 0, noReconocidas = 0, fallidas = 0;
        foreach (var lote in LotesPorTamano(xmls, 900_000))
        {
            try
            {
                var r = await api.RegistrarFacturasConciliacionAsync(lote);
                nuevas += r.Nuevas;
                duplicadas += r.Duplicadas;
                noReconocidas += r.NoReconocidas;
            }
            catch (HttpRequestException ex) when (lote.Count == 1)
            {
                // Un XML solo que el servidor no recibe (p. ej. más grande que el límite del proxy).
                fallidas++;
                log($"  {lote[0].NombreArchivo} no se pudo subir: {ex.Message.Split('\n')[0]}");
            }
        }
        return (nuevas, duplicadas, noReconocidas, fallidas);
    }

    // En el JSON cada <, > y comilla del XML sale escapado como \u003C (6 caracteres): se cuenta el
    // triple del XML para no pasarse del límite de tamaño del proxy.
    private static IEnumerable<List<FacturaXmlEntradaDto>> LotesPorTamano(IReadOnlyList<FacturaXmlEntradaDto> xmls, int maximoBytes)
    {
        var lote = new List<FacturaXmlEntradaDto>();
        var tamano = 0;
        foreach (var x in xmls)
        {
            var estimado = x.Xml.Length * 3;
            if (lote.Count > 0 && tamano + estimado > maximoBytes)
            {
                yield return lote;
                lote = [];
                tamano = 0;
            }
            lote.Add(x);
            tamano += estimado;
        }
        if (lote.Count > 0) yield return lote;
    }

    private static MarcaCargaInicial? LeerMarcaInicial(string ruta)
    {
        try
        {
            return File.Exists(ruta) ? JsonSerializer.Deserialize<MarcaCargaInicial>(File.ReadAllText(ruta)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
