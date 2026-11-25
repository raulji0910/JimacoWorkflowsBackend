using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Catálogo de World Office para crear documentos desde el portal (2026-11-25, Fase 0). El
/// Sincronizador (modo --documentos) sube productos (incremental por VersionCol), existencias
/// (foto completa) y los catálogos chicos (foto completa); el portal solo consulta.
/// </summary>
public class CatalogoWOService(AppDbContext db, TimeProvider reloj) : ICatalogoWOService
{
    private const int MaximoResultados = 30;
    private const string TipoDocumentoCotizacion = "COT";

    // ---------------- Sincronizador ----------------

    public async Task<EstadoCatalogoWODto> ObtenerEstadoAsync(CancellationToken ct = default)
    {
        var estado = await db.SincronizacionCatalogoWO.AsNoTracking().FirstOrDefaultAsync(ct);
        return estado is null
            ? new EstadoCatalogoWODto(0, null, null, null)
            : new EstadoCatalogoWODto(estado.UltimaVersionProductos, estado.FechaProductos, estado.FechaExistencias, estado.FechaCatalogos);
    }

    public async Task<int> GuardarProductosAsync(IReadOnlyList<ProductoWOInputDto> productos, CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var limpios = productos
            .Where(p => p.IdInventario > 0 && !string.IsNullOrWhiteSpace(p.Codigo))
            .GroupBy(p => p.IdInventario)
            .Select(g => g.MaxBy(p => p.Version)!)
            .ToList();

        var ids = limpios.Select(p => p.IdInventario).ToList();
        var existentes = await db.ProductosWO.Where(p => ids.Contains(p.IdInventarioWO)).ToDictionaryAsync(p => p.IdInventarioWO, ct);

        foreach (var entrada in limpios)
        {
            if (!existentes.TryGetValue(entrada.IdInventario, out var producto))
            {
                producto = new ProductoWO { IdInventarioWO = entrada.IdInventario, Codigo = "", Descripcion = "", UnidadMedida = "" };
                db.ProductosWO.Add(producto);
            }
            producto.Codigo = Recortar(entrada.Codigo, 50);
            producto.Descripcion = Recortar(entrada.Descripcion, 500);
            producto.UnidadMedida = Recortar(string.IsNullOrWhiteSpace(entrada.UnidadMedida) ? "Und." : entrada.UnidadMedida, 15);
            producto.Iva = entrada.Iva;
            producto.IdClasificacion = entrada.IdClasificacion;
            producto.Precio1 = entrada.Precio1;
            producto.Activo = entrada.Activo;
            producto.VersionWO = entrada.Version;
            producto.FechaActualizacion = ahora;
        }

        var estado = await ObtenerOCrearEstadoAsync(ct);
        if (limpios.Count > 0)
            estado.UltimaVersionProductos = Math.Max(estado.UltimaVersionProductos, limpios.Max(p => p.Version));
        estado.FechaProductos = ahora;
        await db.SaveChangesAsync(ct);
        return limpios.Count;
    }

    public async Task<int> GuardarExistenciasAsync(IReadOnlyList<ExistenciaWOInputDto> existencias, CancellationToken ct = default)
    {
        var limpias = existencias
            .Where(e => e.IdInventario > 0 && !string.IsNullOrWhiteSpace(e.IdBodega))
            .GroupBy(e => (e.IdInventario, Bodega: e.IdBodega.Trim()))
            .Select(g => new ExistenciaWO { IdInventarioWO = g.Key.IdInventario, IdBodegaWO = Recortar(g.Key.Bodega, 15), Existencia = g.Sum(e => e.Existencia) })
            .ToList();

        await using var transaccion = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await BorrarTodoAsync(db.ExistenciasWO, ct);
        db.ExistenciasWO.AddRange(limpias);
        var estado = await ObtenerOCrearEstadoAsync(ct);
        estado.FechaExistencias = reloj.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        if (transaccion is not null) await transaccion.CommitAsync(ct);
        return limpias.Count;
    }

