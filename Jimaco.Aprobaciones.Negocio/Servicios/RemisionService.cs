using System.Security.Cryptography;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <inheritdoc cref="IRemisionService"/>
public partial class RemisionService(AppDbContext db, IAlmacenamientoArchivos almacenamiento, IRemisionPdfGenerador pdfGenerador,
    IConfiguration configuration, TimeProvider reloj, IVerificadorSoporteEntrega verificadorIA, IMemoryCache cache,
    INotificacionService notificaciones, ILogger<RemisionService> logger) : IRemisionService
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
        var permisos = await PermisosAsync(usuarioId, ct);
        if (!permisos.VerTodas && !(permisos.Documentar && permisos.UsuarioWO is not null))
            throw new UnauthorizedAccessException("No tenés permiso para ver las entregas.");
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var desdeEntregadas = ahora.AddDays(-DiasEntregadasVisibles);
        var umbral = HorasUmbralAlerta;

        // Las entregadas salen a los 60 días... salvo que la facturación siga abierta (fase 2).
        var consulta = db.Remisiones.AsNoTracking()
            .Where(r => r.Estado == EstadoRemision.PorEntregar
                || (r.Estado == EstadoRemision.Entregada
                    && (r.FechaEntrega >= desdeEntregadas || r.EstadoFacturacion != EstadoFacturacionRemision.Facturada)));
        // El emisor sin "ver todas" solo ve las suyas (las que elaboró en WO).
        if (!permisos.VerTodas)
        {
            var wo = permisos.UsuarioWO!.ToUpper();
            consulta = consulta.Where(r => r.ElaboradoPorWO != null && r.ElaboradoPorWO.ToUpper() == wo);
        }

        var filas = await consulta
            .OrderBy(r => r.FechaSincronizacion).ThenBy(r => r.IdAsientoContableWO)
            .Select(r => new
            {
                r.Id, r.Numero, r.Fecha, r.Cliente, r.NitCliente, r.Ciudad, r.DireccionEntrega, r.CampoEntrega,
                r.Vendedor, r.Cotizacion, r.OrdenCompraCliente, Renglones = r.Renglones.Count, r.FechaSincronizacion,
                r.Estado, r.FechaEntrega,
                EntregadaPor = r.EntregadaPorUsuario != null ? r.EntregadaPorUsuario.Nombre : null,
                r.VerificacionQR, r.ElaboradoPorWO, r.EstadoFacturacion, r.FechaEnvioFacturacion,
                TiposAdjuntos = r.Soportes.Where(x => x.Tipo == TipoSoporteRemision.DocumentoFacturacion)
                    .Select(x => x.TipoDocumentoFacturacionId).ToList(),
                ConAviso = r.Soportes.Count(x => x.Tipo == TipoSoporteRemision.DocumentoFacturacion && x.Aviso != null)
            })
            .ToListAsync(ct);

        var tipos = await TiposActivosAsync(ct);
        return filas.Select(f => new RemisionTableroDto(
            f.Id, f.Numero, f.Fecha, f.Cliente, f.NitCliente, f.Ciudad, f.DireccionEntrega, f.CampoEntrega,
            f.Vendedor, f.Cotizacion, f.OrdenCompraCliente, f.Renglones, f.FechaSincronizacion, f.Estado.ToString(),
            f.Estado == EstadoRemision.PorEntregar && (ahora - f.FechaSincronizacion).TotalHours >= umbral,
            f.FechaEntrega, f.EntregadaPor, f.VerificacionQR?.ToString(),
            f.EstadoFacturacion?.ToString(),
            f.EstadoFacturacion is null ? 0 : TiposQueAplican(tipos, f.NitCliente)
                .Count(t => t.Obligatorio && !f.TiposAdjuntos.Contains(t.Id)),
            f.ConAviso,
            permisos.EsEmisor(f.ElaboradoPorWO),
            f.FechaEnvioFacturacion)).ToList();
    }

    public async Task<RemisionDetalleDto> ObtenerAsync(int id, int usuarioId, CancellationToken ct = default)
    {
        var permisos = await PermisosAsync(usuarioId, ct);
        var remision = await CargarAsync(id, ct);
        ExigirVerRemision(permisos, remision);
        return await ADetalleAsync(remision, permisos, ct);
    }

    public async Task<byte[]> GenerarPdfAsync(IReadOnlyList<int> ids, int usuarioId, CancellationToken ct = default)
    {
        var permisos = await PermisosAsync(usuarioId, ct);
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

        foreach (var r in remisiones) ExigirVerRemision(permisos, r);

        // En el mismo orden en que se pidieron.
        var orden = unicos.Select((x, i) => (x, i)).ToDictionary(t => t.x, t => t.i);
        var detalles = new List<RemisionDetalleDto>();
        foreach (var r in remisiones.OrderBy(r => orden[r.Id]))
            detalles.Add(await ADetalleAsync(r, permisos, ct));
        return pdfGenerador.Generar(detalles);
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
        // archivo es esta remisión y trae la firma o el sello del cliente (pedido del usuario 2026-11-12).
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
                IaHaySello = resultado.IaAnalizado ? resultado.IaHaySello : null,
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
        remision.EstadoFacturacion = EstadoFacturacionRemision.PendienteDocumentos;
        await db.SaveChangesAsync(ct);

        await NotificarEntregaAlEmisorAsync(remision, usuarioId, ct);
        return await ObtenerAsync(id, usuarioId, ct);
    }

    /// <summary>
    /// La regla de un archivo válido: (1) es ESTA remisión — el QR coincide, o la IA lee este número
    /// (un QR de OTRA remisión lo descarta siempre); y (2) con la IA activa, se ve la firma o el
    /// sello del cliente (muchos clientes firman y sellan; solo el sello también vale). Sin IA configurada se exige el QR. El resultado de la IA se guarda en memoria por
    /// archivo (hash) para no pagar dos veces la misma revisión (verificar y luego confirmar).
    /// </summary>
    private async Task<VerificacionSoporteDto> EvaluarAsync(Remision remision, byte[] bytes, ArchivoSoporteEntrega archivo, CancellationToken ct)
    {
        var qr = Normalizar(archivo.QrLeido);
        var qrCoincide = qr.Length > 0 && qr == Normalizar(CodigoQR(remision));
        var qrDeOtra = qr.Length > 0 && !qrCoincide;

        if (!await IaEncendidaAsync(ct))
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
        else if (!ia.HayFirmaOSello)
            motivo = "No se ve la firma ni el sello del cliente en la remisión.";

        return new VerificacionSoporteDto(motivo is null, motivo, qrCoincide, true, ia.Analizado,
            ia.NumeroLeido, ia.HayFirma, ia.HayNombreOCedula, ia.Analizado ? ia.Confianza : null, ia.Observacion, ia.HaySello);
    }

    private static async Task<byte[]> LeerAsync(Stream contenido, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        await contenido.CopyToAsync(memoria, ct);
        return memoria.ToArray();
    }

    public async Task<(Stream Contenido, string NombreArchivo, string? ContentType)> DescargarSoporteAsync(int soporteId, int usuarioId, CancellationToken ct = default)
    {
        var permisos = await PermisosAsync(usuarioId, ct);
        var soporte = await db.SoportesRemision.AsNoTracking().Include(s => s.Remision)
            .FirstOrDefaultAsync(s => s.Id == soporteId, ct)
            ?? throw new KeyNotFoundException("Soporte no encontrado.");
        ExigirVerRemision(permisos, soporte.Remision);
        var contenido = await almacenamiento.AbrirAsync(soporte.RutaArchivo, ct);
        return (contenido, soporte.NombreArchivo, soporte.ContentType);
    }

    // ---------------------------------------------------------------------------------------------
    // Administración
    // ---------------------------------------------------------------------------------------------

    public async Task<ConfiguracionEntregasDto> ObtenerConfiguracionAsync(CancellationToken ct = default) =>
        ConfigADto(await CargarConfiguracionAsync(ct));

    public async Task<ConfiguracionEntregasDto> ActualizarConfiguracionAsync(ActualizarConfiguracionEntregasDto dto, int usuarioId, CancellationToken ct = default)
    {
        var config = await CargarConfiguracionAsync(ct);
        config.VerificacionIAActiva = dto.VerificacionIAActiva;
        config.FechaActualizacion = DateTime.UtcNow;
        config.ActualizadoPorUsuarioId = usuarioId;
        await db.SaveChangesAsync(ct);
        await db.Entry(config).Reference(c => c.ActualizadoPorUsuario).LoadAsync(ct);
        return ConfigADto(config);
    }

    private async Task<ConfiguracionEntregas> CargarConfiguracionAsync(CancellationToken ct)
    {
        var config = await db.ConfiguracionesEntregas.Include(c => c.ActualizadoPorUsuario)
            .FirstOrDefaultAsync(c => c.Id == ConfiguracionEntregas.IdUnico, ct);
        if (config is not null) return config;
        // La fila la crea la migración; esto solo cubre una base sin ella.
        config = new ConfiguracionEntregas { Id = ConfiguracionEntregas.IdUnico, FechaInicio = DateTime.Today };
        db.ConfiguracionesEntregas.Add(config);
        await db.SaveChangesAsync(ct);
        return config;
    }

    private ConfiguracionEntregasDto ConfigADto(ConfiguracionEntregas c) =>
        new(c.VerificacionIAActiva, verificadorIA.Activo, c.FechaInicio, c.FechaActualizacion, c.ActualizadoPorUsuario?.Nombre);

    /// <summary>La IA revisa solo si el administrador la encendió Y el servidor tiene la clave.</summary>
    private async Task<bool> IaEncendidaAsync(CancellationToken ct) =>
        verificadorIA.Activo && await db.ConfiguracionesEntregas.AsNoTracking()
            .AnyAsync(c => c.Id == ConfiguracionEntregas.IdUnico && c.VerificacionIAActiva, ct);

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
            .Include(r => r.Soportes).ThenInclude(s => s.TipoDocumentoFacturacion)
            .Include(r => r.EventosFacturacion).ThenInclude(e => e.Usuario)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new KeyNotFoundException("Remisión no encontrada.");

    /// <summary>
    /// Lo que puede hacer el usuario en Entregas. <c>VerTodas</c> = ve todas las remisiones (ver,
    /// gestionar o facturar); el emisor (por su Usuario WO) siempre ve y documenta las suyas.
    /// </summary>
    private sealed record Permisos(int UsuarioId, bool VerTodas, bool Gestionar, bool Documentar, bool Facturar, string? UsuarioWO)
    {
        public bool EsEmisor(string? elaboradoPorWO) =>
            !string.IsNullOrWhiteSpace(UsuarioWO) && !string.IsNullOrWhiteSpace(elaboradoPorWO)
            && string.Equals(UsuarioWO.Trim(), elaboradoPorWO.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Puede adjuntar documentos y enviar a contabilidad (el estado lo revisa quien llama).</summary>
        public bool PuedeDocumentar(Remision r) => EsEmisor(r.ElaboradoPorWO) || (Documentar && VerTodas);
    }

    private async Task<Permisos> PermisosAsync(int usuarioId, CancellationToken ct)
    {
        var roles = await db.UsuarioRoles.AsNoTracking()
            .Where(ur => ur.UsuarioId == usuarioId && ur.Rol.Activo && ur.Usuario.Activo)
            .Select(ur => new { ur.Rol.PuedeGestionarEntregas, ur.Rol.PuedeVerEntregas, ur.Rol.PuedeDocumentarEntregas, ur.Rol.PuedeFacturarEntregas })
            .ToListAsync(ct);
        var usuarioWO = await db.Usuarios.AsNoTracking().Where(u => u.Id == usuarioId && u.Activo)
            .Select(u => u.UsuarioWO).FirstOrDefaultAsync(ct);
        var gestionar = roles.Any(r => r.PuedeGestionarEntregas);
        var facturar = roles.Any(r => r.PuedeFacturarEntregas);
        return new Permisos(usuarioId, gestionar || facturar || roles.Any(r => r.PuedeVerEntregas), gestionar,
            roles.Any(r => r.PuedeDocumentarEntregas), facturar, string.IsNullOrWhiteSpace(usuarioWO) ? null : usuarioWO.Trim());
    }

    private static void ExigirVerRemision(Permisos permisos, Remision remision)
    {
        if (!permisos.VerTodas && !permisos.EsEmisor(remision.ElaboradoPorWO))
            throw new UnauthorizedAccessException("No tenés permiso para ver esta remisión.");
    }

    private async Task ExigirGestionarAsync(int usuarioId, CancellationToken ct)
    {
        if (!(await PermisosAsync(usuarioId, ct)).Gestionar)
            throw new UnauthorizedAccessException("No tenés permiso para confirmar entregas.");
    }

    private async Task<RemisionDetalleDto> ADetalleAsync(Remision r, Permisos permisos, CancellationToken ct)
    {
        var tipos = await TiposActivosAsync(ct);
        var docs = r.Soportes.Where(s => s.Tipo == TipoSoporteRemision.DocumentoFacturacion).ToList();
        var requeridos = r.Estado == EstadoRemision.Entregada
            ? TiposQueAplican(tipos, r.NitCliente)
                .Select(t => new DocumentoRequeridoDto(t.Id, t.Nombre, t.Descripcion, t.Obligatorio, docs.Any(d => d.TipoDocumentoFacturacionId == t.Id)))
                .ToList()
            : [];
        var abierta = r.Estado == EstadoRemision.Entregada
            && r.EstadoFacturacion is EstadoFacturacionRemision.PendienteDocumentos or EstadoFacturacionRemision.Devuelta;
        var puedeAdjuntar = abierta && permisos.PuedeDocumentar(r);
        var emisor = await NombresEmisorAsync(r.ElaboradoPorWO, ct);

        return new RemisionDetalleDto(
            r.Id, r.Prefijo, r.Numero, r.Fecha, r.Cliente, r.NitCliente, r.Direccion, r.Ciudad, r.Telefono,
            r.Vendedor, r.ElaboradoPorWO, r.FormaDePago, r.DireccionEntrega, r.CampoEntrega, r.OrdenCompraCliente,
            r.Cotizacion, r.Nota, r.Estado.ToString(), r.FechaEntrega, r.EntregadaPorUsuario?.Nombre,
            r.ObservacionEntrega, r.VerificacionQR?.ToString(), CodigoQR(r),
            r.Renglones.OrderBy(x => x.Orden).Select(x => new RenglonRemisionDto(x.Orden, x.Codigo, x.Descripcion, x.Cantidad, x.UnidadMedida)).ToList(),
            r.Soportes.OrderBy(s => s.FechaCarga).Select(s => new SoporteRemisionDto(
                s.Id, s.Tipo.ToString(), s.NombreArchivo, s.ContentType, s.TamanoBytes, s.QrLeido,
                s.SubidoPorUsuario?.Nombre ?? "", s.FechaCarga, s.Valido, s.IaNumeroLeido, s.IaHayFirma, s.IaObservacion, s.IaHaySello,
                s.TipoDocumentoFacturacionId, s.TipoDocumentoFacturacion?.Nombre, s.IaTipoDetectado, s.Aviso,
                puedeAdjuntar && s.Tipo == TipoSoporteRemision.DocumentoFacturacion)).ToList(),
            permisos.Gestionar,
            r.EstadoFacturacion?.ToString(),
            requeridos,
            r.EventosFacturacion.OrderBy(e => e.Fecha)
                .Select(e => new EventoFacturacionDto(e.Accion.ToString(), e.Comentario, e.Usuario?.Nombre ?? "", e.Fecha)).ToList(),
            emisor,
            puedeAdjuntar,
            puedeAdjuntar,
            permisos.Facturar && r.EstadoFacturacion == EstadoFacturacionRemision.EnFacturacion,
            r.FechaEnvioFacturacion);
    }

    private static string? Recortar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length <= max ? s : s[..max]);
}
