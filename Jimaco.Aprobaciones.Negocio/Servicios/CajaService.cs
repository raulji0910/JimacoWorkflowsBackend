using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Cajas menores de efectivo (2026-11-27, pedido del usuario). Reemplaza los vales de papel: cada
/// peso que entra o sale es un <see cref="MovimientoCaja"/> y el saldo (<see cref="Caja.SaldoActual"/>)
/// está siempre al día. Reglas:
/// <list type="bullet">
/// <item>Dos roles por empresa: administrador (tope, conceptos, recargas, arqueos, anulaciones) y
/// girador (giros, legalizaciones, vueltas). El administrador también puede girar.</item>
/// <item>Nunca se gira ni se reintegra más del efectivo que hay en la caja en ese momento.</item>
/// <item>Toda plata que sale se legaliza: cuánto se gastó, por qué concepto y a quién. Si sobró, queda
/// vuelta pendiente; si gastó de más, el reintegro se paga al legalizar.</item>
/// <item>Un giro puede pagar OC de contado hasta $300.000 (paso con <see cref="PasoFlujo.RequiereGiroCajaAlAprobar"/>):
/// se legaliza solo por la suma de las OC y la diferencia queda como vuelta pendiente (ej. 500.000
/// para OC de 450.000 → 50.000), con aviso al girador y al administrador.</item>
/// <item>Nada se borra: los errores se anulan con motivo. Solo control en el portal, nada en WO.</item>
/// </list>
/// </summary>
public partial class CajaService(
    AppDbContext db,
    TimeProvider reloj,
    INotificacionService notificaciones,
    IAlmacenamientoArchivos almacenamiento,
    ILogger<CajaService>? logger = null) : ICajaService
{
    public const decimal ValorMaximoMovimiento = 100_000_000m;
    public const double HorasEntreRecordatorios = 24;
    private const long TamanoMaximoSoporte = 15_000_000;
    private readonly ILogger<CajaService> log = logger ?? NullLogger<CajaService>.Instance;

    // ================= Cajas =================

    public async Task<IReadOnlyList<CajaResumenDto>> ListarAsync(int usuarioId, CancellationToken ct = default)
    {
        var permisos = await PermisosAsync(usuarioId, ct);
        var cajas = await db.Cajas.AsNoTracking().Include(c => c.ActualizadoPorUsuario).OrderBy(c => c.Empresa).ToListAsync(ct);
        var resultado = new List<CajaResumenDto>();
        foreach (var caja in cajas)
        {
            var (admin, girador) = permisos.Para(caja.Empresa);
            if (admin || girador)
                resultado.Add(await ResumenAsync(caja, admin, girador, ct));
        }
        return resultado;
    }

    public async Task<CajaResumenDto> ObtenerAsync(int cajaId, int usuarioId, CancellationToken ct = default)
    {
        var (caja, admin, girador) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        return await ResumenAsync(caja, admin, girador, ct);
    }

    public async Task<CajaResumenDto> ActualizarAsync(int cajaId, ActualizarCajaDto dto, int usuarioId, CancellationToken ct = default)
    {
        var (caja, admin, girador) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: true, ct);
        if (dto.Tope <= 0 || dto.Tope > ValorMaximoMovimiento)
            throw new InvalidOperationException("El tope de la caja debe ser mayor que cero.");
        if (dto.SaldoMinimoAlerta < 0 || dto.SaldoMinimoAlerta > dto.Tope)
            throw new InvalidOperationException("El saldo mínimo para avisar debe estar entre 0 y el tope.");

        caja.Tope = Math.Round(dto.Tope, 0);
        caja.SaldoMinimoAlerta = Math.Round(dto.SaldoMinimoAlerta, 0);
        caja.FechaActualizacion = Ahora;
        caja.ActualizadoPorUsuarioId = usuarioId;
        caja.Version = Guid.NewGuid();
        await GuardarAsync(ct);
        await RevisarSaldoBajoAsync(caja, ct);
        return await ObtenerAsync(cajaId, usuarioId, ct);
    }

    // ================= Pasos de OC que se pagan con la caja =================

    public async Task<IReadOnlyList<PasoPagoCajaDto>> ListarPasosPagoAsync(int cajaId, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: true, ct);
        return await PasosDeLaEmpresa(caja.Empresa)
            .OrderBy(p => p.DefinicionFlujo.Nombre).ThenBy(p => p.Orden)
            .Select(p => new PasoPagoCajaDto(p.Id, p.DefinicionFlujo.Nombre, p.Orden, p.Nombre, p.RequiereGiroCajaAlAprobar,
                db.InstanciasDocumento.Count(i => i.PasoActualId == p.Id && i.Estado == EstadoInstanciaDocumento.EnProceso)))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Marca/desmarca el paso que se paga con la caja (los flujos no se editan, se versionan — por eso
    /// esto va aparte). Al marcarlo, las OC que YA están en ese paso también van a exigir giro.
    /// </summary>
    public async Task<PasoPagoCajaDto> MarcarPasoPagoAsync(int cajaId, int pasoId, MarcarPasoPagoCajaDto dto, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: true, ct);
        var paso = await db.PasosFlujo.Include(p => p.DefinicionFlujo).ThenInclude(f => f.TipoDocumento)
            .FirstOrDefaultAsync(p => p.Id == pasoId, ct)
            ?? throw new KeyNotFoundException("No se encontró el paso.");
        if (paso.DefinicionFlujo.TipoDocumento.Empresa != caja.Empresa)
            throw new InvalidOperationException($"Ese paso es de un flujo de {paso.DefinicionFlujo.TipoDocumento.Empresa.Nombre()}.");
        if (!paso.DefinicionFlujo.Activo || paso.DefinicionFlujo.FormaDePagoRequerida != FormaPagoContado)
            throw new InvalidOperationException("Solo se pueden marcar pasos de flujos activos de contado.");
        paso.RequiereGiroCajaAlAprobar = dto.RequiereGiroCaja;
        await GuardarAsync(ct);
        return (await ListarPasosPagoAsync(cajaId, usuarioId, ct)).First(p => p.PasoId == pasoId);
    }

    /// <summary>
    /// Pasos de los flujos activos DE CONTADO de la empresa: el efectivo de la caja solo paga OC de contado.
    /// (No se filtra por el prefijo "OC": en QA la Orden de Compra usa un prefijo de prueba.)
    /// </summary>
    private IQueryable<PasoFlujo> PasosDeLaEmpresa(Empresa empresa) =>
        db.PasosFlujo.AsNoTracking()
            .Where(p => p.DefinicionFlujo.Activo && p.DefinicionFlujo.TipoDocumento.Empresa == empresa
                        && p.DefinicionFlujo.FormaDePagoRequerida == FormaPagoContado);

    private const string FormaPagoContado = "Contado";

    // ================= Conceptos =================

    public async Task<IReadOnlyList<ConceptoCajaDto>> ListarConceptosAsync(int cajaId, int usuarioId, CancellationToken ct = default)
    {
        await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        return await db.ConceptosCaja.AsNoTracking()
            .Where(c => c.CajaId == cajaId)
            .OrderByDescending(c => c.Activo).ThenBy(c => c.Nombre)
            .Select(c => new ConceptoCajaDto(c.Id, c.Nombre, c.Activo))
            .ToListAsync(ct);
    }

    public async Task<ConceptoCajaDto> CrearConceptoAsync(int cajaId, GuardarConceptoCajaDto dto, int usuarioId, CancellationToken ct = default)
    {
        await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: true, ct);
        var nombre = NormalizarNombre(dto.Nombre, "El nombre del concepto", 100);
        if (await db.ConceptosCaja.AnyAsync(c => c.CajaId == cajaId && c.Nombre == nombre, ct))
            throw new InvalidOperationException($"Ya existe el concepto \"{nombre}\" en esta caja.");
        var concepto = new ConceptoCaja { CajaId = cajaId, Nombre = nombre, Activo = dto.Activo };
        db.ConceptosCaja.Add(concepto);
        await GuardarAsync(ct);
        return new ConceptoCajaDto(concepto.Id, concepto.Nombre, concepto.Activo);
    }

    public async Task<ConceptoCajaDto> ActualizarConceptoAsync(int cajaId, int conceptoId, GuardarConceptoCajaDto dto, int usuarioId, CancellationToken ct = default)
    {
        await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: true, ct);
        var concepto = await db.ConceptosCaja.FirstOrDefaultAsync(c => c.Id == conceptoId && c.CajaId == cajaId, ct)
            ?? throw new KeyNotFoundException("No se encontró el concepto.");
        var nombre = NormalizarNombre(dto.Nombre, "El nombre del concepto", 100);
        if (await db.ConceptosCaja.AnyAsync(c => c.CajaId == cajaId && c.Id != conceptoId && c.Nombre == nombre, ct))
            throw new InvalidOperationException($"Ya existe el concepto \"{nombre}\" en esta caja.");
        concepto.Nombre = nombre;
        concepto.Activo = dto.Activo;
        await GuardarAsync(ct);
        return new ConceptoCajaDto(concepto.Id, concepto.Nombre, concepto.Activo);
    }

    // ================= Beneficiarios =================

    public async Task<IReadOnlyList<BeneficiarioCajaDto>> BuscarBeneficiariosAsync(string? texto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirAlgunaCajaAsync(usuarioId, ct);
        var consulta = db.BeneficiariosCaja.AsNoTracking();
        var palabras = (texto ?? "").Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var palabra in palabras)
            consulta = consulta.Where(b => b.Nombre.Contains(palabra) || b.Cedula.Contains(palabra));
        return await consulta
            .OrderByDescending(b => b.Activo).ThenBy(b => b.Nombre)
            .Take(50)
            .Select(b => new BeneficiarioCajaDto(b.Id, b.Nombre, b.Cedula, b.Telefono, b.Correo, b.EsInterno, b.Activo))
            .ToListAsync(ct);
    }

    public async Task<BeneficiarioCajaDto> CrearBeneficiarioAsync(GuardarBeneficiarioCajaDto dto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirAlgunaCajaAsync(usuarioId, ct);
        var beneficiario = new BeneficiarioCaja
        {
            Nombre = "", Cedula = "",
            FechaCreacion = Ahora,
            CreadoPorUsuarioId = usuarioId
        };
        await AplicarBeneficiarioAsync(beneficiario, dto, null, ct);
        db.BeneficiariosCaja.Add(beneficiario);
        await GuardarAsync(ct);
        return MapearBeneficiario(beneficiario);
    }

    public async Task<BeneficiarioCajaDto> ActualizarBeneficiarioAsync(int beneficiarioId, GuardarBeneficiarioCajaDto dto, int usuarioId, CancellationToken ct = default)
    {
        await ExigirAlgunaCajaAsync(usuarioId, ct);
        var beneficiario = await db.BeneficiariosCaja.FirstOrDefaultAsync(b => b.Id == beneficiarioId, ct)
            ?? throw new KeyNotFoundException("No se encontró la persona.");
        await AplicarBeneficiarioAsync(beneficiario, dto, beneficiarioId, ct);
        await GuardarAsync(ct);
        return MapearBeneficiario(beneficiario);
    }

    private async Task AplicarBeneficiarioAsync(BeneficiarioCaja b, GuardarBeneficiarioCajaDto dto, int? idActual, CancellationToken ct)
    {
        var nombre = NormalizarNombre(dto.Nombre, "El nombre", 150);
        var cedula = new string((dto.Cedula ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (cedula.Length is < 4 or > 20)
            throw new InvalidOperationException("La cédula es obligatoria (solo números, entre 4 y 20 dígitos).");
        if (await db.BeneficiariosCaja.AnyAsync(x => x.Cedula == cedula && x.Id != (idActual ?? 0), ct))
            throw new InvalidOperationException($"Ya existe una persona con la cédula {cedula}: búscala por su nombre o cédula.");
        var correo = string.IsNullOrWhiteSpace(dto.Correo) ? null : dto.Correo.Trim().ToLowerInvariant();
        if (correo is not null && (correo.Length > 150 || !CorreoValido().IsMatch(correo)))
            throw new InvalidOperationException("El correo no es válido.");
        var telefono = string.IsNullOrWhiteSpace(dto.Telefono) ? null : dto.Telefono.Trim();
        if (telefono is { Length: > 30 })
            throw new InvalidOperationException("El teléfono es demasiado largo.");

        b.Nombre = nombre;
        b.Cedula = cedula;
        b.Telefono = telefono;
        b.Correo = correo;
        b.EsInterno = dto.EsInterno;
        b.Activo = dto.Activo;
    }

    // ================= Recargas, arqueos, anulaciones =================

    public async Task<MovimientoCajaDto> RegistrarRecargaAsync(int cajaId, RegistrarRecargaDto dto, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: true, ct);
        var valor = ValidarValor(dto.Valor, "El valor de la recarga");
        var movimiento = Mover(caja, TipoMovimientoCaja.Recarga, valor, usuarioId, Nota(dto.Nota), null);
        await GuardarAsync(ct);
        await RevisarSaldoBajoAsync(caja, ct);
        return await MovimientoDtoAsync(movimiento.Id, ct);
    }

    public async Task<ResultadoArqueoDto> RegistrarArqueoAsync(int cajaId, RegistrarArqueoDto dto, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: true, ct);
        if (dto.EfectivoContado < 0 || dto.EfectivoContado > ValorMaximoMovimiento)
            throw new InvalidOperationException("El efectivo contado no es válido.");
        var contado = Math.Round(dto.EfectivoContado, 0);
        var saldoSistema = caja.SaldoActual;
        var diferencia = contado - saldoSistema;
        if (diferencia == 0)
            return new ResultadoArqueoDto(saldoSistema, contado, 0, null);

        var motivo = Nota(dto.Motivo)
            ?? throw new InvalidOperationException($"El efectivo contado no cuadra con el sistema (diferencia de {FormatoPesos.Miles(diferencia)}): escribe el motivo del ajuste.");
        var nota = $"Arqueo: contado {FormatoPesos.Miles(contado)}, sistema {FormatoPesos.Miles(saldoSistema)}. {motivo}";
        var ajuste = Mover(caja, TipoMovimientoCaja.Ajuste, diferencia, usuarioId, Recortar(nota, 500), null);
        await GuardarAsync(ct);
        await RevisarSaldoBajoAsync(caja, ct);
        return new ResultadoArqueoDto(saldoSistema, contado, diferencia, await MovimientoDtoAsync(ajuste.Id, ct));
    }

    public async Task<MovimientoCajaDto> AnularMovimientoAsync(int cajaId, int movimientoId, AnularDto dto, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: true, ct);
        var movimiento = await db.MovimientosCaja.FirstOrDefaultAsync(m => m.Id == movimientoId && m.CajaId == cajaId, ct)
            ?? throw new KeyNotFoundException("No se encontró el movimiento.");
        if (movimiento.Tipo is not (TipoMovimientoCaja.Recarga or TipoMovimientoCaja.Ajuste))
            throw new InvalidOperationException("Solo se anulan aquí recargas y ajustes. Los giros se anulan desde el giro.");
        if (movimiento.Anulado)
            throw new InvalidOperationException("Ese movimiento ya está anulado.");
        var motivo = Motivo(dto);

        if (caja.SaldoActual - movimiento.Valor < 0)
            throw new InvalidOperationException(
                $"No se puede anular: la caja quedaría en negativo (hay {FormatoPesos.Miles(caja.SaldoActual)} y el movimiento es de {FormatoPesos.Miles(movimiento.Valor)}).");
        caja.SaldoActual -= movimiento.Valor;
        caja.Version = Guid.NewGuid();
        Anular(movimiento, usuarioId, motivo);
        await GuardarAsync(ct);
        await RevisarSaldoBajoAsync(caja, ct);
        return await MovimientoDtoAsync(movimiento.Id, ct);
    }

    public async Task<HistorialCajaDto> HistorialAsync(int cajaId, FiltroHistorialCajaDto filtro, int usuarioId, CancellationToken ct = default)
    {
        await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        var consulta = db.MovimientosCaja.AsNoTracking().Where(m => m.CajaId == cajaId);
        if (!filtro.IncluirAnulados) consulta = consulta.Where(m => !m.Anulado);
        if (filtro.Desde is { } desde) consulta = consulta.Where(m => m.Fecha >= desde);
        if (filtro.Hasta is { } hasta) consulta = consulta.Where(m => m.Fecha < hasta);
        if (filtro.Tipo is { } tipo) consulta = consulta.Where(m => m.Tipo == tipo);
        if (filtro.BeneficiarioId is { } beneficiarioId) consulta = consulta.Where(m => m.GiroCaja != null && m.GiroCaja.BeneficiarioCajaId == beneficiarioId);
        if (filtro.ConceptoId is { } conceptoId) consulta = consulta.Where(m => m.GiroCaja != null && m.GiroCaja.ConceptoCajaId == conceptoId);
        foreach (var palabra in (filtro.Texto ?? "").Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            consulta = consulta.Where(m =>
                (m.Nota != null && m.Nota.ToUpper().Contains(palabra)) ||
                (m.GiroCaja != null && (m.GiroCaja.BeneficiarioCaja.Nombre.Contains(palabra) || m.GiroCaja.BeneficiarioCaja.Cedula.Contains(palabra) ||
                    m.GiroCaja.OrdenesCompra.Any(o => o.InstanciaDocumento.NumeroReferencia != null && o.InstanciaDocumento.NumeroReferencia.Contains(palabra)))));

        var total = await consulta.CountAsync(ct);
        var validos = consulta.Where(m => !m.Anulado);
        var entradas = await validos.Where(m => m.Valor > 0).SumAsync(m => (decimal?)m.Valor, ct) ?? 0;
        var salidas = await validos.Where(m => m.Valor < 0).SumAsync(m => (decimal?)m.Valor, ct) ?? 0;
        var tamano = Math.Clamp(filtro.TamanoPagina, 1, 500);
        var pagina = Math.Max(1, filtro.Pagina);
        var movimientos = await ProyectarMovimientos(consulta.OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id)
            .Skip((pagina - 1) * tamano).Take(tamano)).ToListAsync(ct);
        return new HistorialCajaDto(movimientos, total, entradas, -salidas);
    }

    // ================= Giros =================

    public async Task<IReadOnlyList<OrdenCompraPendientePagoDto>> OrdenesCompraPendientesAsync(int cajaId, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        return await OrdenesEsperandoGiro(caja.Empresa)
            .OrderBy(i => i.FechaCreacion)
            .Select(i => new OrdenCompraPendientePagoDto(i.Id, i.NumeroReferencia, i.Proveedor, i.Valor ?? 0, i.FechaCreacion, i.PasoActual!.Nombre))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GiroCajaResumenDto>> ListarGirosAsync(int cajaId, bool soloPendientes, int usuarioId, CancellationToken ct = default)
    {
        await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        var consulta = db.GirosCaja.AsNoTracking().Where(g => g.CajaId == cajaId);
        if (soloPendientes)
            consulta = consulta.Where(g => g.Estado == EstadoGiroCaja.Abierto || g.Estado == EstadoGiroCaja.VueltaPendiente);
        var giros = await consulta
            .Include(g => g.BeneficiarioCaja).Include(g => g.ConceptoCaja).Include(g => g.GiradoPorUsuario)
            .Include(g => g.OrdenesCompra).ThenInclude(o => o.InstanciaDocumento)
            .OrderByDescending(g => g.Numero)
            .Take(soloPendientes ? 1000 : 300)
            .ToListAsync(ct);
        var ahora = Ahora;
        return giros.Select(g => new GiroCajaResumenDto(g.Id, g.Numero, g.Estado, g.Fecha, g.BeneficiarioCaja.Nombre, g.BeneficiarioCaja.Cedula,
            g.ConceptoCaja.Nombre, g.ValorEntregado, g.ValorLegalizado, g.VueltaPendiente, g.GiradoPorUsuario.Nombre,
            HorasPendiente(g, ahora),
            g.OrdenesCompra.Select(o => $"OC {o.InstanciaDocumento.NumeroReferencia}").ToList())).ToList();
    }

    public async Task<GiroCajaDto> ObtenerGiroAsync(int cajaId, int giroId, int usuarioId, CancellationToken ct = default)
    {
        await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        return await GiroDtoAsync(cajaId, giroId, ct);
    }

    public async Task<GiroCajaDto> CrearGiroAsync(int cajaId, CrearGiroCajaDto dto, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        var valor = ValidarValor(dto.ValorEntregado, "El valor entregado");
        var beneficiario = await db.BeneficiariosCaja.FirstOrDefaultAsync(b => b.Id == dto.BeneficiarioId, ct)
            ?? throw new InvalidOperationException("Elige la persona a la que se le entrega el dinero.");
        if (!beneficiario.Activo)
            throw new InvalidOperationException($"{beneficiario.Nombre} está inactivo: actívalo antes de girarle.");
        var concepto = await db.ConceptosCaja.FirstOrDefaultAsync(c => c.Id == dto.ConceptoId && c.CajaId == cajaId, ct)
            ?? throw new InvalidOperationException("Elige el concepto del giro.");
        if (!concepto.Activo)
            throw new InvalidOperationException($"El concepto \"{concepto.Nombre}\" está inactivo.");

        // OC de contado que se pagan con este giro.
        var idsOc = (dto.OrdenesCompraIds ?? []).Distinct().ToList();
        var ordenes = idsOc.Count == 0
            ? []
            : await db.InstanciasDocumento.Include(i => i.PasoActual).Where(i => idsOc.Contains(i.Id)).ToListAsync(ct);
        if (ordenes.Count != idsOc.Count)
            throw new InvalidOperationException("Alguna de las OC elegidas ya no existe.");
        var esperando = idsOc.Count == 0 ? [] : await OrdenesEsperandoGiro(caja.Empresa).Where(i => idsOc.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
        foreach (var oc in ordenes)
        {
            if (oc.Empresa != caja.Empresa)
                throw new InvalidOperationException($"La OC {oc.NumeroReferencia} es de {oc.Empresa.Nombre()}: se paga con la caja de esa empresa.");
            if (!esperando.Contains(oc.Id))
                throw new InvalidOperationException($"La OC {oc.NumeroReferencia} no está esperando el pago en efectivo (o ya tiene un giro).");
            if (oc.Valor is not > 0)
                throw new InvalidOperationException($"La OC {oc.NumeroReferencia} no tiene valor.");
        }
        var totalOc = ordenes.Sum(o => o.Valor!.Value);
        if (totalOc > valor)
            throw new InvalidOperationException(
                $"Las OC elegidas suman {FormatoPesos.Miles(totalOc)} y el giro es de {FormatoPesos.Miles(valor)}: el giro tiene que cubrir las OC.");

        var ahora = Ahora;
        var numero = (await db.GirosCaja.Where(g => g.CajaId == cajaId).MaxAsync(g => (int?)g.Numero, ct) ?? 0) + 1;
        var giro = new GiroCaja
        {
            CajaId = cajaId,
            Numero = numero,
            BeneficiarioCajaId = beneficiario.Id,
            ConceptoCajaId = concepto.Id,
            ValorEntregado = valor,
            Nota = Nota(dto.Nota),
            Fecha = ahora,
            GiradoPorUsuarioId = usuarioId
        };
        foreach (var oc in ordenes)
            giro.OrdenesCompra.Add(new GiroCajaOrdenCompra { InstanciaDocumentoId = oc.Id, Valor = oc.Valor!.Value });

        // Con OC se legaliza solo: lo gastado es lo que valen las OC; lo demás es vuelta.
        if (ordenes.Count > 0)
        {
            giro.ValorLegalizado = totalOc;
            giro.FechaLegalizacion = ahora;
            giro.LegalizadoPorUsuarioId = usuarioId;
            giro.NotaLegalizacion = "Pago de " + string.Join(", ", ordenes.Select(o => $"OC {o.NumeroReferencia}"));
            giro.Estado = totalOc == valor ? EstadoGiroCaja.Cerrado : EstadoGiroCaja.VueltaPendiente;
        }

        db.GirosCaja.Add(giro);
        Mover(caja, TipoMovimientoCaja.Giro, -valor, usuarioId, giro.Nota, giro);
        await GuardarAsync(ct);
        await RevisarSaldoBajoAsync(caja, ct);
        if (giro.Estado == EstadoGiroCaja.VueltaPendiente)
            await AvisarVueltaPendienteAsync(caja, giro.Id, ct);
        return await GiroDtoAsync(cajaId, giro.Id, ct);
    }

    public async Task<GiroCajaDto> LegalizarGiroAsync(int cajaId, int giroId, LegalizarGiroCajaDto dto, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        var giro = await CargarGiroAsync(cajaId, giroId, ct);
        if (giro.Estado != EstadoGiroCaja.Abierto)
            throw new InvalidOperationException($"El giro #{giro.Numero} ya está legalizado.");
        if (dto.ValorGastado < 0 || dto.ValorGastado > ValorMaximoMovimiento)
            throw new InvalidOperationException("El valor gastado no es válido.");
        var gastado = Math.Round(dto.ValorGastado, 0);

        var ahora = Ahora;
        giro.ValorLegalizado = gastado;
        giro.FechaLegalizacion = ahora;
        giro.LegalizadoPorUsuarioId = usuarioId;
        giro.NotaLegalizacion = Nota(dto.Nota);
        giro.UltimoRecordatorio = null;

        if (gastado > giro.ValorEntregado)
        {
            // Gastó de más: se le devuelve la diferencia ya, de la misma caja.
            var reintegro = gastado - giro.ValorEntregado;
            Mover(caja, TipoMovimientoCaja.Reintegro, -reintegro, usuarioId, $"Reintegro del giro #{giro.Numero}: gastó {FormatoPesos.Miles(gastado)} de {FormatoPesos.Miles(giro.ValorEntregado)}", giro);
            giro.Estado = EstadoGiroCaja.Cerrado;
        }
        else
        {
            giro.Estado = gastado == giro.ValorEntregado ? EstadoGiroCaja.Cerrado : EstadoGiroCaja.VueltaPendiente;
            caja.Version = Guid.NewGuid();
        }

        await GuardarAsync(ct);
        await RevisarSaldoBajoAsync(caja, ct);
        if (giro.Estado == EstadoGiroCaja.VueltaPendiente)
            await AvisarVueltaPendienteAsync(caja, giro.Id, ct);
        return await GiroDtoAsync(cajaId, giro.Id, ct);
    }

    public async Task<GiroCajaDto> SubirSoporteAsync(int cajaId, int giroId, string nombreArchivo, string? contentType, Stream contenido, int usuarioId, CancellationToken ct = default)
    {
        await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        var giro = await CargarGiroAsync(cajaId, giroId, ct);
        if (giro.Estado == EstadoGiroCaja.Anulado)
            throw new InvalidOperationException("El giro está anulado.");
        var tipo = (contentType ?? "").ToLowerInvariant();
        if (!(tipo.StartsWith("image/") || tipo == "application/pdf"))
            throw new InvalidOperationException("El soporte debe ser una foto o un PDF.");

        using var memoria = new MemoryStream();
        await contenido.CopyToAsync(memoria, ct);
        if (memoria.Length == 0) throw new InvalidOperationException("El archivo está vacío.");
        if (memoria.Length > TamanoMaximoSoporte) throw new InvalidOperationException("El archivo pesa más de 15 MB.");
        memoria.Position = 0;
        var nombre = Path.GetFileName(string.IsNullOrWhiteSpace(nombreArchivo) ? "soporte" : nombreArchivo);
        giro.SoporteRutaArchivo = await almacenamiento.GuardarAsync($"caja-{cajaId}-giro-{giro.Numero}-{nombre}", memoria, ct);
        giro.SoporteNombreArchivo = Recortar(nombre, 300);
        giro.SoporteContentType = tipo;
        await GuardarAsync(ct);
        return await GiroDtoAsync(cajaId, giro.Id, ct);
    }

    public async Task<(Stream Contenido, string NombreArchivo, string ContentType)> AbrirSoporteAsync(int cajaId, int giroId, int usuarioId, CancellationToken ct = default)
    {
        await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        var giro = await CargarGiroAsync(cajaId, giroId, ct);
        if (giro.SoporteRutaArchivo is null)
            throw new KeyNotFoundException("Este giro no tiene soporte.");
        return (await almacenamiento.AbrirAsync(giro.SoporteRutaArchivo, ct), giro.SoporteNombreArchivo ?? "soporte", giro.SoporteContentType ?? "application/octet-stream");
    }

    public async Task<GiroCajaDto> RecibirVueltaAsync(int cajaId, int giroId, RecibirVueltaDto dto, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        var giro = await CargarGiroAsync(cajaId, giroId, ct);
        if (giro.Estado != EstadoGiroCaja.VueltaPendiente)
            throw new InvalidOperationException($"El giro #{giro.Numero} no tiene vueltas pendientes.");
        var valor = ValidarValor(dto.Valor, "El valor de la vuelta");
        var pendiente = giro.VueltaPendiente;
        if (valor > pendiente)
            throw new InvalidOperationException($"La vuelta pendiente del giro #{giro.Numero} es de {FormatoPesos.Miles(pendiente)}.");

        giro.ValorVueltasRecibidas += valor;
        Mover(caja, TipoMovimientoCaja.Vuelta, valor, usuarioId, Nota(dto.Nota) ?? $"Vuelta del giro #{giro.Numero}", giro);
        if (giro.VueltaPendiente == 0)
            giro.Estado = EstadoGiroCaja.Cerrado;
        await GuardarAsync(ct);
        await RevisarSaldoBajoAsync(caja, ct);
        return await GiroDtoAsync(cajaId, giro.Id, ct);
    }

    public async Task<GiroCajaDto> AnularGiroAsync(int cajaId, int giroId, AnularDto dto, int usuarioId, CancellationToken ct = default)
    {
        var (caja, _, _) = await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: true, ct);
        var giro = await db.GirosCaja
            .Include(g => g.Movimientos)
            .Include(g => g.OrdenesCompra).ThenInclude(o => o.InstanciaDocumento).ThenInclude(i => i.PasoActual)
            .FirstOrDefaultAsync(g => g.Id == giroId && g.CajaId == cajaId, ct)
            ?? throw new KeyNotFoundException("No se encontró el giro.");
        var motivo = Motivo(dto);
        if (giro.Estado == EstadoGiroCaja.Anulado)
            throw new InvalidOperationException("El giro ya está anulado.");
        // Se puede anular mientras la plata sigue "toda afuera": sin vueltas ni reintegros registrados.
        if (giro.Movimientos.Any(m => !m.Anulado && m.Tipo != TipoMovimientoCaja.Giro))
            throw new InvalidOperationException("El giro ya tiene vueltas o reintegros registrados: no se puede anular.");
        if (giro.Estado == EstadoGiroCaja.Cerrado && giro.OrdenesCompra.Count == 0)
            throw new InvalidOperationException("El giro ya está cerrado: no se puede anular.");
        var ocAvanzada = giro.OrdenesCompra.FirstOrDefault(o =>
            o.InstanciaDocumento.Estado != EstadoInstanciaDocumento.EnProceso || o.InstanciaDocumento.PasoActual is not { RequiereGiroCajaAlAprobar: true });
        if (ocAvanzada is not null)
            throw new InvalidOperationException($"La OC {ocAvanzada.InstanciaDocumento.NumeroReferencia} ya se aprobó con este giro: no se puede anular.");

        // El efectivo vuelve a la caja.
        foreach (var movimiento in giro.Movimientos.Where(m => !m.Anulado))
        {
            caja.SaldoActual -= movimiento.Valor;
            Anular(movimiento, usuarioId, motivo);
        }
        caja.Version = Guid.NewGuid();
        giro.Estado = EstadoGiroCaja.Anulado;
        giro.FechaAnulacion = Ahora;
        giro.AnuladoPorUsuarioId = usuarioId;
        giro.MotivoAnulacion = motivo;
        await GuardarAsync(ct);
        await RevisarSaldoBajoAsync(caja, ct);
        return await GiroDtoAsync(cajaId, giro.Id, ct);
    }

    public async Task<(byte[] Pdf, string NombreArchivo)> GenerarPdfGiroAsync(int cajaId, int giroId, int usuarioId, CancellationToken ct = default)
    {
        await CargarCajaAsync(cajaId, usuarioId, exigirAdministrador: false, ct);
        var giro = await GiroDtoAsync(cajaId, giroId, ct);
        var pdf = GiroCajaPdfGenerador.Generar(giro);
        return (pdf, $"Giro-caja-{giro.Empresa.Nombre()}-{giro.Numero}.pdf");
    }

    // ================= Recordatorios =================

    public async Task<int> EnviarRecordatoriosAsync(CancellationToken ct = default)
    {
        var ahora = Ahora;
        var limite = ahora.AddHours(-HorasEntreRecordatorios);
        var pendientes = await db.GirosCaja
            .Include(g => g.Caja).Include(g => g.BeneficiarioCaja).Include(g => g.ConceptoCaja)
            .Include(g => g.OrdenesCompra).ThenInclude(o => o.InstanciaDocumento)
            .Where(g => (g.Estado == EstadoGiroCaja.Abierto && g.Fecha <= limite) ||
                        (g.Estado == EstadoGiroCaja.VueltaPendiente && g.FechaLegalizacion <= limite))
            .Where(g => g.UltimoRecordatorio == null || g.UltimoRecordatorio <= limite)
            .ToListAsync(ct);
        if (pendientes.Count == 0) return 0;

        // Un solo correo por caja con todos sus pendientes (no uno por giro).
        foreach (var grupo in pendientes.GroupBy(g => g.Caja))
        {
            var caja = grupo.Key;
            var filas = new StringBuilder();
            foreach (var g in grupo.OrderBy(g => g.Numero))
            {
                var que = g.Estado == EstadoGiroCaja.Abierto
                    ? $"sin legalizar ({FormatoPesos.Miles(g.ValorEntregado)} entregados)"
                    : $"debe <strong>{FormatoPesos.Miles(g.VueltaPendiente)}</strong> de vuelta";
                var oc = g.OrdenesCompra.Count == 0 ? "" : " — " + string.Join(", ", g.OrdenesCompra.Select(o => $"OC {o.InstanciaDocumento.NumeroReferencia}"));
                filas.Append($"<li>Giro #{g.Numero} — {H(g.BeneficiarioCaja.Nombre)} ({H(g.ConceptoCaja.Nombre)}{oc}): {que}, hace {(int)HorasPendiente(g, ahora)} horas.</li>");
            }
            var asunto = $"[Jimaco Aprobaciones] {caja.Nombre}: {grupo.Count()} giro(s) pendiente(s) de legalizar o de vueltas";
            var cuerpo = $"<p>En la <strong>{H(caja.Nombre)}</strong> hay giros pendientes de hace más de {HorasEntreRecordatorios:0} horas:</p><ul>{filas}</ul>";
            await AvisarResponsablesAsync(caja, asunto, cuerpo, ct);
            foreach (var g in grupo) g.UltimoRecordatorio = ahora;
        }
        await db.SaveChangesAsync(ct);
        return pendientes.Count;
    }

    // ================= Avisos =================

    private async Task AvisarVueltaPendienteAsync(Caja caja, int giroId, CancellationToken ct)
    {
        var giro = await db.GirosCaja.AsNoTracking()
            .Include(g => g.BeneficiarioCaja).Include(g => g.ConceptoCaja)
            .Include(g => g.OrdenesCompra).ThenInclude(o => o.InstanciaDocumento)
            .FirstAsync(g => g.Id == giroId, ct);
        var oc = giro.OrdenesCompra.Count == 0
            ? ""
            : $" para pagar {string.Join(", ", giro.OrdenesCompra.Select(o => $"OC {o.InstanciaDocumento.NumeroReferencia} ({FormatoPesos.Miles(o.Valor)})"))}";
        var asunto = $"[Jimaco Aprobaciones] {caja.Nombre}: esperando {FormatoPesos.Miles(giro.VueltaPendiente)} de vuelta del giro #{giro.Numero}";
        var cuerpo = $"""
            <p>Al giro <strong>#{giro.Numero}</strong> de la {H(caja.Nombre)} se le entregaron
            <strong>{FormatoPesos.Miles(giro.ValorEntregado)}</strong> a {H(giro.BeneficiarioCaja.Nombre)}{oc}, y se gastaron
            {FormatoPesos.Miles(giro.ValorLegalizado ?? 0)}.</p>
            <p>Está pendiente la vuelta de <strong>{FormatoPesos.Miles(giro.VueltaPendiente)}</strong>.</p>
            """;
        await AvisarResponsablesAsync(caja, asunto, cuerpo, ct);
    }

    private async Task RevisarSaldoBajoAsync(Caja caja, CancellationToken ct)
    {
        var bajo = caja.SaldoMinimoAlerta > 0 && caja.SaldoActual < caja.SaldoMinimoAlerta;
        if (bajo == caja.AvisoSaldoBajoEnviado) return;
        caja.AvisoSaldoBajoEnviado = bajo;
        await db.SaveChangesAsync(ct);
        if (!bajo) return;

        var asunto = $"[Jimaco Aprobaciones] {caja.Nombre}: hay que recargar (quedan {FormatoPesos.Miles(caja.SaldoActual)})";
        var cuerpo = $"""
            <p>El efectivo de la <strong>{H(caja.Nombre)}</strong> bajó a <strong>{FormatoPesos.Miles(caja.SaldoActual)}</strong>,
            por debajo del mínimo de {FormatoPesos.Miles(caja.SaldoMinimoAlerta)}.</p>
            <p>Para llegar al tope ({FormatoPesos.Miles(caja.Tope)}) faltan <strong>{FormatoPesos.Miles(Math.Max(0, caja.Tope - caja.SaldoActual))}</strong>.</p>
            """;
        await AvisarResponsablesAsync(caja, asunto, cuerpo, ct, soloAdministradores: true);
    }

    private async Task AvisarResponsablesAsync(Caja caja, string asunto, string cuerpo, CancellationToken ct, bool soloAdministradores = false)
    {
        var esJimaco = caja.Empresa == Empresa.Jimaco;
        var usuarios = await db.UsuarioRoles.AsNoTracking()
            .Where(ur => ur.Usuario.Activo && ur.Rol.Activo)
            .Where(ur => esJimaco
                ? ur.Rol.PuedeAdministrarCajaJimaco || (!soloAdministradores && ur.Rol.PuedeGirarCajaJimaco)
                : ur.Rol.PuedeAdministrarCajaFerrealiados || (!soloAdministradores && ur.Rol.PuedeGirarCajaFerrealiados))
            .Select(ur => ur.UsuarioId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var usuarioId in usuarios)
        {
            try
            {
                await notificaciones.NotificarHtmlAsync(null, usuarioId, asunto, cuerpo, $"/cajas/{caja.Id}", ct);
            }
            catch (Exception ex)
            {
                // El aviso nunca tumba el movimiento, que ya quedó guardado.
                log.LogWarning(ex, "No se pudo avisar al usuario {UsuarioId} sobre la caja {CajaId}.", usuarioId, caja.Id);
            }
        }
    }

    // ================= Apoyo =================

    private DateTime Ahora => reloj.GetUtcNow().UtcDateTime;

    private sealed record PermisosCaja(bool AdminJimaco, bool GiraJimaco, bool AdminFerrealiados, bool GiraFerrealiados)
    {
        public (bool Admin, bool Girador) Para(Empresa empresa) => empresa == Empresa.Ferrealiados
            ? (AdminFerrealiados, AdminFerrealiados || GiraFerrealiados)
            : (AdminJimaco, AdminJimaco || GiraJimaco);

        public bool Alguna => AdminJimaco || GiraJimaco || AdminFerrealiados || GiraFerrealiados;
    }

    private async Task<PermisosCaja> PermisosAsync(int usuarioId, CancellationToken ct)
    {
        var roles = await db.UsuarioRoles.AsNoTracking()
            .Where(ur => ur.UsuarioId == usuarioId && ur.Usuario.Activo && ur.Rol.Activo)
            .Select(ur => new { ur.Rol.PuedeAdministrarCajaJimaco, ur.Rol.PuedeGirarCajaJimaco, ur.Rol.PuedeAdministrarCajaFerrealiados, ur.Rol.PuedeGirarCajaFerrealiados })
            .ToListAsync(ct);
        return new PermisosCaja(roles.Any(r => r.PuedeAdministrarCajaJimaco), roles.Any(r => r.PuedeGirarCajaJimaco),
            roles.Any(r => r.PuedeAdministrarCajaFerrealiados), roles.Any(r => r.PuedeGirarCajaFerrealiados));
    }

    private async Task ExigirAlgunaCajaAsync(int usuarioId, CancellationToken ct)
    {
        if (!(await PermisosAsync(usuarioId, ct)).Alguna)
            throw new UnauthorizedAccessException("No tenés permiso sobre ninguna caja.");
    }

    private async Task<(Caja Caja, bool Admin, bool Girador)> CargarCajaAsync(int cajaId, int usuarioId, bool exigirAdministrador, CancellationToken ct)
    {
        var caja = await db.Cajas.Include(c => c.ActualizadoPorUsuario).FirstOrDefaultAsync(c => c.Id == cajaId, ct)
            ?? throw new KeyNotFoundException("No se encontró la caja.");
        var (admin, girador) = (await PermisosAsync(usuarioId, ct)).Para(caja.Empresa);
        if (!girador)
            throw new UnauthorizedAccessException($"No tenés permiso sobre la {caja.Nombre}.");
        if (exigirAdministrador && !admin)
            throw new UnauthorizedAccessException($"Solo el administrador de la {caja.Nombre} puede hacer esto.");
        return (caja, admin, girador);
    }

    private async Task<GiroCaja> CargarGiroAsync(int cajaId, int giroId, CancellationToken ct) =>
        await db.GirosCaja.FirstOrDefaultAsync(g => g.Id == giroId && g.CajaId == cajaId, ct)
        ?? throw new KeyNotFoundException("No se encontró el giro.");

    /// <summary>OC de la empresa paradas en un paso que exige giro de caja y que todavía no tienen uno vigente.</summary>
    private IQueryable<InstanciaDocumento> OrdenesEsperandoGiro(Empresa empresa) =>
        db.InstanciasDocumento.AsNoTracking()
            .Where(i => i.Empresa == empresa && i.Estado == EstadoInstanciaDocumento.EnProceso
                        && i.PasoActual != null && i.PasoActual.RequiereGiroCajaAlAprobar)
            .Where(i => !db.GirosCajaOrdenesCompra.Any(o => o.InstanciaDocumentoId == i.Id && o.GiroCaja.Estado != EstadoGiroCaja.Anulado));

    /// <summary>Aplica un movimiento con signo a la caja. Nunca deja la caja en negativo.</summary>
    private MovimientoCaja Mover(Caja caja, TipoMovimientoCaja tipo, decimal valorConSigno, int usuarioId, string? nota, GiroCaja? giro)
    {
        if (caja.SaldoActual + valorConSigno < 0)
            throw new InvalidOperationException(
                $"No hay suficiente efectivo en la caja: hay {FormatoPesos.Miles(caja.SaldoActual)} y se necesitan {FormatoPesos.Miles(-valorConSigno)}.");
        caja.SaldoActual += valorConSigno;
        caja.Version = Guid.NewGuid();
        var movimiento = new MovimientoCaja
        {
            Caja = caja,
            Tipo = tipo,
            Valor = valorConSigno,
            SaldoDespues = caja.SaldoActual,
            Fecha = Ahora,
            UsuarioId = usuarioId,
            Nota = nota,
            GiroCaja = giro
        };
        db.MovimientosCaja.Add(movimiento);
        return movimiento;
    }

    private void Anular(MovimientoCaja movimiento, int usuarioId, string motivo)
    {
        movimiento.Anulado = true;
        movimiento.FechaAnulacion = Ahora;
        movimiento.AnuladoPorUsuarioId = usuarioId;
        movimiento.MotivoAnulacion = motivo;
    }

    private async Task GuardarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Otra persona movió la caja al mismo tiempo. Vuelve a cargar e intenta de nuevo.");
        }
    }

    private async Task<CajaResumenDto> ResumenAsync(Caja caja, bool admin, bool girador, CancellationToken ct)
    {
        var abiertos = await db.GirosCaja.AsNoTracking().Where(g => g.CajaId == caja.Id && g.Estado == EstadoGiroCaja.Abierto)
            .GroupBy(_ => 1).Select(x => new { N = x.Count(), V = x.Sum(g => g.ValorEntregado) }).FirstOrDefaultAsync(ct);
        var conVuelta = await db.GirosCaja.AsNoTracking().Where(g => g.CajaId == caja.Id && g.Estado == EstadoGiroCaja.VueltaPendiente)
            .Select(g => new { g.ValorEntregado, g.ValorLegalizado, g.ValorVueltasRecibidas }).ToListAsync(ct);
        return new CajaResumenDto(caja.Id, caja.Empresa, caja.Nombre, caja.Tope, caja.SaldoMinimoAlerta, caja.SaldoActual,
            abiertos?.N ?? 0, abiertos?.V ?? 0,
            conVuelta.Count, conVuelta.Sum(g => Math.Max(0, g.ValorEntregado - (g.ValorLegalizado ?? 0) - g.ValorVueltasRecibidas)),
            Math.Max(0, caja.Tope - caja.SaldoActual),
            caja.SaldoMinimoAlerta > 0 && caja.SaldoActual < caja.SaldoMinimoAlerta,
            admin, girador, caja.FechaActualizacion, caja.ActualizadoPorUsuario?.Nombre);
    }

    private IQueryable<MovimientoCajaDto> ProyectarMovimientos(IQueryable<MovimientoCaja> consulta) =>
        consulta.Select(m => new MovimientoCajaDto(m.Id, m.Tipo, m.Valor, m.SaldoDespues, m.Fecha, m.Usuario.Nombre, m.Nota,
            m.GiroCajaId, m.GiroCaja != null ? m.GiroCaja.Numero : null,
            m.GiroCaja != null ? m.GiroCaja.BeneficiarioCaja.Nombre : null,
            m.GiroCaja != null ? m.GiroCaja.ConceptoCaja.Nombre : null,
            m.Anulado, m.MotivoAnulacion, m.AnuladoPorUsuario != null ? m.AnuladoPorUsuario.Nombre : null, m.FechaAnulacion));

    private async Task<MovimientoCajaDto> MovimientoDtoAsync(int movimientoId, CancellationToken ct) =>
        await ProyectarMovimientos(db.MovimientosCaja.AsNoTracking().Where(m => m.Id == movimientoId)).FirstAsync(ct);

    private async Task<GiroCajaDto> GiroDtoAsync(int cajaId, int giroId, CancellationToken ct)
    {
        var g = await db.GirosCaja.AsNoTracking()
            .Include(x => x.Caja).Include(x => x.BeneficiarioCaja).Include(x => x.ConceptoCaja)
            .Include(x => x.GiradoPorUsuario).Include(x => x.LegalizadoPorUsuario).Include(x => x.AnuladoPorUsuario)
            .Include(x => x.OrdenesCompra).ThenInclude(o => o.InstanciaDocumento).ThenInclude(i => i.PasoActual)
            .FirstOrDefaultAsync(x => x.Id == giroId && x.CajaId == cajaId, ct)
            ?? throw new KeyNotFoundException("No se encontró el giro.");
        var movimientos = await ProyectarMovimientos(db.MovimientosCaja.AsNoTracking().Where(m => m.GiroCajaId == giroId).OrderBy(m => m.Fecha).ThenBy(m => m.Id))
            .ToListAsync(ct);
        return new GiroCajaDto(g.Id, g.CajaId, g.Caja.Empresa, g.Numero, g.Estado, g.Fecha, MapearBeneficiario(g.BeneficiarioCaja),
            new ConceptoCajaDto(g.ConceptoCaja.Id, g.ConceptoCaja.Nombre, g.ConceptoCaja.Activo),
            g.ValorEntregado, g.Nota, g.GiradoPorUsuario.Nombre, g.ValorLegalizado, g.FechaLegalizacion, g.LegalizadoPorUsuario?.Nombre,
            g.NotaLegalizacion, g.ValorVueltasRecibidas, g.VueltaPendiente, g.SoporteRutaArchivo is not null, g.SoporteNombreArchivo,
            g.FechaAnulacion, g.AnuladoPorUsuario?.Nombre, g.MotivoAnulacion,
            g.OrdenesCompra.Select(o => new OrdenCompraGiroDto(o.InstanciaDocumentoId, o.InstanciaDocumento.NumeroReferencia,
                o.InstanciaDocumento.Proveedor, o.Valor, o.InstanciaDocumento.PasoActual?.Nombre)).ToList(),
            movimientos);
    }

    private static BeneficiarioCajaDto MapearBeneficiario(BeneficiarioCaja b) =>
        new(b.Id, b.Nombre, b.Cedula, b.Telefono, b.Correo, b.EsInterno, b.Activo);

    private static double HorasPendiente(GiroCaja g, DateTime ahora) => g.Estado switch
    {
        EstadoGiroCaja.Abierto => (ahora - g.Fecha).TotalHours,
        EstadoGiroCaja.VueltaPendiente => (ahora - (g.FechaLegalizacion ?? g.Fecha)).TotalHours,
        _ => 0
    };

    private static decimal ValidarValor(decimal valor, string etiqueta)
    {
        if (valor <= 0) throw new InvalidOperationException($"{etiqueta} debe ser mayor que cero.");
        if (valor > ValorMaximoMovimiento) throw new InvalidOperationException($"{etiqueta} es demasiado alto.");
        if (valor != Math.Round(valor, 0)) throw new InvalidOperationException($"{etiqueta} va en pesos, sin centavos.");
        return valor;
    }

    private static string NormalizarNombre(string? texto, string etiqueta, int largo)
    {
        var limpio = string.Join(' ', (texto ?? "").Trim().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (limpio.Length == 0) throw new InvalidOperationException($"{etiqueta} es obligatorio.");
        if (limpio.Length > largo) throw new InvalidOperationException($"{etiqueta} no puede pasar de {largo} caracteres.");
        return limpio;
    }

    private static string? Nota(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : Recortar(texto.Trim(), 500);

    private static string Motivo(AnularDto dto) =>
        Nota(dto.Motivo) ?? throw new InvalidOperationException("Escribe el motivo de la anulación.");

    private static string Recortar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];

    private static string H(string? texto) => WebUtility.HtmlEncode(texto ?? "");

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex CorreoValido();
}