    public async Task GuardarCatalogosAsync(CatalogosWOInputDto catalogos, CancellationToken ct = default)
    {
        await using var transaccion = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        await BorrarTodoAsync(db.BodegasWO, ct);
        db.BodegasWO.AddRange(catalogos.Bodegas
            .Where(b => !string.IsNullOrWhiteSpace(b.IdBodega))
            .DistinctBy(b => b.IdBodega.Trim())
            .Select(b => new BodegaWO { IdBodegaWO = Recortar(b.IdBodega, 15), Nombre = Recortar(string.IsNullOrWhiteSpace(b.Nombre) ? b.IdBodega : b.Nombre, 50), Activa = b.Activa }));

        await BorrarTodoAsync(db.DireccionesTerceroWO, ct);
        db.DireccionesTerceroWO.AddRange(catalogos.Direcciones
            .Where(d => d.IdDireccion > 0)
            .DistinctBy(d => d.IdDireccion)
            .Select(d => new DireccionTerceroWO
            {
                IdDireccionWO = d.IdDireccion,
                IdTerceroWO = d.IdTercero,
                Direccion = RecortarNulo(d.Direccion, 300),
                Telefonos = RecortarNulo(d.Telefonos, 100),
                Ciudad = RecortarNulo(d.Ciudad, 100),
                Principal = d.Principal
            }));

        await BorrarTodoAsync(db.FormasPagoWO, ct);
        db.FormasPagoWO.AddRange(catalogos.FormasPago
            .Where(f => f.IdFormaPago > 0)
            .DistinctBy(f => f.IdFormaPago)
            .Select(f => new FormaPagoWO { IdFormaPagoWO = f.IdFormaPago, Nombre = Recortar(string.IsNullOrWhiteSpace(f.Nombre) ? $"Forma de pago {f.IdFormaPago}" : f.Nombre, 100) }));

        await BorrarTodoAsync(db.CamposPersonalizadosWO, ct);
        db.CamposPersonalizadosWO.AddRange(catalogos.CamposPersonalizados
            .Where(c => !string.IsNullOrWhiteSpace(c.TipoDocumento) && c.Numero is >= 1 and <= 15)
            .DistinctBy(c => (c.TipoDocumento.Trim().ToUpperInvariant(), c.Numero))
            .Select(c => new CampoPersonalizadoWO
            {
                TipoDocumento = Recortar(c.TipoDocumento.ToUpperInvariant(), 10),
                Numero = c.Numero,
                Etiqueta = Recortar(string.IsNullOrWhiteSpace(c.Etiqueta) ? $"Personalizado {c.Numero}" : c.Etiqueta, 100),
                Activo = c.Activo
            }));

        var estado = await ObtenerOCrearEstadoAsync(ct);
        estado.FechaCatalogos = reloj.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        if (transaccion is not null) await transaccion.CommitAsync(ct);
    }

    // ---------------- Portal ----------------

    public async Task<CatalogoCotizacionDto> ObtenerCatalogoCotizacionAsync(int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);

        var bodegas = await db.BodegasWO.AsNoTracking().Where(b => b.Activa)
            .OrderBy(b => b.Nombre).Select(b => new BodegaDto(b.IdBodegaWO, b.Nombre)).ToListAsync(ct);
        var formasPago = await db.FormasPagoWO.AsNoTracking()
            .OrderBy(f => f.Nombre).Select(f => new FormaPagoDto(f.IdFormaPagoWO, f.Nombre)).ToListAsync(ct);
        var campos = await db.CamposPersonalizadosWO.AsNoTracking()
            .Where(c => c.TipoDocumento == TipoDocumentoCotizacion && c.Activo)
            .OrderBy(c => c.Numero).Select(c => new CampoMasDatosDto(c.Numero, c.Etiqueta)).ToListAsync(ct);

