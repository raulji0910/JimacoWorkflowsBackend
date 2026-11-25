using System.Text.Json;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Utilidades;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Cotizaciones del portal (2026-11-25, Fase 1). El portal solo guarda y valida; quien crea la COT
/// en WO es el Sincronizador --documentos (CotizacionWriter), que toma las "PorCrear", la graba y
/// devuelve prefijo y número. Los productos, bodegas, clientes y "Más Datos" se validan contra el
/// catálogo de WO que sube el mismo Sincronizador (CatalogoWOService).
/// </summary>
public class CotizacionService(AppDbContext db, TimeProvider reloj) : ICotizacionService
{
    public const int MaximoRenglones = 300;
    public const int LargoMaximoMasDatos = 500;
    private const string TipoDocumentoWO = "COT";
    private const string ConceptoPorDefecto = "COTIZACIÓN";

    // ---------------- Portal ----------------

    public async Task<IReadOnlyList<CotizacionResumenDto>> ListarAsync(int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        return await db.Cotizaciones.AsNoTracking()
            .Where(c => c.UsuarioId == usuarioId)
            .OrderByDescending(c => c.FechaActualizacion)
            .Take(500)
            .Select(c => new CotizacionResumenDto(c.Id, c.Estado, c.Usuario.Nombre, c.ClienteNombre, c.ClienteIdentificacion,
                c.Renglones.Count, c.Total, c.FechaActualizacion, c.FechaEnvio, c.PrefijoWO, c.NumeroWO, c.MensajeError))
            .ToListAsync(ct);
    }

    public async Task<CotizacionDto> ObtenerAsync(int id, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        return Mapear(await CargarPropiaAsync(id, usuarioId, ct));
    }

    public async Task<CotizacionDto> CrearAsync(GuardarCotizacionDto dto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var cotizacion = new Cotizacion { UsuarioId = usuarioId, FechaCreacion = ahora, FechaActualizacion = ahora, Concepto = ConceptoPorDefecto };
        await AplicarAsync(cotizacion, dto, ct);
        db.Cotizaciones.Add(cotizacion);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(cotizacion.Id, usuarioId, ct);
    }

    public async Task<CotizacionDto> ActualizarAsync(int id, GuardarCotizacionDto dto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var cotizacion = await CargarPropiaAsync(id, usuarioId, ct);
        ExigirEditable(cotizacion);
        db.CotizacionRenglones.RemoveRange(cotizacion.Renglones);
        cotizacion.Renglones.Clear();
        await AplicarAsync(cotizacion, dto, ct);
        cotizacion.FechaActualizacion = reloj.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Mapear(cotizacion);
    }

