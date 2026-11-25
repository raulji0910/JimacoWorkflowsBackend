using Jimaco.Aprobaciones.Negocio.DTOs;
using Microsoft.Extensions.Configuration;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Modo <c>--documentos</c> (2026-11-25): creación de documentos de World Office desde el portal.
/// Corre como una Tarea Programada APARTE, cada minuto, para no esperar al ciclo de aprobaciones ni
/// frenarlo. Fase 0 (esta): mantiene al día en la Api el catálogo de WO que necesita el portal:
///  - productos: en cada corrida, solo los que cambiaron (VersionCol de Inventarios);
///  - existencias por bodega: foto completa cada <c>Documentos:MinutosExistencias</c> (5);
///  - bodegas, direcciones de clientes, formas de pago y "Más Datos": cada <c>Documentos:MinutosCatalogos</c> (60).
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
        var minutosCatalogos = int.TryParse(config["Documentos:MinutosCatalogos"], out var mc) ? mc : 60;
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

        // ---- Catálogos chicos (cada hora) ----
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

    private static bool Vencio(DateTime? ultimaVez, int minutos) =>
        ultimaVez is null || DateTime.UtcNow - DateTime.SpecifyKind(ultimaVez.Value, DateTimeKind.Utc) >= TimeSpan.FromMinutes(minutos) - TimeSpan.FromSeconds(20);
}