        return new CatalogoCotizacionDto(bodegas, formasPago, campos, await ObtenerEstadoAsync(ct));
    }

    /// <summary>
    /// Productos activos cuyo código o descripción contienen TODAS las palabras escritas (en cualquier
    /// orden: "az carta" encuentra "AZ TAMAÑO CARTA"). Primero el código exacto, después los que
    /// empiezan por lo escrito.
    /// </summary>
    public async Task<IReadOnlyList<ProductoCatalogoDto>> BuscarProductosAsync(string texto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var palabras = Palabras(texto);
        if (palabras.Count == 0) return [];

        var consulta = db.ProductosWO.AsNoTracking().Where(p => p.Activo);
        foreach (var palabra in palabras)
            consulta = consulta.Where(p => p.Codigo.ToUpper().Contains(palabra) || p.Descripcion.ToUpper().Contains(palabra));

        var buscado = texto.Trim().ToUpperInvariant();
        var productos = await consulta
            .OrderBy(p => p.Codigo.ToUpper() == buscado ? 0 : p.Codigo.ToUpper().StartsWith(buscado) ? 1 : 2)
            .ThenBy(p => p.Codigo)
            .Take(MaximoResultados)
            .ToListAsync(ct);

        var ids = productos.Select(p => p.IdInventarioWO).ToList();
        var bodegas = await db.BodegasWO.AsNoTracking().ToDictionaryAsync(b => b.IdBodegaWO, b => b.Nombre, ct);
        var existencias = (await db.ExistenciasWO.AsNoTracking().Where(e => ids.Contains(e.IdInventarioWO)).ToListAsync(ct))
            .ToLookup(e => e.IdInventarioWO);

        return productos.Select(p =>
        {
            var porBodega = existencias[p.IdInventarioWO]
                .OrderBy(e => e.IdBodegaWO == "Uno" ? 0 : 1).ThenBy(e => e.IdBodegaWO)
                .Select(e => new ExistenciaBodegaDto(e.IdBodegaWO, bodegas.GetValueOrDefault(e.IdBodegaWO, e.IdBodegaWO), e.Existencia))
                .ToList();
            return new ProductoCatalogoDto(p.IdInventarioWO, p.Codigo, p.Descripcion, p.UnidadMedida, p.Iva, p.IdClasificacion, p.Precio1,
                porBodega.Sum(e => e.Existencia), porBodega);
        }).ToList();
    }

    public async Task<IReadOnlyList<ClienteCatalogoDto>> BuscarClientesAsync(string texto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var palabras = Palabras(texto);
        if (palabras.Count == 0) return [];

        var buscado = texto.Trim();
        var consulta = db.TercerosWO.AsNoTracking().AsQueryable();
        foreach (var palabra in palabras)
            consulta = consulta.Where(t => t.Nombre.ToUpper().Contains(palabra) || t.Identificacion.StartsWith(palabra));

        return await consulta
            .OrderBy(t => t.Identificacion == buscado ? 0 : 1)
            .ThenBy(t => t.Nombre)
            .Take(MaximoResultados)
            .Select(t => new ClienteCatalogoDto(t.IdTerceroWO, t.Identificacion, t.Nombre))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DireccionClienteDto>> ListarDireccionesAsync(int idTercero, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        return await db.DireccionesTerceroWO.AsNoTracking()
            .Where(d => d.IdTerceroWO == idTercero)
            .OrderByDescending(d => d.Principal).ThenBy(d => d.IdDireccionWO)
            .Select(d => new DireccionClienteDto(d.IdDireccionWO, d.Direccion, d.Ciudad, d.Telefonos, d.Principal))
            .ToListAsync(ct);
    }

    // ---------------- Apoyo ----------------

    private async Task ExigirPermisoAsync(int usuarioId, CancellationToken ct)
    {
        var tienePermiso = await db.UsuarioRoles
            .AnyAsync(ur => ur.UsuarioId == usuarioId && ur.Usuario.Activo && ur.Rol.Activo && ur.Rol.PuedeCrearCotizaciones, ct);
        if (!tienePermiso)
            throw new UnauthorizedAccessException("No tenés permiso para crear cotizaciones.");
    }

    private async Task<SincronizacionCatalogoWO> ObtenerOCrearEstadoAsync(CancellationToken ct)
    {
        var estado = await db.SincronizacionCatalogoWO.FirstOrDefaultAsync(ct);
        if (estado is not null) return estado;
        estado = new SincronizacionCatalogoWO { Id = 1 };
        db.SincronizacionCatalogoWO.Add(estado);
        return estado;
    }

    private async Task BorrarTodoAsync<T>(DbSet<T> tabla, CancellationToken ct) where T : class
    {
        if (db.Database.IsRelational())
            await tabla.ExecuteDeleteAsync(ct);
        else
        {
            // Pruebas (InMemory): sin ExecuteDelete. Se guarda ya para que volver a agregar la misma
            // llave no choque con la fila borrada que sigue en el seguimiento.
            tabla.RemoveRange(await tabla.ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }
    }

    // En mayúsculas: SQL Server no distingue, pero así la búsqueda es igual en cualquier motor.
    private static List<string> Palabras(string? texto) =>
        (texto ?? "").ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0).Take(6).ToList();

    private static string Recortar(string texto, int largo)
    {
        var limpio = texto.Trim();
        return limpio.Length <= largo ? limpio : limpio[..largo];
    }

    private static string? RecortarNulo(string? texto, int largo) =>
        string.IsNullOrWhiteSpace(texto) ? null : Recortar(texto, largo);
}
