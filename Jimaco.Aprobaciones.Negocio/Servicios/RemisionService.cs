using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <inheritdoc cref="IRemisionService"/>
public class RemisionService(AppDbContext db, IAlmacenamientoArchivos almacenamiento, IRemisionPdfGenerador pdfGenerador)
    : IRemisionService
{
    private const int TamanoPaginaPorDefecto = 20;
    private const int MaxRemisionesPorPdf = 50;

    /// <summary>Lo que lleva el QR impreso — el navegador compara el QR leído del soporte contra esto.</summary>
    public static string CodigoQR(Remision r) => $"JIMACO-{r.Prefijo}-{r.Numero}";

    private static string Normalizar(string? qr) => (qr ?? "").Trim().ToUpperInvariant();

    // ---------------------------------------------------------------------------------------------
    // Sincronizador
    // ---------------------------------------------------------------------------------------------

    public async Task<EstadoSincronizacionRemisionesDto> ObtenerEstadoSincronizacionAsync(CancellationToken ct = default)
    {
        var fechaInicio = await FechaInicioAsync(ct);
        var ids = await db.Remisiones.AsNoTracking().Select(r => r.IdAsientoContableWO).ToListAsync(ct);
        return new EstadoSincronizacionRemisionesDto(fechaInicio, ids);
    }

    public async Task<ResultadoSincronizacionRemisionesDto> RegistrarAsync(IReadOnlyList<RemisionInputDto> remisiones, CancellationToken ct = default)
    {
        var fechaInicio = await FechaInicioAsync(ct);
        var ids = remisiones.Select(r => r.IdAsientoContableWO).Distinct().ToList();
        var existentes = (await db.Remisiones.Where(r => ids.Contains(r.IdAsientoContableWO))
            .Select(r => r.IdAsientoContableWO).ToListAsync(ct)).ToHashSet();

        var creadas = 0;
        foreach (var dto in remisiones.DistinctBy(r => r.IdAsientoContableWO))
        {
            // Defensa: nada anterior a la fecha de inicio, aunque el Sincronizador lo mande.
            if (existentes.Contains(dto.IdAsientoContableWO) || dto.Fecha.Date < fechaInicio.Date || dto.Renglones.Count == 0)
                continue;

            db.Remisiones.Add(new Remision
            {
                IdAsientoContableWO = dto.IdAsientoContableWO,
                Prefijo = dto.Prefijo,
                Numero = dto.Numero,
                Fecha = dto.Fecha,
                Cliente = Recortar(dto.Cliente, 300),
                NitCliente = Recortar(dto.NitCliente, 50),
                Direccion = Recortar(dto.Direccion, 300),
                Ciudad = Recortar(dto.Ciudad, 150),
                Telefono = Recortar(dto.Telefono, 150),
                Vendedor = Recortar(dto.Vendedor, 200),
                ElaboradoPorWO = Recortar(dto.ElaboradoPorWO, 100),
                FormaDePago = Recortar(dto.FormaDePago, 150),
                DireccionEntrega = Recortar(dto.DireccionEntrega, 300),
                CampoEntrega = Recortar(dto.CampoEntrega, 300),
                OrdenCompraCliente = Recortar(dto.OrdenCompraCliente, 150),
                Cotizacion = Recortar(dto.Cotizacion, 150),
                Nota = Recortar(dto.Nota, 1000),
                Renglones = dto.Renglones.Select((x, i) => new RenglonRemision
                {
                    Orden = i + 1,
                    Codigo = Recortar(x.Codigo, 50),
                    Descripcion = Recortar(x.Descripcion, 500) ?? "(sin descripción)",
                    Cantidad = x.Cantidad,
                    UnidadMedida = Recortar(x.UnidadMedida, 30)
                }).ToList()
            });
            creadas++;
        }

        await db.SaveChangesAsync(ct);
        return new ResultadoSincronizacionRemisionesDto(creadas, remisiones.Count - creadas);
    }

    public async Task MarcarAnuladasAsync(IReadOnlyList<int> idsAsientoContableWO, CancellationToken ct = default)
    {
        // Una ya entregada se deja como está: el soporte firmado existe y es un hecho real.
        var remisiones = await db.Remisiones
            .Where(r => idsAsientoContableWO.Contains(r.IdAsientoContableWO) && r.Estado == EstadoRemision.PorEntregar)
            .ToListAsync(ct);
        foreach (var r in remisiones) r.Estado = EstadoRemision.Anulada;
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------------------------------------
    // Página
    // ---------------------------------------------------------------------------------------------

    public async Task<RemisionesPaginaDto> ListarAsync(FiltroRemisionesDto filtro, int usuarioId, CancellationToken ct = default)
    {
        var (_, puedeGestionar) = await ExigirVerAsync(usuarioId, ct);

        var visibles = db.Remisiones.AsNoTracking().Where(r => r.Estado != EstadoRemision.Anulada);

        var texto = filtro.Buscar?.Trim();
        if (!string.IsNullOrEmpty(texto))
        {
            visibles = visibles.Where(r =>
                r.Numero.Contains(texto) ||
                (r.Cliente != null && r.Cliente.Contains(texto)) ||
                (r.NitCliente != null && r.NitCliente.Contains(texto)) ||
                (r.Ciudad != null && r.Ciudad.Contains(texto)) ||
                (r.DireccionEntrega != null && r.DireccionEntrega.Contains(texto)) ||
                (r.CampoEntrega != null && r.CampoEntrega.Contains(texto)) ||
                (r.Vendedor != null && r.Vendedor.Contains(texto)) ||
                (r.Cotizacion != null && r.Cotizacion.Contains(texto)) ||
                (r.OrdenCompraCliente != null && r.OrdenCompraCliente.Contains(texto)));
        }

        // Conteos con el filtro de texto aplicado, para los chips de estado.
        var porEntregar = await visibles.CountAsync(r => r.Estado == EstadoRemision.PorEntregar, ct);
        var entregadas = await visibles.CountAsync(r => r.Estado == EstadoRemision.Entregada, ct);
        var revisar = await visibles.CountAsync(r => r.Estado == EstadoRemision.Entregada
            && r.VerificacionQR != null && r.VerificacionQR != VerificacionQRSoporte.Coincide, ct);

        var consulta = (filtro.Estado ?? "PorEntregar") switch
        {
            "Entregada" => visibles.Where(r => r.Estado == EstadoRemision.Entregada),
            "Revisar" => visibles.Where(r => r.Estado == EstadoRemision.Entregada
                && r.VerificacionQR != null && r.VerificacionQR != VerificacionQRSoporte.Coincide),
            "Todas" => visibles,
            _ => visibles.Where(r => r.Estado == EstadoRemision.PorEntregar)
        };

        // Por entregar: la más vieja primero (lo urgente arriba). Entregadas: la más reciente primero
        // (para que Logística encuentre la que acaba de confirmar, igual que las HA recién completadas).
        consulta = (filtro.Estado ?? "PorEntregar") == "PorEntregar"
            ? consulta.OrderBy(r => r.Fecha).ThenBy(r => r.IdAsientoContableWO)
            : consulta.OrderByDescending(r => r.FechaEntrega ?? r.FechaSincronizacion).ThenByDescending(r => r.IdAsientoContableWO);

        var tamano = Math.Clamp(filtro.TamanoPagina ?? TamanoPaginaPorDefecto, 1, 100);
        var total = await consulta.CountAsync(ct);
        var pagina = Math.Max(1, filtro.Pagina);

        var filas = await consulta
            .Skip((pagina - 1) * tamano).Take(tamano)
            .Select(r => new
            {
                r.Id, r.Numero, r.Fecha, r.Cliente, r.NitCliente, r.Ciudad, r.DireccionEntrega, r.CampoEntrega,
                r.Vendedor, r.Cotizacion, r.OrdenCompraCliente, r.Estado, r.FechaEntrega,
                EntregadaPor = r.EntregadaPorUsuario != null ? r.EntregadaPorUsuario.Nombre : null,
                r.VerificacionQR,
                Renglones = r.Renglones.Count,
                Soportes = r.Soportes.Count
            })
            .ToListAsync(ct);

        var items = filas.Select(f => new RemisionResumenDto(
            f.Id, f.Numero, f.Fecha, f.Cliente, f.NitCliente, f.Ciudad, f.DireccionEntrega, f.CampoEntrega,
            f.Vendedor, f.Cotizacion, f.OrdenCompraCliente, f.Estado.ToString(), f.FechaEntrega, f.EntregadaPor,
            f.VerificacionQR?.ToString(), f.Renglones, f.Soportes)).ToList();

        return new RemisionesPaginaDto(items, total, pagina, tamano, porEntregar, entregadas, revisar, puedeGestionar);
    }

    public async Task<RemisionDetalleDto> ObtenerAsync(int id, int usuarioId, CancellationToken ct = default)
    {
        var (_, puedeGestionar) = await ExigirVerAsync(usuarioId, ct);
        var remision = await CargarAsync(id, ct);
        return ADetalle(remision, puedeGestionar);
    }

    public async Task<byte[]> GenerarPdfAsync(IReadOnlyList<int> ids, int usuarioId, CancellationToken ct = default)
    {
        var (_, puedeGestionar) = await ExigirVerAsync(usuarioId, ct);
        var unicos = ids.Distinct().Take(MaxRemisionesPorPdf).ToList();
        if (unicos.Count == 0)
            throw new InvalidOperationException("Elegí al menos una remisión para imprimir.");

        var remisiones = await db.Remisiones.AsNoTracking()
            .Include(r => r.Renglones).Include(r => r.EntregadaPorUsuario)
            .Include(r => r.Soportes).ThenInclude(s => s.SubidoPorUsuario)
            .Where(r => unicos.Contains(r.Id))
            .ToListAsync(ct);
        if (remisiones.Count == 0)
            throw new KeyNotFoundException("No se encontraron las remisiones.");

        // En el mismo orden en que se pidieron.
        var orden = unicos.Select((x, i) => (x, i)).ToDictionary(t => t.x, t => t.i);
        return pdfGenerador.Generar(remisiones.OrderBy(r => orden[r.Id]).Select(r => ADetalle(r, puedeGestionar)).ToList());
    }

    public async Task<RemisionDetalleDto> ConfirmarEntregaAsync(int id, int usuarioId, string? observacion,
        IReadOnlyList<ArchivoSoporteEntrega> archivos, CancellationToken ct = default)
    {
        await ExigirGestionarAsync(usuarioId, ct);
        if (archivos.Count == 0)
            throw new InvalidOperationException("Adjuntá la foto o el escaneo de la remisión firmada por el cliente.");

        var remision = await CargarAsync(id, ct);
        if (remision.Estado == EstadoRemision.Entregada)
            throw new InvalidOperationException($"La remisión {remision.Numero} ya se confirmó como entregada.");
        if (remision.Estado == EstadoRemision.Anulada)
            throw new InvalidOperationException($"La remisión {remision.Numero} se anuló en World Office.");

        foreach (var a in archivos)
        {
            var ruta = await almacenamiento.GuardarAsync($"remision-{remision.Numero}-{a.NombreArchivo}", a.Contenido, ct);
            remision.Soportes.Add(new SoporteRemision
            {
                Tipo = TipoSoporteRemision.EntregaFirmada,
                NombreArchivo = Recortar(a.NombreArchivo, 300) ?? "soporte",
                RutaArchivo = ruta,
                ContentType = Recortar(a.ContentType, 150),
                TamanoBytes = a.TamanoBytes,
                QrLeido = Recortar(a.QrLeido?.Trim(), 200),
                SubidoPorUsuarioId = usuarioId
            });
        }

        remision.Estado = EstadoRemision.Entregada;
        remision.FechaEntrega = DateTime.UtcNow;
        remision.EntregadaPorUsuarioId = usuarioId;
        remision.ObservacionEntrega = Recortar(observacion?.Trim(), 1000);
        remision.VerificacionQR = Verificar(remision);
        await db.SaveChangesAsync(ct);

        return await ObtenerAsync(id, usuarioId, ct);
    }

    /// <summary>
    /// Coincide si ALGÚN soporte trae el QR de esta remisión; NoCoincide si se leyó un QR pero de
    /// otra; SinQR si no se leyó ninguno.
    /// </summary>
    internal static VerificacionQRSoporte Verificar(Remision r)
    {
        var esperado = Normalizar(CodigoQR(r));
        var leidos = r.Soportes.Where(s => s.Tipo == TipoSoporteRemision.EntregaFirmada && !string.IsNullOrWhiteSpace(s.QrLeido))
            .Select(s => Normalizar(s.QrLeido)).ToList();
        if (leidos.Count == 0) return VerificacionQRSoporte.SinQR;
        return leidos.Contains(esperado) ? VerificacionQRSoporte.Coincide : VerificacionQRSoporte.NoCoincide;
    }

    public async Task<(Stream Contenido, string NombreArchivo, string? ContentType)> DescargarSoporteAsync(int soporteId, int usuarioId, CancellationToken ct = default)
    {
        await ExigirVerAsync(usuarioId, ct);
        var soporte = await db.SoportesRemision.AsNoTracking().FirstOrDefaultAsync(s => s.Id == soporteId, ct)
            ?? throw new KeyNotFoundException("Soporte no encontrado.");
        var contenido = await almacenamiento.AbrirAsync(soporte.RutaArchivo, ct);
        return (contenido, soporte.NombreArchivo, soporte.ContentType);
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<DateTime> FechaInicioAsync(CancellationToken ct)
    {
        var config = await db.ConfiguracionesEntregas.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == ConfiguracionEntregas.IdUnico, ct);
        // Sin fila (base sin migrar, pruebas): hoy — nunca traer lo viejo por accidente.
        return config?.FechaInicio.Date ?? DateTime.Today;
    }

    private async Task<Remision> CargarAsync(int id, CancellationToken ct) =>
        await db.Remisiones
            .Include(r => r.Renglones)
            .Include(r => r.EntregadaPorUsuario)
            .Include(r => r.Soportes).ThenInclude(s => s.SubidoPorUsuario)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new KeyNotFoundException("Remisión no encontrada.");

    private async Task<(bool Ver, bool Gestionar)> PermisosAsync(int usuarioId, CancellationToken ct)
    {
        var roles = await db.UsuarioRoles.AsNoTracking()
            .Where(ur => ur.UsuarioId == usuarioId && ur.Rol.Activo && ur.Usuario.Activo)
            .Select(ur => new { ur.Rol.PuedeGestionarEntregas, ur.Rol.PuedeVerEntregas })
            .ToListAsync(ct);
        var gestionar = roles.Any(r => r.PuedeGestionarEntregas);
        return (gestionar || roles.Any(r => r.PuedeVerEntregas), gestionar);
    }

    private async Task<(bool Ver, bool Gestionar)> ExigirVerAsync(int usuarioId, CancellationToken ct)
    {
        var permisos = await PermisosAsync(usuarioId, ct);
        if (!permisos.Ver)
            throw new UnauthorizedAccessException("No tenés permiso para ver las entregas.");
        return permisos;
    }

    private async Task ExigirGestionarAsync(int usuarioId, CancellationToken ct)
    {
        var (_, gestionar) = await PermisosAsync(usuarioId, ct);
        if (!gestionar)
            throw new UnauthorizedAccessException("No tenés permiso para confirmar entregas.");
    }

    private static RemisionDetalleDto ADetalle(Remision r, bool puedeGestionar) => new(
        r.Id, r.Prefijo, r.Numero, r.Fecha, r.Cliente, r.NitCliente, r.Direccion, r.Ciudad, r.Telefono,
        r.Vendedor, r.ElaboradoPorWO, r.FormaDePago, r.DireccionEntrega, r.CampoEntrega, r.OrdenCompraCliente,
        r.Cotizacion, r.Nota, r.Estado.ToString(), r.FechaEntrega, r.EntregadaPorUsuario?.Nombre,
        r.ObservacionEntrega, r.VerificacionQR?.ToString(), CodigoQR(r),
        r.Renglones.OrderBy(x => x.Orden).Select(x => new RenglonRemisionDto(x.Orden, x.Codigo, x.Descripcion, x.Cantidad, x.UnidadMedida)).ToList(),
        r.Soportes.OrderBy(s => s.FechaCarga).Select(s => new SoporteRemisionDto(
            s.Id, s.Tipo.ToString(), s.NombreArchivo, s.ContentType, s.TamanoBytes, s.QrLeido,
            s.SubidoPorUsuario?.Nombre ?? "", s.FechaCarga)).ToList(),
        puedeGestionar);

    private static string? Recortar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s : s[..max]);
}