    public async Task EliminarAsync(int id, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var cotizacion = await CargarPropiaAsync(id, usuarioId, ct);
        if (cotizacion.Estado != EstadoCotizacion.Borrador)
            throw new InvalidOperationException("Solo se pueden borrar cotizaciones en borrador (una que ya se envió puede haberse creado en World Office).");
        db.Cotizaciones.Remove(cotizacion);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Valida todo lo que WO va a exigir y la deja "PorCrear" para el Sincronizador.</summary>
    public async Task<CotizacionDto> EnviarAsync(int id, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var cotizacion = await CargarPropiaAsync(id, usuarioId, ct);
        ExigirEditable(cotizacion);

        var usuario = await db.Usuarios.AsNoTracking().FirstAsync(u => u.Id == usuarioId, ct);
        if (string.IsNullOrWhiteSpace(usuario.UsuarioWO) || usuario.IdTerceroWO is null)
            throw new InvalidOperationException("Tu usuario no tiene configurado el usuario y el tercero de World Office (\"elaborado por\"). Pedíselo al administrador.");

        var errores = new List<string>();
        if (cotizacion.IdTerceroWO is null) errores.Add("Falta el cliente.");
        if (cotizacion.IdFormaPagoWO is null) errores.Add("Falta la forma de pago.");
        if (cotizacion.VigenciaDias is < 0 or > 365) errores.Add("La vigencia debe estar entre 0 y 365 días.");
        if (cotizacion.Renglones.Count == 0) errores.Add("La cotización no tiene productos.");

        if (cotizacion.IdTerceroWO is { } idTercero && !await db.TercerosWO.AnyAsync(t => t.IdTerceroWO == idTercero, ct))
            errores.Add("El cliente ya no existe en el catálogo de World Office.");

        var ids = cotizacion.Renglones.Select(r => r.IdInventarioWO).Distinct().ToList();
        var activos = await db.ProductosWO.AsNoTracking().Where(p => ids.Contains(p.IdInventarioWO) && p.Activo)
            .Select(p => p.IdInventarioWO).ToListAsync(ct);
        foreach (var renglon in cotizacion.Renglones.OrderBy(r => r.Orden))
        {
            var etiqueta = $"Renglón {renglon.Orden} ({renglon.Codigo})";
            if (!activos.Contains(renglon.IdInventarioWO)) errores.Add($"{etiqueta}: el producto está inactivo o ya no existe en World Office.");
            if (renglon.Cantidad <= 0) errores.Add($"{etiqueta}: la cantidad debe ser mayor que cero.");
            if (renglon.ValorUnitario < 0) errores.Add($"{etiqueta}: el valor unitario no puede ser negativo.");
        }
        if (errores.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errores));

        var ahora = reloj.GetUtcNow().UtcDateTime;
        cotizacion.Estado = EstadoCotizacion.PorCrear;
        cotizacion.FechaEnvio = ahora;
        cotizacion.FechaDocumento = ConciliacionService.HoyColombia(reloj);
        cotizacion.UsuarioWO = usuario.UsuarioWO!.Trim();
        cotizacion.IdTerceroElaboraWO = usuario.IdTerceroWO;
        cotizacion.MensajeError = null;
        cotizacion.FechaActualizacion = ahora;
        await db.SaveChangesAsync(ct);
        return Mapear(cotizacion);
    }

