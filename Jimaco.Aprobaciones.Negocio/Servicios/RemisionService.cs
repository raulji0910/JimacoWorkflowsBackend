using System.Security.Cryptography;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <inheritdoc cref="IRemisionService"/>
public class RemisionService(AppDbContext db, IAlmacenamientoArchivos almacenamiento, IRemisionPdfGenerador pdfGenerador,
    IConfiguration configuration, TimeProvider reloj, IVerificadorSoporteEntrega verificadorIA, IMemoryCache cache) : IRemisionService
{
    private const int MaxRemisionesPorPdf = 50;

    /// <summary>Horas por entregar desde que llegó para pasar a "Atrasada" (alerta roja y sonido).</summary>
    private double HorasUmbralAlerta =>
        double.TryParse(configuration["Entregas:HorasUmbralAlerta"], out var h) ? h : 24;

    private int DiasEntregadasVisibles =>
        int.TryParse(configuration["Entregas:DiasEntregadasVisibles"], out var d) ? d : 60;

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

    public async Task<IReadOnlyList<RemisionTableroDto>> ListarAsync(int usuarioId, CancellationToken ct = default)
    {
        await ExigirVerAsync(usuarioId, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var desdeEntregadas = ahora.AddDays(-DiasEntregadasVisibles);
        var umbral = HorasUmbralAlerta;

        var filas = await db.Remisiones.AsNoTracking()
            .Where(r => r.Estado == EstadoRemision.PorEntregar
                || (r.Estado == EstadoRemision.Entregada && r.FechaEntrega >= desdeEntregadas))
            .OrderBy(r => r.FechaSincronizacion).ThenBy(r => r.IdAsientoContableWO)
            .Select(r => new
            {
                r.Id, r.Numero, r.Fecha, r.Cliente, r.NitCliente, r.Ciudad, r.DireccionEntrega, r.CampoEntrega,
                r.Vendedor, r.Cotizacion, r.OrdenCompraCliente, Renglones = r.Renglones.Count, r.FechaSincronizacion,
                r.Estado, r.FechaEntrega,
                EntregadaPor = r.EntregadaPorUsuario != null ? r.EntregadaPorUsuario.Nombre : null,
                r.VerificacionQR
            })
            .ToListAsync(ct);

        return filas.Select(f => new RemisionTableroDto(
            f.Id, f.Numero, f.Fecha, f.Cliente, f.NitCliente, f.Ciudad, f.DireccionEntrega, f.CampoEntrega,
            f.Vendedor, f.Cotizacion, f.OrdenCompraCliente, f.Renglones, f.FechaSincronizacion, f.Estado.ToString(),
            f.Estado == EstadoRemision.PorEntregar && (ahora - f.FechaSincronizacion).TotalHours >= umbral,
            f.FechaEntrega, f.EntregadaPor, f.VerificacionQR?.ToString())).ToList();
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

    public async Task<VerificacionSoporteDto> VerificarSoporteAsync(int id, int usuarioId, ArchivoSoporteEntrega archivo, CancellationToken ct = default)
    {
        await ExigirGestionarAsync(usuarioId, ct);
        var remision = await db.Remisiones.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException("Remisión no encontrada.");
        var bytes = await LeerAsync(archivo.Contenido, ct);
        return await EvaluarAsync(remision, bytes, archivo, ct);
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

        // Primero se revisan TODOS (sin guardar nada): la entrega solo se confirma si al menos un
        // archivo es esta remisión y trae la firma del cliente (pedido del usuario 2026-11-12).
        var revisados = new List<(ArchivoSoporteEntrega Archivo, byte[] Bytes, VerificacionSoporteDto Resultado)>();
        foreach (var a in archivos)
        {
            var bytes = await LeerAsync(a.Contenido, ct);
            revisados.Add((a, bytes, await EvaluarAsync(remision, bytes, a, ct)));
        }

        if (!revisados.Any(r => r.Resultado.Valido))
        {
            var motivos = revisados.Select(r => $"{r.Archivo.NombreArchivo}: {r.Resultado.Motivo}");
            throw new InvalidOperationException(
                $"No se confirmó la entrega de la REM {remision.Numero}: ningún soporte es válido. " + string.Join(" · ", motivos));
        }

        foreach (var (a, bytes, resultado) in revisados)
        {
            using var copia = new MemoryStream(bytes, writable: false);
            var ruta = await almacenamiento.GuardarAsync($"remision-{remision.Numero}-{a.NombreArchivo}", copia, ct);
            remision.Soportes.Add(new SoporteRemision
            {
                Tipo = TipoSoporteRemision.EntregaFirmada,
                NombreArchivo = Recortar(a.NombreArchivo, 300) ?? "soporte",
                RutaArchivo = ruta,
                ContentType = Recortar(a.ContentType, 150),
                TamanoBytes = bytes.LongLength,
                QrLeido = Recortar(a.QrLeido?.Trim(), 200),
                IaNumeroLeido = resultado.IaAnalizado ? Recortar(resultado.IaNumeroLeido, 30) : null,
                IaHayFirma = resultado.IaAnalizado ? resultado.IaHayFirma : null,
                IaObservacion = resultado.IaAnalizado ? Recortar(resultado.IaObservacion, 500) : null,
                Valido = resultado.Valido,
                SubidoPorUsuarioId = usuarioId
            });
        }

        remision.Estado = EstadoRemision.Entregada;
        remision.FechaEntrega = DateTime.UtcNow;
        remision.EntregadaPorUsuarioId = usuarioId;
        remision.ObservacionEntrega = Recortar(observacion?.Trim(), 1000);
        remision.VerificacionQR = revisados.Any(r => r.Resultado.QrCoincide) ? VerificacionQRSoporte.Coincide : VerificacionQRSoporte.SinQR;
        await db.SaveChangesAsync(ct);

        return await ObtenerAsync(id, usuarioId, ct);
    }

    /// <summary>
    /// La regla de un archivo válido: (1) es ESTA remisión — el QR coincide, o la IA lee este número
    /// (un QR de OTRA remisión lo descarta siempre); y (2) con la IA activa, se ve la firma del
    /// cliente. Sin IA configurada se exige el QR. El resultado de la IA se guarda en memoria por
    /// archivo (hash) para no pagar dos veces la misma revisión (verificar y luego confirmar).
    /// </summary>
    private async Task<VerificacionSoporteDto> EvaluarAsync(Remision remision, byte[] bytes, ArchivoSoporteEntrega archivo, CancellationToken ct)
    {
        var qr = Normalizar(archivo.QrLeido);
        var qrCoincide = qr.Length > 0 && qr == Normalizar(CodigoQR(remision));
        var qrDeOtra = qr.Length > 0 && !qrCoincide;

        if (!verificadorIA.Activo)
        {
            return new VerificacionSoporteDto(qrCoincide,
                qrCoincide ? null : qrDeOtra
                    ? $"El código QR es de otra remisión ({archivo.QrLeido})."
                    : "No se pudo leer el código QR de esta remisión. Tomá la foto de la hoja completa, nítida y con el QR visible.",
                qrCoincide, false, false, null, false, false, null, null);
        }

        var clave = $"verif-soporte:{remision.Id}:{Convert.ToHexString(SHA256.HashData(bytes))}";
        if (!cache.TryGetValue(clave, out ResultadoVerificacionIA? ia) || ia is null)
        {
            ia = await verificadorIA.VerificarAsync(bytes, archivo.ContentType, ct);
            if (ia.Analizado)
                cache.Set(clave, ia, TimeSpan.FromMinutes(30));
        }

        string? motivo = null;
        if (qrDeOtra)
            motivo = $"El código QR es de otra remisión ({archivo.QrLeido}).";
        else if (!ia.Analizado)
            motivo = ia.Error;
        else if (!ia.Legible)
            motivo = "La imagen no se puede revisar (borrosa, oscura o cortada). Tomá otra foto.";
        else if (!qrCoincide && ia.NumeroLeido != remision.Numero)
            motivo = ia.NumeroLeido is null
                ? "No se reconoce el número de esta remisión en el documento."
                : $"El documento es la remisión {ia.NumeroLeido}, no la {remision.Numero}.";
        else if (!ia.HayFirma)
            motivo = "No se ve la firma del cliente en la remisión.";

        return new VerificacionSoporteDto(motivo is null, motivo, qrCoincide, true, ia.Analizado,
            ia.NumeroLeido, ia.HayFirma, ia.HayNombreOCedula, ia.Analizado ? ia.Confianza : null, ia.Observacion);
    }

    private static async Task<byte[]> LeerAsync(Stream contenido, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        await contenido.CopyToAsync(memoria, ct);
        return memoria.ToArray();
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
            s.SubidoPorUsuario?.Nombre ?? "", s.FechaCarga, s.Valido, s.IaNumeroLeido, s.IaHayFirma, s.IaObservacion)).ToList(),
        puedeGestionar);

    private static string? Recortar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s : s[..max]);
}
