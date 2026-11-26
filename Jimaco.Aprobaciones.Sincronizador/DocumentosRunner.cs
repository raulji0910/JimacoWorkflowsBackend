using Jimaco.Aprobaciones.Negocio.DTOs;
using Microsoft.Extensions.Configuration;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Modo <c>--documentos</c> (2026-11-25): creación de documentos de World Office desde el portal.
/// Corre como una Tarea Programada APARTE, cada minuto, para no esperar al ciclo de aprobaciones ni
/// frenarlo. Fase 0 (esta): mantiene al día en la Api el catálogo de WO que necesita el portal:
///  - productos: en cada corrida, solo los que cambiaron (VersionCol de Inventarios);
///  - existencias por bodega: foto completa cada <c>Documentos:MinutosExistencias</c> (5);
///  - clientes (terceros) cada <c>Documentos:MinutosClientes</c> (10) y bodegas, sucursales, formas de pago
///    y "Más Datos" cada <c>Documentos:MinutosCatalogos</c> (10).
/// Fase 1: además creará en WO las cotizaciones pendientes del portal.
/// </summary>
public static class DocumentosRunner
{
    private const int TamanoLoteProductos = 2000;

    public static async Task<int> EjecutarAsync(IConfiguration config, string[] args)
    {
        string Requerido(string clave) => config[clave] ?? throw new InvalidOperationException($"Falta configurar {clave}.");
        void Log(string texto) => Console.WriteLine($"[{DateTime.Now:s}] {texto}");

        var minutosExistencias = int.TryParse(config["Documentos:MinutosExistencias"], out var me) ? me : 5;
        // Catálogos chicos (sucursales, formas de pago, Más Datos) y clientes: cada 10 minutos (2026-11-25:
        // se crean clientes y sucursales en WO a cada rato y la vendedora los necesita para cotizar).
        var minutosCatalogos = int.TryParse(config["Documentos:MinutosCatalogos"], out var mc) ? mc : 10;
        var minutosClientes = int.TryParse(config["Documentos:MinutosClientes"], out var mcl) ? mcl : 10;
        // "--documentos --todo": manda todo ya, sin mirar cuándo fue la última vez (útil al instalar).
        var forzar = args.Contains("--todo");

        using var http = new HttpClient { BaseAddress = new Uri(Requerido("Jimaco:ApiBaseUrl")), Timeout = TimeSpan.FromMinutes(5) };
        // Sin X-Empresa = Jimaco (por ahora la creación de documentos es solo de Jimaco).
        var api = new JimacoAprobacionesClient(http, Requerido("Jimaco:UsuarioServicio:Email"), Requerido("Jimaco:UsuarioServicio:Password"));
        var lector = new WorldOfficeCatalogoReader(Requerido("WorldOffice:ConnectionString"));
        var huboError = false;

        EstadoCatalogoWODto estado;
        try
        {
            estado = await api.ObtenerEstadoCatalogoWOAsync();
        }
        catch (Exception ex)
        {
            Log($"ERROR consultando el estado del catálogo en la Api: {ex.Message}");
            return 1;
        }

        // ---- Cotizaciones del portal → WO (primero: es lo que la vendedora está esperando) ----
        if (!await CrearCotizacionesAsync(config, api, Log))
            huboError = true;

        // ---- Productos (incremental) ----
        try
        {
            var desde = forzar ? 0 : estado.UltimaVersionProductos;
            var productos = await lector.ObtenerProductosAsync(desde);
            foreach (var lote in productos.Chunk(TamanoLoteProductos))
                await api.EnviarProductosWOAsync(lote);
            if (productos.Count > 0 || desde == 0)
                Log($"Productos: {productos.Count} {(desde == 0 ? "(carga completa)" : "cambiados desde la última corrida")}.");
        }
        catch (Exception ex)
        {
            huboError = true;
            Log($"ERROR sincronizando productos: {ex.Message}");
        }

        // ---- Existencias (foto completa cada N minutos) ----
        if (forzar || Vencio(estado.FechaExistencias, minutosExistencias))
        {
            try
            {
                var inicio = DateTime.UtcNow;
                var existencias = await lector.ObtenerExistenciasAsync();
                var leidaEn = DateTime.UtcNow - inicio;
                await api.EnviarExistenciasWOAsync(existencias);
                Log($"Existencias: {existencias.Count} producto-bodega (la vista de WO tardó {leidaEn.TotalSeconds:0.0} s).");
            }
            catch (Exception ex)
            {
                huboError = true;
                Log($"ERROR sincronizando existencias: {ex.Message}");
            }
        }

        // ---- Clientes (Terceros de WO, foto completa) ----
        // El mismo catálogo TerceroWO que manda el Sincronizador de aprobaciones cada pocas horas (Cuentas
        // por pagar); acá se manda más seguido porque es la lista de clientes de la cotización.
        try
        {
            var ultimaClientes = await api.ObtenerFechaCorteTercerosAsync();
            if (forzar || Vencio(ultimaClientes, minutosClientes))
            {
                var terceros = await new WorldOfficeReader(Requerido("WorldOffice:ConnectionString")).ObtenerTercerosAsync();
                await api.EnviarTercerosAsync(terceros
                    .Where(t => !string.IsNullOrWhiteSpace(t.Identificacion))
                    .Select(t => new TerceroWOInputDto(t.IdTercero, t.Identificacion!.Trim(), t.NombreCompleto.Trim()))
                    .ToList());
                Log($"Clientes: {terceros.Count} terceros de WO.");
            }
        }
        catch (Exception ex)
        {
            huboError = true;
            Log($"ERROR sincronizando clientes: {ex.Message}");
        }

        // ---- Catálogos chicos (sucursales, formas de pago, Más Datos) ----
        if (forzar || Vencio(estado.FechaCatalogos, minutosCatalogos))
        {
            try
            {
                var catalogos = await lector.ObtenerCatalogosAsync();
                await api.EnviarCatalogosWOAsync(catalogos);
                Log($"Catálogos: {catalogos.Bodegas.Count} bodegas, {catalogos.Direcciones.Count} direcciones, " +
                    $"{catalogos.FormasPago.Count} formas de pago, {catalogos.CamposPersonalizados.Count} campos de \"Más Datos\".");
            }
            catch (Exception ex)
            {
                huboError = true;
                Log($"ERROR sincronizando catálogos: {ex.Message}");
            }
        }

        return huboError ? 1 : 0;
    }