    /// <summary>"Copiar documento" de WO: una cotización nueva en borrador con el mismo contenido.</summary>
    public async Task<CotizacionDto> CopiarAsync(int id, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoAsync(usuarioId, ct);
        var origen = await CargarPropiaAsync(id, usuarioId, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var copia = new Cotizacion
        {
            UsuarioId = usuarioId,
            FechaCreacion = ahora,
            FechaActualizacion = ahora,
            IdTerceroWO = origen.IdTerceroWO,
            ClienteIdentificacion = origen.ClienteIdentificacion,
            ClienteNombre = origen.ClienteNombre,
            IdDireccionWO = origen.IdDireccionWO,
            DireccionTexto = origen.DireccionTexto,
            IdFormaPagoWO = origen.IdFormaPagoWO,
            FormaPagoNombre = origen.FormaPagoNombre,
            Concepto = origen.Concepto,
            VigenciaDias = origen.VigenciaDias,
            MasDatosJson = origen.MasDatosJson,
            Subtotal = origen.Subtotal,
            Iva = origen.Iva,
            Total = origen.Total,
            Renglones = origen.Renglones.OrderBy(r => r.Orden).Select(r => new CotizacionRenglon
            {
                Orden = r.Orden, IdInventarioWO = r.IdInventarioWO, Codigo = r.Codigo, Descripcion = r.Descripcion,
                UnidadMedida = r.UnidadMedida, IdBodegaWO = r.IdBodegaWO, Cantidad = r.Cantidad, ValorUnitario = r.ValorUnitario,
                Iva = r.Iva, IdClasificacion = r.IdClasificacion, Nota = r.Nota
            }).ToList()
        };
        db.Cotizaciones.Add(copia);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(copia.Id, usuarioId, ct);
    }

    // ---------------- Sincronizador ----------------

    public async Task<IReadOnlyList<PendienteCreacionCotizacionDto>> ListarPendientesCreacionAsync(CancellationToken ct = default)
    {
        var pendientes = await db.Cotizaciones.AsNoTracking()
            .Include(c => c.Renglones)
            .Where(c => c.Estado == EstadoCotizacion.PorCrear)
            .OrderBy(c => c.FechaEnvio)
            .Take(20)
            .ToListAsync(ct);

        return pendientes.Select(c => new PendienteCreacionCotizacionDto(
            c.Id,
            (c.FechaDocumento ?? ConciliacionService.HoyColombia(reloj)).ToDateTime(TimeOnly.MinValue),
            c.IdTerceroWO!.Value,
            c.ClienteIdentificacion ?? "",
            c.IdDireccionWO,
            c.IdFormaPagoWO!.Value,
            c.IdTerceroElaboraWO!.Value,
            c.UsuarioWO!,
            string.IsNullOrWhiteSpace(c.Concepto) ? ConceptoPorDefecto : c.Concepto,
            c.VigenciaDias,
            LeerMasDatos(c.MasDatosJson),
            c.Total,
            c.Renglones.OrderBy(r => r.Orden).Select(r => new RenglonCreacionCotizacionDto(r.IdInventarioWO, r.Codigo, r.IdBodegaWO,
                r.UnidadMedida, r.Cantidad, r.ValorUnitario, r.Iva, r.IdClasificacion, r.Nota)).ToList()))
            .ToList();
    }

    public async Task ConfirmarCreadaAsync(int id, CotizacionCreadaDto dto, CancellationToken ct = default)
    {
        var cotizacion = await db.Cotizaciones.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Cotización no encontrada.");
        if (cotizacion.Estado == EstadoCotizacion.Creada && cotizacion.IdAsientoContableWO == dto.IdAsientoContable)
            return;
        if (cotizacion.Estado != EstadoCotizacion.PorCrear)
            throw new InvalidOperationException($"La cotización {id} está en estado {cotizacion.Estado}, no se esperaba que se creara en WO.");

        var ahora = reloj.GetUtcNow().UtcDateTime;
        cotizacion.Estado = EstadoCotizacion.Creada;
        cotizacion.PrefijoWO = dto.Prefijo;
        cotizacion.NumeroWO = dto.Numero;
        cotizacion.IdAsientoContableWO = dto.IdAsientoContable;
        cotizacion.FechaCreacionWO = ahora;
        cotizacion.MensajeError = null;
        cotizacion.FechaActualizacion = ahora;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReportarErrorAsync(int id, string mensaje, CancellationToken ct = default)
    {
        var cotizacion = await db.Cotizaciones.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Cotización no encontrada.");
        if (cotizacion.Estado != EstadoCotizacion.PorCrear) return;

        cotizacion.Estado = EstadoCotizacion.Error;
        cotizacion.MensajeError = Recortar(string.IsNullOrWhiteSpace(mensaje) ? "World Office rechazó la cotización." : mensaje.Trim(), 1000);
        cotizacion.FechaActualizacion = reloj.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Apoyo ----------------

    /// <summary>Copia lo de la pantalla a la entidad, resolviendo nombres y datos de producto contra el catálogo de WO.</summary>
    private async Task AplicarAsync(Cotizacion cotizacion, GuardarCotizacionDto dto, CancellationToken ct)
    {
        if (dto.Renglones.Count > MaximoRenglones)
            throw new InvalidOperationException($"Una cotización admite hasta {MaximoRenglones} renglones.");

        // Cliente, sucursal y forma de pago.
        cotizacion.IdTerceroWO = null; cotizacion.ClienteIdentificacion = null; cotizacion.ClienteNombre = null;
        if (dto.IdTercero is { } idTercero)
        {
            var tercero = await db.TercerosWO.AsNoTracking().FirstOrDefaultAsync(t => t.IdTerceroWO == idTercero, ct)
                ?? throw new InvalidOperationException("El cliente no existe en el catálogo de World Office.");
            cotizacion.IdTerceroWO = tercero.IdTerceroWO;
            cotizacion.ClienteIdentificacion = tercero.Identificacion;
            cotizacion.ClienteNombre = tercero.Nombre;
        }

        cotizacion.IdDireccionWO = null; cotizacion.DireccionTexto = null;
        if (dto.IdDireccion is { } idDireccion && cotizacion.IdTerceroWO is not null)
        {
            var direccion = await db.DireccionesTerceroWO.AsNoTracking().FirstOrDefaultAsync(d => d.IdDireccionWO == idDireccion, ct);
            if (direccion is null || direccion.IdTerceroWO != cotizacion.IdTerceroWO)
                throw new InvalidOperationException("La sucursal elegida no es de ese cliente.");
            cotizacion.IdDireccionWO = direccion.IdDireccionWO;
            cotizacion.DireccionTexto = Recortar(string.Join(" - ", new[] { direccion.Direccion, direccion.Ciudad }.Where(s => !string.IsNullOrWhiteSpace(s))), 400);
        }

        cotizacion.IdFormaPagoWO = null; cotizacion.FormaPagoNombre = null;
        if (dto.IdFormaPago is { } idFormaPago)
        {
            var forma = await db.FormasPagoWO.AsNoTracking().FirstOrDefaultAsync(f => f.IdFormaPagoWO == idFormaPago, ct)
                ?? throw new InvalidOperationException("La forma de pago no existe en World Office.");
            cotizacion.IdFormaPagoWO = forma.IdFormaPagoWO;
            cotizacion.FormaPagoNombre = forma.Nombre;
        }

        cotizacion.Concepto = string.IsNullOrWhiteSpace(dto.Concepto) ? ConceptoPorDefecto : Recortar(dto.Concepto.Trim(), 500);
        cotizacion.VigenciaDias = dto.VigenciaDias;

        // "Más Datos": solo los campos en uso de la COT en WO.
        var camposActivos = await db.CamposPersonalizadosWO.AsNoTracking()
            .Where(c => c.TipoDocumento == TipoDocumentoWO && c.Activo).Select(c => c.Numero).ToListAsync(ct);
        var masDatos = new SortedDictionary<int, string>();
        foreach (var (numero, valor) in dto.MasDatos ?? new Dictionary<int, string>())
        {
            if (string.IsNullOrWhiteSpace(valor)) continue;
            if (!camposActivos.Contains(numero))
                throw new InvalidOperationException($"El campo {numero} de \"Más Datos\" no está en uso en las cotizaciones de World Office.");
            masDatos[numero] = Recortar(valor.Trim(), LargoMaximoMasDatos);
        }
        cotizacion.MasDatosJson = masDatos.Count == 0 ? null : JsonSerializer.Serialize(masDatos);

        // Renglones: código, descripción, unidad, IVA y C. Costos salen del catálogo, no de la pantalla.
        var ids = dto.Renglones.Select(r => r.IdInventario).Distinct().ToList();
        var productos = await db.ProductosWO.AsNoTracking().Where(p => ids.Contains(p.IdInventarioWO)).ToDictionaryAsync(p => p.IdInventarioWO, ct);
        var bodegas = await db.BodegasWO.AsNoTracking().Where(b => b.Activa).Select(b => b.IdBodegaWO).ToListAsync(ct);
        var orden = 0;
        foreach (var entrada in dto.Renglones)
        {
            orden++;
            if (!productos.TryGetValue(entrada.IdInventario, out var producto))
                throw new InvalidOperationException($"Renglón {orden}: el producto no existe en el catálogo de World Office.");
            if (!bodegas.Contains(entrada.IdBodega))
                throw new InvalidOperationException($"Renglón {orden} ({producto.Codigo}): la bodega \"{entrada.IdBodega}\" no existe o está inactiva.");
            if (entrada.Cantidad < 0 || entrada.ValorUnitario < 0)
                throw new InvalidOperationException($"Renglón {orden} ({producto.Codigo}): cantidad y valor no pueden ser negativos.");

            cotizacion.Renglones.Add(new CotizacionRenglon
            {
                Orden = orden,
                IdInventarioWO = producto.IdInventarioWO,
                Codigo = producto.Codigo,
                Descripcion = producto.Descripcion,
                UnidadMedida = producto.UnidadMedida,
                IdBodegaWO = entrada.IdBodega,
                Cantidad = Math.Round(entrada.Cantidad, 4),
                ValorUnitario = Math.Round(entrada.ValorUnitario, 4),
                Iva = producto.Iva,
                IdClasificacion = producto.IdClasificacion,
                Nota = string.IsNullOrWhiteSpace(entrada.Nota) ? null : Recortar(entrada.Nota.Trim(), 500)
            });
        }

        var totales = TotalesWO.Documento(cotizacion.Renglones.Select(r => (r.Cantidad, r.ValorUnitario, r.Iva)));
        cotizacion.Subtotal = totales.Subtotal;
        cotizacion.Iva = totales.Iva;
        cotizacion.Total = totales.Total;
    }

    private static void ExigirEditable(Cotizacion cotizacion)
    {
        if (cotizacion.Estado is not (EstadoCotizacion.Borrador or EstadoCotizacion.Error))
            throw new InvalidOperationException(cotizacion.Estado == EstadoCotizacion.Creada
                ? $"La cotización ya se creó en World Office ({cotizacion.PrefijoWO} {cotizacion.NumeroWO:0}). Cualquier cambio se hace allá."
                : "La cotización ya se envió a World Office y se está creando.");
    }

    private async Task<Cotizacion> CargarPropiaAsync(int id, int usuarioId, CancellationToken ct)
    {
        var cotizacion = await db.Cotizaciones
            .Include(c => c.Renglones)
            .Include(c => c.Usuario)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Cotización no encontrada.");
        if (cotizacion.UsuarioId != usuarioId)
            throw new UnauthorizedAccessException("Esa cotización es de otro usuario.");
        return cotizacion;
    }

    private async Task ExigirPermisoAsync(int usuarioId, CancellationToken ct)
    {
        var tienePermiso = await db.UsuarioRoles
            .AnyAsync(ur => ur.UsuarioId == usuarioId && ur.Usuario.Activo && ur.Rol.Activo && ur.Rol.PuedeCrearCotizaciones, ct);
        if (!tienePermiso)
            throw new UnauthorizedAccessException("No tenés permiso para crear cotizaciones.");
    }

    private static CotizacionDto Mapear(Cotizacion c)
    {
        var renglones = c.Renglones.OrderBy(r => r.Orden).Select(r =>
        {
            var t = TotalesWO.Renglon(r.Cantidad, r.ValorUnitario, r.Iva);
            return new CotizacionRenglonDto(r.Orden, r.IdInventarioWO, r.Codigo, r.Descripcion, r.UnidadMedida, r.IdBodegaWO,
                r.Cantidad, r.ValorUnitario, r.Iva, r.Nota, t.Subtotal, t.Iva, t.Total);
        }).ToList();

        return new CotizacionDto(c.Id, c.Estado, c.UsuarioId, c.Usuario?.Nombre ?? "", c.IdTerceroWO, c.ClienteIdentificacion, c.ClienteNombre,
            c.IdDireccionWO, c.DireccionTexto, c.IdFormaPagoWO, c.FormaPagoNombre, c.Concepto, c.VigenciaDias, LeerMasDatos(c.MasDatosJson),
            c.Subtotal, c.Iva, c.Total, renglones.Sum(r => r.Cantidad), c.FechaCreacion, c.FechaActualizacion, c.FechaDocumento, c.FechaEnvio,
            c.PrefijoWO, c.NumeroWO, c.FechaCreacionWO, c.MensajeError, renglones);
    }

    private static IReadOnlyDictionary<int, string> LeerMasDatos(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<int, string>()
            : JsonSerializer.Deserialize<Dictionary<int, string>>(json) ?? new Dictionary<int, string>();

    private static string Recortar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];
}