    /// <summary>
    /// Crea en WO las cotizaciones enviadas desde el portal. Apagado salvo <c>Documentos:CrearCotizaciones</c>
    /// = true y una <c>WorldOffice:ConnectionStringEscritura</c> (la misma que usa la creación de CE).
    /// Devuelve false si hubo un error que no es de una cotización puntual (Api caída, WO sin conexión).
    /// </summary>
    private static async Task<bool> CrearCotizacionesAsync(IConfiguration config, JimacoAprobacionesClient api, Action<string> log)
    {
        // Freno de emergencia LOCAL: Documentos:CrearCotizaciones=false apaga la creación aunque en la página
        // esté encendida. Ausente = la decide la página (Administración → Config. de cotizaciones).
        if (bool.TryParse(config["Documentos:CrearCotizaciones"], out var activoLocal) && !activoLocal) return true;
        var conexionEscritura = config["WorldOffice:ConnectionStringEscritura"];

        ConfiguracionDocumentosWODto configuracion;
        try
        {
            configuracion = await api.ObtenerConfiguracionDocumentosWOAsync();
        }
        catch (Exception ex)
        {
            log($"ERROR leyendo la configuración de cotizaciones de la Api: {ex.Message}");
            return false;
        }
        if (!configuracion.CrearCotizacionesEnWorldOffice)
        {
            if (configuracion.CotizacionesEnCola > 0)
                log($"Hay {configuracion.CotizacionesEnCola} cotización(es) esperando, pero la creación en WO está APAGADA en la página (Administración → Config. de cotizaciones).");
            return true;
        }
        if (string.IsNullOrWhiteSpace(conexionEscritura))
        {
            log("La creación de cotizaciones está encendida en la página pero falta WorldOffice:ConnectionStringEscritura — no se crean cotizaciones.");
            return false;
        }
        var prefijo = configuracion.PrefijoCotizacion;

        try
        {
            var pendientes = await api.ListarCotizacionesPendientesAsync();
            if (pendientes.Count == 0) return true;

            var writer = new CotizacionWriter(conexionEscritura, new ConfiguracionCotizacion(
                prefijo.Trim(),
                string.IsNullOrWhiteSpace(config["Documentos:NomPc"]) ? "APROBACIONES" : config["Documentos:NomPc"]!.Trim(),
                string.IsNullOrWhiteSpace(config["Documentos:ColumnaMarcaCotizacion"]) ? "Personalizado8" : config["Documentos:ColumnaMarcaCotizacion"]!.Trim()));
            if (!await writer.PrefijoExisteAsync())
            {
                log($"El prefijo de cotización '{prefijo}' no tiene ninguna COT en World Office — revisar el prefijo en Administración → Config. de cotizaciones. No se crea nada.");
                return false;
            }

            foreach (var cotizacion in pendientes)
            {
                try
                {
                    var resultado = await writer.CrearAsync(cotizacion);
                    if (resultado.Resultado == ResultadoCreacionCotizacion.Conflicto)
                    {
                        log($"  Cotización #{cotizacion.CotizacionId}: WO no la aceptó — {resultado.Mensaje}");
                        await api.ReportarErrorCotizacionAsync(cotizacion.CotizacionId, resultado.Mensaje ?? "World Office no aceptó la cotización.");
                        continue;
                    }
                    await api.ConfirmarCotizacionCreadaAsync(cotizacion.CotizacionId,
                        new CotizacionCreadaDto(resultado.Prefijo!, resultado.Numero!.Value, resultado.IdAsientoContable!.Value, resultado.Fecha));
                    log($"  Cotización #{cotizacion.CotizacionId} -> {resultado.Prefijo} {resultado.Numero:0}" +
                        (resultado.Resultado == ResultadoCreacionCotizacion.YaExistia ? " (ya estaba creada, se avisó a la Api)." : " creada en WO."));
                }
                catch (Exception ex)
                {
                    // Error técnico (conexión, permisos): se deja PorCrear y se reintenta en la próxima corrida.
                    log($"  ERROR creando la cotización #{cotizacion.CotizacionId}: {ex.Message}");
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            log($"ERROR con las cotizaciones pendientes: {ex.Message}");
            return false;
        }
    }

    private static bool Vencio(DateTime? ultimaVez, int minutos) =>
        ultimaVez is null || DateTime.UtcNow - DateTime.SpecifyKind(ultimaVez.Value, DateTimeKind.Utc) >= TimeSpan.FromMinutes(minutos) - TimeSpan.FromSeconds(20);
}
