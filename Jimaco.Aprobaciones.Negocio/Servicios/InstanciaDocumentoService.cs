using System.Text.Json;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// El motor de workflow: crear una instancia, listar pendientes por usuario/rol, y ejecutar
/// las acciones (aprobar/devolver/rechazar/reenviar) validando permisos y transiciones.
/// Todo lo que sabe sobre "OC" es configuración (TipoDocumento/DefinicionFlujo) — este servicio
/// no tiene ninguna referencia a un tipo de documento concreto.
/// </summary>
public class InstanciaDocumentoService(
    AppDbContext db,
    IAlmacenamientoArchivos almacenamiento,
    INotificacionService notificacionService,
    TimeProvider timeProvider) : IInstanciaDocumentoService
{
    /// <summary>
    /// Id "de mentira" que representa "El emisor" como una opción más de destino de devolución,
    /// junto a los ids reales de PasoFlujo en DestinosDevolucionDisponibles — nunca existe un
    /// PasoFlujo real con este id (los ids reales de la BD son siempre positivos).
    /// </summary>
    private const int DestinoEmisorId = -1;

    /// <summary>
    /// Puede haber varios flujos activos para el mismo tipo de documento, cada uno cubriendo un
    /// rango de Valor/FormaDePago distinto (ver DefinicionFlujo) — se elige el que calza con este
    /// documento puntual. Null en ValorMinimo/ValorMaximo/FormaDePagoRequerida = sin restricción de
    /// ese lado (aplica a cualquier valor/forma de pago). Se reusa tanto al crear un documento nuevo
    /// como al refrescar uno reenviado por el Sincronizador (ver <see cref="CrearAsync"/>) — en
    /// ambos casos hay que evaluar el flujo con los datos ACTUALES, no asumir uno fijo.
    /// </summary>
    private async Task<DefinicionFlujo> SeleccionarFlujoAsync(int tipoDocumentoId, decimal? valor, string? formaDePago, CancellationToken ct)
    {
        var flujosActivos = await db.DefinicionesFlujo.Include(f => f.Pasos)
            .Where(f => f.TipoDocumentoId == tipoDocumentoId && f.Activo)
            .ToListAsync(ct);

        return flujosActivos.FirstOrDefault(f =>
                (f.ValorMinimo is null || valor is null || valor >= f.ValorMinimo)
                && (f.ValorMaximo is null || valor is null || valor <= f.ValorMaximo)
                && (f.FormaDePagoRequerida is null || string.Equals(f.FormaDePagoRequerida, formaDePago, StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException(
                $"No hay un flujo activo configurado para este caso (Valor={valor?.ToString() ?? "sin valor"}, FormaDePago={formaDePago ?? "sin dato"}).");
    }

    public async Task<InstanciaDocumentoDetalleDto> CrearAsync(CrearInstanciaDocumentoDto dto, int usuarioId, CancellationToken ct = default, bool notificar = true)
    {
        // Idempotente para documentos sincronizados: el par (PrefijoOrigen, IdAsientoContableOrigen)
        // identifica de forma única una fila real de World Office. Si el Sincronizador vuelve a
        // pedir la creación de la misma OC (ej. un correo que se volvió a marcar como no leído, o
        // dos corridas superpuestas), se devuelve el documento que ya existe en vez de crear un
        // duplicado — sin esto, cada reintento crea un `InstanciaDocumento` nuevo arrancando desde
        // cero, dejando el historial real disperso entre copias.
        // La empresa del documento es la de su tipo (2026-11-13): la OC 1503 de Ferrealiados y la
        // OC 1503 de Jimaco pueden incluso compartir IdAsientoContable — son bases de WO distintas.
        var empresa = await db.TiposDocumento.Where(t => t.Id == dto.TipoDocumentoId).Select(t => t.Empresa).FirstOrDefaultAsync(ct);

        if (dto.IdAsientoContableOrigen is not null && dto.PrefijoOrigen is not null)
        {
            var existente = await db.InstanciasDocumento
                .Include(i => i.DefinicionFlujo).ThenInclude(f => f.Pasos)
                .Include(i => i.Renglones)
                .Include(i => i.DocumentosVinculados).ThenInclude(d => d.Renglones)
                .Where(i => i.IdAsientoContableOrigen == dto.IdAsientoContableOrigen && i.PrefijoOrigen == dto.PrefijoOrigen && i.Empresa == empresa)
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(ct);

            if (existente is not null)
            {
                // Si World Office vuelve a mandar la señal de "Solicitar Aprobación" (mismo correo)
                // para un documento que ya habíamos devuelto al emisor O que ya se había completado
                // del todo, es la misma intención que un reenvío manual desde "Mis documentos" — se
                // reinicia al primer paso. Devuelto: decisión explícita 2026-09-14. Completado: caso
                // real 2026-09-17 (OC 19235) — alguien necesitaba corregir y volver a mandar a
                // aprobar algo YA aprobado, y tuvo que aprobarlo a mano en WO porque el sistema no
                // reaccionaba. Rechazado/EnProceso siguen sin tocarse a propósito: un rechazo es una
                // decisión más definitiva que requiere revisión explícita, no un simple reenvío.
                if (existente.Estado is EstadoInstanciaDocumento.Devuelto or EstadoInstanciaDocumento.Completado)
                {
                    // El emisor devolvió el documento para que se corrigiera algo en World Office —
                    // el correo nuevo de "Solicitar Aprobación" trae los datos YA corregidos, así que
                    // además de reiniciar el paso hay que refrescar el contenido (si no, quien aprueba
                    // seguiría viendo la versión vieja, la que motivó la devolución).
                    var renglonesFrescos = ConstruirRenglones(dto.Renglones);
                    var valorFresco = CalcularValorConIva(renglonesFrescos, dto.Valor);
                    var formaDePagoFresca = dto.Datos?.GetValueOrDefault("formaDePago");

                    // Bug real (2026-10-19, OC 19640): esto refrescaba Valor/FormaDePago pero
                    // reiniciaba en el MISMO flujo viejo (`existente.DefinicionFlujo`), aunque la
                    // corrección hiciera que ya no correspondiera — pasó de Crédito a Contado por
                    // encima de $300.000 y quedó reiniciada en el flujo de Crédito (2 pasos),
                    // saltándose Contador y Gerencia General por completo. Ahora se vuelve a elegir
                    // el flujo con los datos frescos, igual que en una creación nueva.
                    var flujoFresco = await SeleccionarFlujoAsync(existente.TipoDocumentoId, valorFresco, formaDePagoFresca, ct);
                    existente.DefinicionFlujoId = flujoFresco.Id;
                    // Flujo sin pasos (crédito de Ferrealiados): queda aprobado al llegar, ver más abajo.
                    var primerPasoReenvio = flujoFresco.Pasos.OrderBy(p => p.Orden).FirstOrDefault();
                    var ahoraReenvio = timeProvider.GetUtcNow().UtcDateTime;

                    existente.Renglones.Clear();
                    foreach (var r in renglonesFrescos) existente.Renglones.Add(r);

                    // Bug real (2026-11-04, OC 19857 / Hoja 16109): esto hacía Clear() + copias nuevas —
                    // la copia vieja (con todo el avance de Transporte) quedaba huérfana como si fuera
                    // una Hoja sin OC, y la nueva arrancaba en 0, así que 4 horas después saltaba la
                    // alerta de "sigue incompleta" sobre una Hoja ya completa y remisionada. Ahora la
                    // copia de una Cotización que sigue ligada se CONSERVA tal cual (sus datos se
                    // refrescan solos por RefrescarDesdeWOAsync), la que ya no está ligada se borra de
                    // verdad, y solo se crea copia para una Cotización nueva — heredando el avance de
                    // sus hermanas si la Cotización ya estaba ligada a otra OC.
                    var vinculadosFrescos = ConstruirDocumentosVinculados(dto.DocumentosVinculados);
                    foreach (var v in vinculadosFrescos) v.Empresa = empresa;
                    var clavesFrescas = vinculadosFrescos.Select(v => (v.PrefijoOrigen, v.NumeroReferencia)).ToHashSet();
                    foreach (var vieja in existente.DocumentosVinculados.Where(v => !clavesFrescas.Contains((v.PrefijoOrigen, v.NumeroReferencia))).ToList())
                    {
                        existente.DocumentosVinculados.Remove(vieja);
                        db.DocumentosVinculados.Remove(vieja);
                    }
                    var nuevasCopias = vinculadosFrescos
                        .Where(v => !existente.DocumentosVinculados.Any(e => e.PrefijoOrigen == v.PrefijoOrigen && e.NumeroReferencia == v.NumeroReferencia))
                        .ToList();
                    await HeredarAvanceDeCopiasHermanasAsync(nuevasCopias, ct);
                    foreach (var v in nuevasCopias) existente.DocumentosVinculados.Add(v);

                    existente.Proveedor = dto.Proveedor;
                    existente.FechaDocumento = dto.FechaDocumento;
                    existente.DatosJson = dto.Datos is { Count: > 0 } ? JsonSerializer.Serialize(dto.Datos) : null;
                    existente.Valor = valorFresco;
                    existente.SolicitanteWO = dto.SolicitanteWO;

                    existente.PasoActualId = primerPasoReenvio?.Id;
                    existente.Estado = primerPasoReenvio is null ? EstadoInstanciaDocumento.Completado : EstadoInstanciaDocumento.EnProceso;

                    // Si venía de un Devuelto, el paso 1 pudo haber dejado pendiente una escritura en
                    // WO (ver TipoAccion.Devuelto más abajo) — si el Sincronizador todavía no llegó a
                    // hacerla cuando llega este reenvío, hay que cancelarla: el documento ya no está
                    // devuelto, está de nuevo en proceso. Si venía de Completado, esto ya estaba en
                    // false (se confirmó al escribir la aprobación) — no hace daño repetirlo.
                    existente.PendienteEscrituraWO = false;

                    db.HistorialAcciones.Add(new HistorialAccion
                    {
                        InstanciaDocumentoId = existente.Id,
                        PasoFlujoId = primerPasoReenvio?.Id,
                        UsuarioId = usuarioId,
                        Accion = TipoAccion.Reenviado,
                        Fecha = ahoraReenvio
                    });
                    if (primerPasoReenvio is null)
                        AprobarAutomaticamente(existente, usuarioId, ahoraReenvio);

                    await db.SaveChangesAsync(ct);
                    if (primerPasoReenvio is not null)
                        await NotificarSinRomperAsync(() => notificacionService.NotificarPasoAsync(existente.Id, primerPasoReenvio.Id, ct));
                }

                return await ObtenerAsync(existente.Id, ct);
            }
        }

        var tipoDocumento = await db.TiposDocumento.Include(t => t.Campos)
            .FirstOrDefaultAsync(t => t.Id == dto.TipoDocumentoId && t.Activo, ct)
            ?? throw new KeyNotFoundException("Tipo de documento no encontrado o inactivo.");

        ValidarCamposRequeridos(tipoDocumento.Campos, dto.Datos);

        var renglones = ConstruirRenglones(dto.Renglones);

        // Igual que en World Office: si el documento tiene renglones, el valor sale de sumarlos
        // (fuente de verdad), no del campo tipeado a mano — evita que quede desincronizado. Con
        // IVA incluido (2026-09-23, bug real): "Valor" tiene que ser el total real del documento
        // (el mismo que muestra "TOTAL DOCUMENTO" en el PDF), no el subtotal antes de impuestos —
        // este mismo campo decide a qué flujo entra el documento (corte de $300.000), así que un
        // valor sin IVA podía clasificar mal una OC que con IVA sí cruzaba el umbral.
        var valor = CalcularValorConIva(renglones, dto.Valor);

        var documentosVinculados = ConstruirDocumentosVinculados(dto.DocumentosVinculados);
        foreach (var v in documentosVinculados) v.Empresa = tipoDocumento.Empresa;
        await HeredarAvanceDeCopiasHermanasAsync(documentosVinculados, ct);

        var formaDePago = dto.Datos?.GetValueOrDefault("formaDePago");
        var flujo = await SeleccionarFlujoAsync(dto.TipoDocumentoId, valor, formaDePago, ct);

        // Un flujo SIN pasos es válido (2026-11-13, OC a crédito de Ferrealiados: "pasan derecho a
        // logística sin ninguna aprobación"): el documento queda Completado al llegar, con un
        // AprobadoAutomatico en el historial que libera su Hoja de Alistamiento.
        var primerPaso = flujo.Pasos.OrderBy(p => p.Orden).FirstOrDefault();

        var ahora = timeProvider.GetUtcNow().UtcDateTime;
        var instancia = new InstanciaDocumento
        {
            TipoDocumentoId = tipoDocumento.Id,
            Empresa = tipoDocumento.Empresa,
            DefinicionFlujoId = flujo.Id,
            PasoActualId = primerPaso?.Id,
            Estado = primerPaso is null ? EstadoInstanciaDocumento.Completado : EstadoInstanciaDocumento.EnProceso,
            NumeroReferencia = dto.NumeroReferencia,
            Proveedor = dto.Proveedor,
            Valor = valor,
            FechaDocumento = dto.FechaDocumento,
            DatosJson = dto.Datos is { Count: > 0 } ? JsonSerializer.Serialize(dto.Datos) : null,
            CreadoPorUsuarioId = usuarioId,
            FechaCreacion = ahora,
            Renglones = renglones,
            IdAsientoContableOrigen = dto.IdAsientoContableOrigen,
            PrefijoOrigen = dto.PrefijoOrigen,
            DocumentosVinculados = documentosVinculados,
            SolicitanteWO = dto.SolicitanteWO
        };
        db.InstanciasDocumento.Add(instancia);
        db.HistorialAcciones.Add(new HistorialAccion
        {
            InstanciaDocumento = instancia,
            PasoFlujoId = primerPaso?.Id,
            UsuarioId = usuarioId,
            Accion = TipoAccion.Creado,
            Fecha = ahora
        });
        if (primerPaso is null)
            AprobarAutomaticamente(instancia, usuarioId, ahora);

        await db.SaveChangesAsync(ct);
        if (notificar && primerPaso is not null)
            await NotificarSinRomperAsync(() => notificacionService.NotificarPasoAsync(instancia.Id, primerPaso.Id, ct));

        return await ObtenerAsync(instancia.Id, ct);
    }

    /// <summary>
    /// Flujo sin pasos: aprobado al llegar. Si vino de World Office, además queda pendiente de
    /// reflejar la aprobación allá (igual que cuando alguien aprueba el paso 1).
    /// </summary>
    private void AprobarAutomaticamente(InstanciaDocumento instancia, int usuarioId, DateTime ahora)
    {
        db.HistorialAcciones.Add(new HistorialAccion
        {
            InstanciaDocumento = instancia,
            UsuarioId = usuarioId,
            Accion = TipoAccion.AprobadoAutomatico,
            Comentario = "Flujo sin pasos de aprobación: queda aprobado al llegar.",
            Fecha = ahora
        });
        if (instancia.IdAsientoContableOrigen is not null && instancia.PrefijoOrigen is not null)
            instancia.PendienteEscrituraWO = true;
    }

    /// <summary>
    /// El usuario de WO de una persona EN LA EMPRESA del documento: en Ferrealiados es
    /// UsuarioWOFerrealiados (o, si no lo tiene, el mismo UsuarioWO de Jimaco).
    /// </summary>
    internal static string? UsuarioWOEn(Usuario u, Empresa empresa) =>
        empresa == Empresa.Ferrealiados && !string.IsNullOrWhiteSpace(u.UsuarioWOFerrealiados) ? u.UsuarioWOFerrealiados : u.UsuarioWO;

    /// <summary>Usuarios cuyo usuario de WO (en la empresa del documento) es el solicitante.</summary>
    private IQueryable<Usuario> UsuariosSolicitantes(InstanciaDocumento instancia) =>
        instancia.Empresa == Empresa.Ferrealiados
            ? db.Usuarios.Where(u => u.UsuarioWOFerrealiados == instancia.SolicitanteWO
                || ((u.UsuarioWOFerrealiados == null || u.UsuarioWOFerrealiados == "") && u.UsuarioWO == instancia.SolicitanteWO))
            : db.Usuarios.Where(u => u.UsuarioWO == instancia.SolicitanteWO);

    public async Task<InstanciaDocumentoDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var instancia = await CargarConDetalle().FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");
        return MapearDetalleDto(instancia);
    }

    public async Task<IReadOnlyList<InstanciaDocumentoResumenDto>> ListarPendientesAsync(int usuarioId, CancellationToken ct = default)
    {
        var rolesUsuario = await ObtenerRolesIdsAsync(usuarioId, ct);

        var instancias = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual).ThenInclude(p => p!.PasoFlujoRoles)
            .Where(i => i.Estado == EstadoInstanciaDocumento.EnProceso
                        && i.PasoActual!.PasoFlujoRoles.Any(pr => rolesUsuario.Contains(pr.RolId))
                        // Las órdenes de pago tienen su propio menú ("Pagos", 2026-11-09) — pedido
                        // explícito: no mezclarlas con OC y demás, se presta para confusiones.
                        && i.TipoDocumento.PrefijoWorldOffice != CuentasPorPagarService.PrefijoPagoProveedor)
            .OrderBy(i => i.FechaCreacion)
            .ToListAsync(ct);

        return instancias.Select(MapearResumenDto).ToList();
    }

    public async Task<IReadOnlyList<InstanciaDocumentoResumenDto>> ListarMisDocumentosAsync(int usuarioId, CancellationToken ct = default)
    {
        var yo = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        var usuarioWO = yo?.UsuarioWO;
        var usuarioWOFerrealiados = yo is null ? null : UsuarioWOEn(yo, Empresa.Ferrealiados);

        var instancias = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual)
            .Where(i => (i.CreadoPorUsuarioId == usuarioId
                            || (i.Empresa == Empresa.Jimaco && usuarioWO != null && i.SolicitanteWO == usuarioWO)
                            || (i.Empresa == Empresa.Ferrealiados && usuarioWOFerrealiados != null && i.SolicitanteWO == usuarioWOFerrealiados))
                        && i.TipoDocumento.PrefijoWorldOffice != CuentasPorPagarService.PrefijoPagoProveedor)
            .OrderByDescending(i => i.FechaCreacion)
            .ToListAsync(ct);

        return instancias.Select(MapearResumenDto).ToList();
    }

    // Las OC sincronizadas desde World Office quedan a nombre del servicio Sincronizador; la persona
    // que emitió de verdad es la del SolicitanteWO (mismo criterio que las notificaciones).
    private async Task<bool> EsEmisorAsync(InstanciaDocumento instancia, int usuarioId, CancellationToken ct)
    {
        if (instancia.CreadoPorUsuarioId == usuarioId) return true;
        if (string.IsNullOrWhiteSpace(instancia.SolicitanteWO)) return false;
        return await UsuariosSolicitantes(instancia).AnyAsync(u => u.Id == usuarioId, ct);
    }

    /// <summary>
    /// En un documento "Pago a Proveedor", el paso que exige soporte de pago es el del gerente
    /// general: aprobarlo es confirmar que ya pagó (ver <see cref="ConfirmarPagoProveedorAsync"/>).
    /// </summary>
    private static bool EsConfirmacionPagoProveedor(InstanciaDocumento instancia, PasoFlujo? paso) =>
        paso is { RequiereSoportePagoAlAprobar: true }
        && instancia.TipoDocumento?.PrefijoWorldOffice == CuentasPorPagarService.PrefijoPagoProveedor;

    /// <summary>
    /// Guarda banco y fecha del pago y deja el documento en la cola del Sincronizador para crear el
    /// CE en WO. Valida acá (y no en el Sincronizador) lo que depende de quien aprueba: que eligió un
    /// banco que existe en la lista de WO, y que la fecha no es futura.
    /// </summary>
    private async Task ConfirmarPagoProveedorAsync(InstanciaDocumento instancia, EjecutarAccionDto dto, bool sinSalidaDePlata, CancellationToken ct)
    {
        var pago = instancia.PagoProveedor
            ?? throw new InvalidOperationException("Este pago no tiene los datos del proveedor (documento creado con una versión anterior). Rechazalo y volvé a crearlo desde Cuentas por Pagar.");

        if (pago.NumeroCE is not null)
            throw new InvalidOperationException($"Este pago ya tiene el comprobante {pago.PrefijoCE} {pago.NumeroCE:0} creado en World Office.");

        var hoy = timeProvider.GetUtcNow().UtcDateTime.AddHours(-5).Date;
        var fecha = (dto.FechaPago ?? hoy).Date;
        if (fecha > hoy)
            throw new InvalidOperationException("La fecha de pago no puede ser posterior a hoy.");

        if (sinSalidaDePlata)
        {
            pago.IdCuentaBancoWO = null;
            pago.NombreBanco = null;
        }
        else
        {
            if (dto.IdCuentaBancoWO is not int idBanco)
                throw new InvalidOperationException("Elegí el banco desde donde se hizo el pago.");

            var banco = await db.CuentasBancoWO.FirstOrDefaultAsync(b => b.IdCuentaContableWO == idBanco, ct)
                ?? throw new InvalidOperationException("Ese banco no está en la lista de cuentas de World Office.");

            pago.IdCuentaBancoWO = banco.IdCuentaContableWO;
            pago.NombreBanco = $"{banco.Codigo} {banco.Nombre}";
        }

        pago.FechaPago = fecha;
        pago.PendienteCreacionCE = true;
        pago.ConflictoCreacionCE = null;
    }

    public async Task<InstanciaDocumentoDetalleDto> EjecutarAccionAsync(int id, int usuarioId, EjecutarAccionDto dto, CancellationToken ct = default, bool omitirControlRol = false)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual).ThenInclude(p => p!.PasoFlujoRoles)
            .Include(i => i.PasoActual).ThenInclude(p => p!.DestinosDevolucion).ThenInclude(d => d.PasoFlujoDestino)
            .Include(i => i.DefinicionFlujo).ThenInclude(f => f.Pasos)
            .Include(i => i.PagoProveedor)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        if (instancia.Estado != EstadoInstanciaDocumento.EnProceso || instancia.PasoActual is null)
            throw new InvalidOperationException("El documento no tiene un paso activo pendiente de acción.");

        // "Pago a Proveedor" (2026-11-06): aprobar el paso del soporte de pago ES confirmar el pago
        // real — ahí se elige banco y fecha, y el Sincronizador crea el CE en WO con eso. Si el neto
        // es 0 (los anticipos cubren todo) no sale plata: ni banco ni soporte.
        var confirmaPagoProveedor = EsConfirmacionPagoProveedor(instancia, instancia.PasoActual);
        var pagoSinSalidaDePlata = confirmaPagoProveedor && (instancia.Valor ?? 0) <= 0;

        var paso = instancia.PasoActual;

        if (!omitirControlRol)
        {
            var rolesUsuario = await ObtenerRolesIdsAsync(usuarioId, ct);
            if (!paso.PasoFlujoRoles.Any(pr => rolesUsuario.Contains(pr.RolId)))
                throw new UnauthorizedAccessException("No tenés un rol habilitado para actuar en este paso.");
        }

        var ahora = timeProvider.GetUtcNow().UtcDateTime;

        switch (dto.Accion)
        {
            case TipoAccion.Aprobado:
                if (paso.RequiereComentarioAlAprobar && string.IsNullOrWhiteSpace(dto.Comentario))
                    throw new InvalidOperationException("Este paso exige un comentario para aprobar.");

                if (paso.RequiereSoportePagoAlAprobar && !pagoSinSalidaDePlata)
                {
                    if (dto.SoportePagoAdjuntoId is not int soporteId)
                        throw new InvalidOperationException("Este paso exige adjuntar el soporte de pago (PDF) para aprobar.");

                    var soporte = await db.Adjuntos.FirstOrDefaultAsync(a => a.Id == soporteId, ct)
                        ?? throw new InvalidOperationException("El soporte de pago indicado no existe.");
                    if (soporte.InstanciaDocumentoId != instancia.Id)
                        throw new InvalidOperationException("El soporte de pago no pertenece a este documento.");
                    if (soporte.ContentType != "application/pdf" && !soporte.NombreArchivo.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("El soporte de pago tiene que ser un PDF.");

                    instancia.SoportePagoAdjuntoId = soporte.Id;
                }

                if (paso.RequiereComprobanteEgresoAlAprobar)
                {
                    if (string.IsNullOrWhiteSpace(dto.NumeroComprobanteEgreso))
                        throw new InvalidOperationException("Este paso exige el número del Comprobante de Egreso de World Office para aprobar.");

                    // El paso avanza igual, ya — no se traba a quien aprueba esperando al
                    // Sincronizador. Lo que se pospone es la NOTIFICACIÓN al paso siguiente (ver
                    // NotificarSegunAccionAsync), hasta que el comprobante realmente llegue.
                    instancia.NumeroComprobanteEgresoSolicitado = dto.NumeroComprobanteEgreso.Trim();
                    instancia.PendienteBusquedaComprobanteEgresoWO = true;
                    instancia.ConflictoComprobanteEgresoWO = null;
                }

                if (confirmaPagoProveedor)
                    await ConfirmarPagoProveedorAsync(instancia, dto, pagoSinSalidaDePlata, ct);

                var siguiente = instancia.DefinicionFlujo.Pasos
                    .Where(p => p.Orden > paso.Orden)
                    .OrderBy(p => p.Orden)
                    .FirstOrDefault();
                if (siguiente is null)
                {
                    instancia.Estado = EstadoInstanciaDocumento.Completado;
                    instancia.PasoActualId = null;
                }
                else
                {
                    instancia.PasoActualId = siguiente.Id;
                }

                // Se aprobó el primer paso de CUALQUIER flujo (genérico, no específico de OC) —
                // si el documento vino sincronizado desde World Office, queda pendiente de
                // reflejar esa aprobación allá. No se revierte si un paso posterior rechaza o
                // devuelve el documento — la aprobación de este primer paso ya fue real.
                if (paso.Orden == 1 && instancia.IdAsientoContableOrigen is not null)
                    instancia.PendienteEscrituraWO = true;

                break;

            case TipoAccion.Devuelto:
                if (!paso.PermiteDevolver)
                    throw new InvalidOperationException("Este paso no permite devolver el documento.");
                if (string.IsNullOrWhiteSpace(dto.Comentario))
                    throw new InvalidOperationException("Debés indicar un motivo para devolver el documento.");

                // Las opciones de destino se combinan (2026-09-21): "el emisor" (si
                // PermiteDevolverAlEmisor) más cualquier paso anterior puntual configurado. Con una
                // sola opción en total no hace falta elegir — se usa esa directo.
                var opcionesDestino = new List<int>();
                if (paso.PermiteDevolverAlEmisor) opcionesDestino.Add(DestinoEmisorId);
                opcionesDestino.AddRange(paso.DestinosDevolucion.Select(d => d.PasoFlujoDestinoId));

                if (opcionesDestino.Count == 0)
                    throw new InvalidOperationException("Este paso no tiene ningún destino de devolución configurado — contactá al administrador.");

                int destinoElegido;
                if (dto.PasoDestinoId is int elegido)
                {
                    if (!opcionesDestino.Contains(elegido))
                        throw new InvalidOperationException("Ese destino no está permitido desde este paso.");
                    destinoElegido = elegido;
                }
                else if (opcionesDestino.Count == 1)
                {
                    destinoElegido = opcionesDestino[0];
                }
                else
                {
                    throw new InvalidOperationException("Este paso tiene más de un destino posible de devolución — elegí a cuál.");
                }

                if (destinoElegido == DestinoEmisorId)
                {
                    instancia.Estado = EstadoInstanciaDocumento.Devuelto;
                    instancia.PasoActualId = null;

                    // Mismo criterio que la aprobación del primer paso: si el documento vino de
                    // World Office, queda pendiente de reflejar allá — acá como "vuelve a
                    // pendiente" (senAprobado=NULL, ver WorldOfficeWriter), NO como rechazado: la
                    // intención de "Devolver" es "corregí esto en WO y volvé a mandarlo", así que
                    // WO tiene que mostrarlo de nuevo "En espera de aprobación", no como algo
                    // terminado (2026-09-16, para eso está el botón "Rechazar" aparte). Solo aplica
                    // cuando el paso 1 devuelve DE VERDAD al emisor — un paso posterior que también
                    // elige "el emisor" hace lo mismo puertas adentro (Estado=Devuelto), pero WO ya
                    // reflejó la aprobación del paso 1 hace rato, no hay nada que reescribir ahí.
                    if (paso.Orden == 1 && instancia.IdAsientoContableOrigen is not null)
                        instancia.PendienteEscrituraWO = true;
                }
                else
                {
                    instancia.PasoActualId = destinoElegido;
                }
                break;

            case TipoAccion.Rechazado:
                if (!paso.PermiteRechazar)
                    throw new InvalidOperationException("Este paso no permite rechazar el documento.");
                if (string.IsNullOrWhiteSpace(dto.Comentario))
                    throw new InvalidOperationException("Debés indicar un motivo para rechazar el documento.");
                instancia.Estado = EstadoInstanciaDocumento.Rechazado;
                instancia.PasoActualId = null;

                // A diferencia de Devolver (que ahora vuelve a pendiente en WO), Rechazar SÍ es
                // terminal — mismo resultado que el botón nativo "Rechazar" de WO (senAprobado=2).
                if (paso.Orden == 1 && instancia.IdAsientoContableOrigen is not null)
                    instancia.PendienteEscrituraWO = true;
                break;

            case TipoAccion.RedirigidoAContado:
                if (paso.PasoRedirigirAPagoContadoId is not int destinoContado)
                    throw new InvalidOperationException("Este paso no permite redirigir a pago en efectivo.");
                if (string.IsNullOrWhiteSpace(dto.Comentario))
                    throw new InvalidOperationException("Debés indicar el motivo del cambio a pago en efectivo.");

                // Hacia ADELANTE, no hacia atrás: el documento sigue EnProceso y retoma su curso
                // normal desde el paso destino (que a su vez avanza por Orden como cualquier otro).
                instancia.PasoActualId = destinoContado;

                if (paso.Orden == 1 && instancia.IdAsientoContableOrigen is not null)
                    instancia.PendienteEscrituraWO = true;
                break;

            default:
                throw new InvalidOperationException($"Acción \"{dto.Accion}\" no soportada en este paso.");
        }

        db.HistorialAcciones.Add(new HistorialAccion
        {
            InstanciaDocumentoId = instancia.Id,
            PasoFlujoId = paso.Id,
            UsuarioId = usuarioId,
            Accion = dto.Accion,
            Comentario = dto.Comentario,
            Fecha = ahora
        });

        await db.SaveChangesAsync(ct);
        await NotificarSegunAccionAsync(instancia, dto, ct);

        // Pago real ya hecho (Gerencia General aprobó con el soporte adjunto) — avisar a
        // Contabilidad y al emisor real con el PDF pegado, aparte del aviso normal de "avanzó de
        // paso" que ya disparó NotificarSegunAccionAsync arriba.
        if (dto.Accion == TipoAccion.Aprobado && paso.RequiereSoportePagoAlAprobar && instancia.SoportePagoAdjuntoId is int soportePagoId)
        {
            var pasoContabilidad = instancia.DefinicionFlujo.Pasos.FirstOrDefault(p => p.RequiereComprobanteEgresoAlAprobar);
            if (pasoContabilidad is not null)
                await NotificarSinRomperAsync(() => notificacionService.NotificarPagoRealizadoAsync(instancia.Id, pasoContabilidad.Id, soportePagoId, ct));
        }

        // Aviso aparte, a quien mandó el documento a aprobar en World Office (Datos.elaboradoPor
        // es OTRA persona — la que armó la OC, no necesariamente la misma) — solo tiene sentido
        // para el resultado del primer paso, y solo si de verdad salió del flujo hacia el emisor
        // (Devuelto con destino intermedio no cuenta, ver arriba).
        if (paso.Orden == 1 && (dto.Accion == TipoAccion.Aprobado
                                 || instancia.Estado is EstadoInstanciaDocumento.Devuelto or EstadoInstanciaDocumento.Rechazado))
            await NotificarSinRomperAsync(() => NotificarSolicitanteAsync(instancia, dto.Accion, dto.Comentario, ct));

        return await ObtenerAsync(instancia.Id, ct);
    }

    /// <summary>
    /// Le avisa a quien mandó el documento a aprobar en World Office (<see
    /// cref="InstanciaDocumento.SolicitanteWO"/>) el resultado del primer paso — requiere que haya
    /// un <see cref="Usuario"/> registrado acá con ese mismo <see cref="Usuario.UsuarioWO"/>; si no
    /// hay ninguno, no hay a quién mandarle el correo, y no se hace nada (no es un error).
    /// </summary>
    private async Task NotificarSolicitanteAsync(InstanciaDocumento instancia, TipoAccion accion, string? comentario, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instancia.SolicitanteWO)) return;

        var solicitante = await UsuariosSolicitantes(instancia).FirstOrDefaultAsync(ct);
        if (solicitante is null) return;

        // Devuelto/Rechazado ya le llega a esa misma persona como aviso interno (con el motivo y
        // dónde corregirlo), así que no se repite por correo aparte.
        if (accion is TipoAccion.Devuelto or TipoAccion.Rechazado) return;

        var desc = DescripcionDocumento(instancia);
        var (asunto, mensaje) = accion switch
        {
            TipoAccion.Aprobado =>
                ($"[Jimaco Aprobaciones] Tu documento {desc} fue aprobado",
                 $"El primer paso de aprobación de tu documento {desc} fue aprobado."),
            TipoAccion.Rechazado =>
                ($"[Jimaco Aprobaciones] Tu documento {desc} fue rechazado",
                 $"Tu documento {desc} fue rechazado. Motivo: {comentario}. Esta decisión es definitiva."),
            _ =>
                ($"[Jimaco Aprobaciones] Tu documento {desc} fue devuelto",
                 $"Tu documento {desc} fue devuelto. Motivo: {comentario}. Corregilo en World Office y volvé a solicitar la aprobación.")
        };

        await notificacionService.NotificarUsuarioAsync(instancia.Id, solicitante.Id, asunto, mensaje, ct);
    }

    // Las OC sincronizadas quedan a nombre del Sincronizador; el aviso interno de devolución o
    // rechazo tiene que llegarle a quien las emitió de verdad (SolicitanteWO), no al servicio.
    private async Task<int> EmisorInternoIdAsync(InstanciaDocumento instancia, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(instancia.SolicitanteWO))
        {
            var real = await UsuariosSolicitantes(instancia)
                .Where(u => u.Activo)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync(ct);
            if (real is int id) return id;
        }
        return instancia.CreadoPorUsuarioId;
    }

    /// <summary>"{TipoDocumento} {NumeroReferencia}" (ej. "Orden de Compra 19205") — usado en todos
    /// los asuntos/cuerpos de correo para que quien lo lee sepa de qué documento se trata sin tener
    /// que entrar al sistema.</summary>
    private static string DescripcionDocumento(InstanciaDocumento instancia) =>
        string.IsNullOrWhiteSpace(instancia.NumeroReferencia)
            ? instancia.TipoDocumento.Nombre
            : $"{instancia.TipoDocumento.Nombre} {instancia.NumeroReferencia}";

    private async Task NotificarSegunAccionAsync(InstanciaDocumento instancia, EjecutarAccionDto dto, CancellationToken ct)
    {
        var desc = DescripcionDocumento(instancia);
        var emisorId = await EmisorInternoIdAsync(instancia, ct);

        switch (dto.Accion)
        {
            // Si quedó pendiente de buscar el Comprobante de Egreso, la notificación se pospone
            // hasta que AdjuntarComprobanteEgresoAsync la dispare — no tiene sentido avisarle al
            // paso siguiente (ni cerrar el flujo) mostrando un documento sin el soporte todavía.
            case TipoAccion.Aprobado when instancia.PendienteBusquedaComprobanteEgresoWO:
                break;

            // Mismo criterio para "Pago a Proveedor": el aviso sale cuando el CE ya existe en WO y
            // quedó adjunto (ConfirmarCECreadoAsync → búsqueda normal del CE → AdjuntarComprobanteEgresoAsync).
            case TipoAccion.Aprobado when instancia.PagoProveedor?.PendienteCreacionCE == true:
                break;

            case TipoAccion.Aprobado:
                await NotificarAvanceTrasAprobarAsync(instancia, ct);
                break;

            case TipoAccion.Devuelto when instancia.PasoActualId is int destinoId:
                await NotificarSinRomperAsync(() => notificacionService.NotificarPasoAsync(instancia.Id, destinoId, ct));
                break;

            case TipoAccion.Devuelto: // sin paso destino = vuelve al emisor
                await NotificarSinRomperAsync(() => notificacionService.NotificarUsuarioAsync(
                    instancia.Id, emisorId, $"[Jimaco Aprobaciones] Tu documento {desc} fue devuelto",
                    $"Tu documento {desc} fue devuelto. Motivo: {dto.Comentario}. Corregilo y reenvialo desde \"Mis documentos\".", ct));
                break;

            // Orden de pago rechazada desde el menú "Pagos": el aviso detallado (tercero, facturas,
            // motivo) se lo manda PagosService.RechazarAsync a Gerencia Comercial.
            case TipoAccion.Rechazado when instancia.PagoProveedor is not null:
                break;

            case TipoAccion.Rechazado:
                await NotificarSinRomperAsync(() => notificacionService.NotificarUsuarioAsync(
                    instancia.Id, emisorId, $"[Jimaco Aprobaciones] Tu documento {desc} fue rechazado",
                    $"Tu documento {desc} fue rechazado. Motivo: {dto.Comentario}.", ct));
                break;

            case TipoAccion.RedirigidoAContado when instancia.PasoActualId is int destinoId:
                await NotificarSinRomperAsync(() => notificacionService.NotificarPasoAsync(instancia.Id, destinoId, ct));

                // Avisar a Contabilidad (si este flujo tiene un paso que exige Comprobante de
                // Egreso y ya se pidió uno) que ese comprobante no se va a usar — ver caso real
                // 2026-09-25: Gerencia General redirige a pago de contado después de que
                // Contabilidad ya había generado el comprobante pensado para la transferencia.
                var pasoContabilidad = instancia.DefinicionFlujo.Pasos.FirstOrDefault(p => p.RequiereComprobanteEgresoAlAprobar);
                if (pasoContabilidad is not null && !string.IsNullOrWhiteSpace(instancia.NumeroComprobanteEgresoSolicitado))
                {
                    await NotificarSinRomperAsync(() => notificacionService.NotificarComprobanteEgresoNoUtilizadoAsync(
                        instancia.Id, pasoContabilidad.Id, instancia.NumeroComprobanteEgresoSolicitado, dto.Comentario ?? "", ct));
                }
                break;
        }
    }

    /// <summary>
    /// Avisa del avance normal tras un Aprobado — al paso siguiente si quedó uno, o a quien creó el
    /// documento si ya quedó Completado. Separado de <see cref="NotificarSegunAccionAsync"/> para
    /// poder dispararlo también desde <see cref="AdjuntarComprobanteEgresoAsync"/>, cuando la
    /// notificación se pospuso esperando el Comprobante de Egreso (ver
    /// InstanciaDocumento.PendienteBusquedaComprobanteEgresoWO).
    /// </summary>
    private async Task NotificarAvanceTrasAprobarAsync(InstanciaDocumento instancia, CancellationToken ct)
    {
        if (instancia.PasoActualId is int siguientePasoId)
        {
            await NotificarSinRomperAsync(() => notificacionService.NotificarPasoAsync(instancia.Id, siguientePasoId, ct));
        }
        else
        {
            var desc = DescripcionDocumento(instancia);
            var emisorId = await EmisorInternoIdAsync(instancia, ct);
            await NotificarSinRomperAsync(() => notificacionService.NotificarUsuarioAsync(
                instancia.Id, emisorId, $"[Jimaco Aprobaciones] Tu documento {desc} fue aprobado en todos los pasos",
                $"Tu documento {desc} completó todo el flujo de aprobación.", ct));
        }
    }

    public async Task<InstanciaDocumentoDetalleDto> ReenviarAsync(int id, int usuarioId, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.DefinicionFlujo).ThenInclude(f => f.Pasos)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        if (instancia.Estado != EstadoInstanciaDocumento.Devuelto)
            throw new InvalidOperationException("Solo se puede reenviar un documento que fue devuelto.");

        if (!await EsEmisorAsync(instancia, usuarioId, ct))
            throw new UnauthorizedAccessException("Solo quien emitió el documento puede reenviarlo.");

        var primerPaso = instancia.DefinicionFlujo.Pasos.OrderBy(p => p.Orden).First();

        instancia.PasoActualId = primerPaso.Id;
        instancia.Estado = EstadoInstanciaDocumento.EnProceso;

        db.HistorialAcciones.Add(new HistorialAccion
        {
            InstanciaDocumentoId = instancia.Id,
            PasoFlujoId = primerPaso.Id,
            UsuarioId = usuarioId,
            Accion = TipoAccion.Reenviado,
            Fecha = timeProvider.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(ct);
        await NotificarSinRomperAsync(() => notificacionService.NotificarPasoAsync(instancia.Id, primerPaso.Id, ct));

        return await ObtenerAsync(instancia.Id, ct);
    }

    public async Task<ReenvioNotificacionResultadoDto> ReenviarNotificacionAsync(int id, int usuarioId, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento.FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        if (!await EsEmisorAsync(instancia, usuarioId, ct))
            throw new UnauthorizedAccessException("Solo quien emitió el documento puede reenviar su notificación.");

        if (instancia.Estado != EstadoInstanciaDocumento.EnProceso || instancia.PasoActualId is null)
            throw new InvalidOperationException("El documento no tiene un paso activo pendiente de notificar.");

        // NotificarPasoAsync nunca lanza excepción por un envío fallido (queda registrado como
        // Fallida en Notificacion, sin romper nada) — por eso, a diferencia del envío automático de
        // Crear/EjecutarAccion, acá leemos las filas que se acaban de crear para poder devolverle a
        // quien pidió el reenvío si de verdad salió o no.
        var antes = timeProvider.GetUtcNow().UtcDateTime;
        await notificacionService.NotificarPasoAsync(instancia.Id, instancia.PasoActualId.Value, ct);

        var nuevas = await db.Notificaciones
            .Where(n => n.InstanciaDocumentoId == instancia.Id && n.FechaCreacion >= antes)
            .ToListAsync(ct);

        return new ReenvioNotificacionResultadoDto(
            nuevas.Count(n => n.Estado == EstadoNotificacion.Enviada),
            nuevas.Count(n => n.Estado == EstadoNotificacion.Fallida));
    }

    // ---- Buscar y devolver cualquier documento (2026-09-23) — permiso administrativo transversal,
    // ver Rol.PuedeDevolverCualquierDocumento. ----

    public async Task<IReadOnlyList<InstanciaDocumentoResumenDto>> BuscarPorNumeroReferenciaAsync(string numeroReferencia, int usuarioId, CancellationToken ct = default)
    {
        await ExigirPermisoDevolverCualquieraAsync(usuarioId, ct);

        if (string.IsNullOrWhiteSpace(numeroReferencia)) return [];

        var instancias = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual)
            .Where(i => i.NumeroReferencia != null && i.NumeroReferencia.Contains(numeroReferencia))
            .OrderByDescending(i => i.FechaCreacion)
            .Take(50)
            .ToListAsync(ct);

        return instancias.Select(MapearResumenDto).ToList();
    }

    public async Task<InstanciaDocumentoDetalleDto> DevolverForzadoAsync(int id, int usuarioId, DevolverForzadoDto dto, CancellationToken ct = default)
    {
        await ExigirPermisoDevolverCualquieraAsync(usuarioId, ct);

        if (string.IsNullOrWhiteSpace(dto.Comentario))
            throw new InvalidOperationException("Debés indicar un motivo para devolver el documento.");

        var instancia = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        if (instancia.Estado != EstadoInstanciaDocumento.EnProceso || instancia.PasoActual is null)
            throw new InvalidOperationException("El documento no tiene un paso activo — no se puede devolver.");

        var paso = instancia.PasoActual;
        var ahora = timeProvider.GetUtcNow().UtcDateTime;

        instancia.Estado = EstadoInstanciaDocumento.Devuelto;
        instancia.PasoActualId = null;

        // A diferencia de la devolución normal (que solo escribe en WO si el paso 1 es el que
        // devuelve), esta acción administrativa SIEMPRE deja el documento "en espera de aprobación"
        // en World Office, sin importar en qué paso estaba — es la única forma de que la persona lo
        // pueda editar allá y relanzarlo (ver Rol.PuedeDevolverCualquierDocumento).
        if (instancia.IdAsientoContableOrigen is not null)
            instancia.PendienteEscrituraWO = true;

        db.HistorialAcciones.Add(new HistorialAccion
        {
            InstanciaDocumentoId = instancia.Id,
            PasoFlujoId = paso.Id,
            UsuarioId = usuarioId,
            Accion = TipoAccion.Devuelto,
            Comentario = dto.Comentario,
            Fecha = ahora
        });

        await db.SaveChangesAsync(ct);

        var desc = DescripcionDocumento(instancia);
        var emisorId = await EmisorInternoIdAsync(instancia, ct);
        await NotificarSinRomperAsync(() => notificacionService.NotificarUsuarioAsync(
            instancia.Id, emisorId, $"[Jimaco Aprobaciones] Tu documento {desc} fue devuelto",
            $"Tu documento {desc} fue devuelto. Motivo: {dto.Comentario}. Corregilo y reenvialo desde \"Mis documentos\".", ct));

        // Mismo criterio que la devolución del primer paso: si alguien mandó este documento a
        // aprobar en World Office, también se le avisa ahí (además del emisor interno) — acá
        // siempre aplica, sin condicionar a paso.Orden==1, porque esta acción SIEMPRE actúa como si
        // fuera el primer paso a efectos de World Office.
        await NotificarSinRomperAsync(() => NotificarSolicitanteAsync(instancia, TipoAccion.Devuelto, dto.Comentario, ct));

        return await ObtenerAsync(instancia.Id, ct);
    }

    /// <summary>Lanza si ninguno de los roles activos del usuario tiene <see cref="Rol.PuedeDevolverCualquierDocumento"/>.</summary>
    private async Task ExigirPermisoDevolverCualquieraAsync(int usuarioId, CancellationToken ct)
    {
        var tienePermiso = await db.UsuarioRoles
            .Where(ur => ur.UsuarioId == usuarioId)
            .AnyAsync(ur => ur.Rol.Activo && ur.Rol.PuedeDevolverCualquierDocumento, ct);

        if (!tienePermiso)
            throw new UnauthorizedAccessException("No tenés permiso para buscar/devolver cualquier documento.");
    }

    public async Task<IReadOnlyList<PendienteEscrituraWODto>> ListarPendientesEscrituraWOAsync(Empresa empresa = Empresa.Jimaco, CancellationToken ct = default)
    {
        // Cada Sincronizador escribe SOLO en el World Office de su empresa.
        var pendientes = await db.InstanciasDocumento
            .Include(i => i.Historial).ThenInclude(h => h.PasoFlujo)
            .Include(i => i.Historial).ThenInclude(h => h.Usuario)
            .Where(i => i.Empresa == empresa && i.PendienteEscrituraWO && i.IdAsientoContableOrigen != null && i.PrefijoOrigen != null)
            .ToListAsync(ct);

        return pendientes.Select(i =>
        {
            // El Estado actual ya distingue los tres resultados posibles del primer paso sin
            // necesitar un campo aparte — Devuelto y Rechazado son casos DISTINTOS a propósito
            // (ver ResultadoPrimerPaso): Devuelto vuelve a "pendiente" en WO, Rechazado es terminal.
            var (resultado, accionBuscada) = i.Estado switch
            {
                EstadoInstanciaDocumento.Devuelto => (ResultadoPrimerPaso.Devuelto, TipoAccion.Devuelto),
                EstadoInstanciaDocumento.Rechazado => (ResultadoPrimerPaso.Rechazado, TipoAccion.Rechazado),
                _ => (ResultadoPrimerPaso.Aprobado, TipoAccion.Aprobado)
            };

            // El usuario a escribir en WO es quien tomó esa acción en el primer paso (Orden 1) acá
            // — no necesariamente quien creó el documento (ese fue el Sincronizador). Para Devuelto
            // no se exige Orden==1: además de la devolución normal del primer paso, puede venir de
            // la devolución forzada administrativa (ver DevolverForzadoAsync), que aplica desde
            // CUALQUIER paso y también debe reflejarse en WO — se toma la más reciente sin filtrar
            // por paso.
            var accionPrimerPaso = i.Historial
                .Where(h => h.Accion == accionBuscada && (accionBuscada == TipoAccion.Devuelto || h.PasoFlujo?.Orden == 1))
                .MaxBy(h => h.Fecha);

            var usuarioWO = accionPrimerPaso is null ? null : UsuarioWOEn(accionPrimerPaso.Usuario, i.Empresa);
            if (string.IsNullOrWhiteSpace(usuarioWO))
                usuarioWO = "JIMACO APROBACIONES"; // respaldo si esa persona no tiene usuario de WO configurado

            return new PendienteEscrituraWODto(i.Id, i.IdAsientoContableOrigen!.Value, i.PrefijoOrigen!, i.NumeroReferencia, usuarioWO, resultado);
        }).ToList();
    }

    public async Task ConfirmarEscrituraWOAsync(int instanciaDocumentoId, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento.FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        instancia.PendienteEscrituraWO = false;
        instancia.ConflictoWO = null;
        instancia.FechaEscrituraWO = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReportarConflictoWOAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento.FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        // Se saca de la cola a propósito (no reintentar indefinidamente algo que ya sabemos que
        // va a volver a chocar) — queda visible en el detalle del documento vía ConflictoWO.
        instancia.PendienteEscrituraWO = false;
        instancia.ConflictoWO = mensaje;
        await db.SaveChangesAsync(ct);
    }

    // ---- Comprobante de Egreso (2026-09-22) — mismo patrón "pedir y recoger" que arriba: el
    // Sincronizador revisa ListarPendientesComprobanteEgresoAsync en cada corrida, busca el
    // documento en WO, y empuja el resultado vía AdjuntarComprobanteEgresoAsync (o reporta que no
    // lo encontró vía ReportarConflictoComprobanteEgresoAsync). ----

    public async Task<IReadOnlyList<PendienteComprobanteEgresoDto>> ListarPendientesComprobanteEgresoAsync(Empresa empresa = Empresa.Jimaco, CancellationToken ct = default)
    {
        var pendientes = await db.InstanciasDocumento
            .Where(i => i.Empresa == empresa && i.PendienteBusquedaComprobanteEgresoWO && i.NumeroComprobanteEgresoSolicitado != null)
            .ToListAsync(ct);

        return pendientes.Select(i => new PendienteComprobanteEgresoDto(i.Id, i.NumeroComprobanteEgresoSolicitado!)).ToList();
    }

    public async Task AdjuntarComprobanteEgresoAsync(int instanciaDocumentoId, ComprobanteEgresoEncontradoDto dto, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.ComprobanteEgreso)
            .Include(i => i.PagoProveedor)
            .FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        // Si ya había un comprobante adjunto de un intento anterior (ej. Contabilidad corrigió el
        // número y volvió a aprobar), se reemplaza entero — EF borra el anterior en cascada al
        // quitarlo de la navegación (ver AppDbContext: ComprobanteEgreso 1:1, cascade delete).
        if (instancia.ComprobanteEgreso is not null)
            db.ComprobantesEgreso.Remove(instancia.ComprobanteEgreso);

        var comprobante = new ComprobanteEgreso
        {
            InstanciaDocumentoId = instancia.Id,
            NumeroReferencia = dto.NumeroReferencia,
            Fecha = dto.Fecha,
            Valor = dto.Valor,
            Beneficiario = dto.Beneficiario,
            NitBeneficiario = dto.NitBeneficiario,
            Direccion = dto.Direccion,
            Ciudad = dto.Ciudad,
            Telefono = dto.Telefono,
            Concepto = dto.Concepto,
            ElaboradoPor = dto.ElaboradoPor,
            Renglones = dto.Renglones.Select((r, idx) => new RenglonComprobanteEgreso
            {
                Orden = idx,
                CodigoCuenta = r.CodigoCuenta,
                NombreCuenta = r.NombreCuenta,
                Concepto = r.Concepto,
                Tercero = r.Tercero,
                Debito = r.Debito,
                Credito = r.Credito
            }).ToList()
        };
        db.ComprobantesEgreso.Add(comprobante);

        instancia.PendienteBusquedaComprobanteEgresoWO = false;
        instancia.ConflictoComprobanteEgresoWO = null;
        await db.SaveChangesAsync(ct);

        // Orden pagada dentro de un lote (2026-11-09): nada de un correo por orden — sale UNO solo
        // cuando el lote entero terminó (todos los CE creados o con conflicto).
        if (instancia.PagoProveedor?.LotePagoId is int lotePagoId)
        {
            await NotificarLotePagoSiTerminoAsync(lotePagoId, ct);
            return;
        }

        // Ahora sí — el comprobante ya está adjunto, se dispara la notificación al paso siguiente
        // (o al emisor si quedó Completado) que NotificarSegunAccionAsync se había saltado.
        await NotificarAvanceTrasAprobarAsync(instancia, ct);
    }

    public async Task NotificarLotePagoSiTerminoAsync(int lotePagoId, CancellationToken ct = default)
    {
        var lote = await db.LotesPago
            .Include(l => l.Pagos).ThenInclude(p => p.InstanciaDocumento).ThenInclude(i => i.ComprobanteEgreso)
            .FirstOrDefaultAsync(l => l.Id == lotePagoId, ct);
        if (lote is null || lote.AvisoTerminadoEnviado) return;

        // Terminado = cada orden tiene su CE ya adjunto, o quedó con un conflicto (que ya avisó aparte).
        var terminado = lote.Pagos.All(p => p.InstanciaDocumento.ComprobanteEgreso is not null || p.ConflictoCreacionCE is not null);
        if (!terminado) return;

        lote.AvisoTerminadoEnviado = true;
        await db.SaveChangesAsync(ct);

        var filas = string.Concat(lote.Pagos.OrderBy(p => p.NombreTercero ?? p.InstanciaDocumento.Proveedor).Select(p =>
        {
            var ce = p.ConflictoCreacionCE is not null
                ? $"<span style=\"color:#b3261e\">Sin CE: {System.Net.WebUtility.HtmlEncode(p.ConflictoCreacionCE)}</span>"
                : $"{p.PrefijoCE} {p.NumeroCE:0}";
            return $"<tr><td>{System.Net.WebUtility.HtmlEncode(p.NombreTercero ?? p.InstanciaDocumento.Proveedor ?? p.Identificacion)}</td>" +
                   $"<td style=\"text-align:right\">{(p.InstanciaDocumento.Valor ?? 0):N0}</td><td>{ce}</td></tr>";
        }));
        var conflictos = lote.Pagos.Count(p => p.ConflictoCreacionCE is not null);
        var asunto = $"[Jimaco Aprobaciones] Lote de pago {lote.Numero} pagado" + (conflictos > 0 ? $" ({conflictos} sin comprobante)" : "");
        var cuerpo = $"<p>El lote de pago <b>{lote.Numero}</b> ({lote.FechaPago:dd/MM/yyyy}, {System.Net.WebUtility.HtmlEncode(lote.NombreBanco ?? "sin banco")}) " +
                     $"quedó registrado en World Office por <b>${lote.Total:N0}</b>.</p>" +
                     "<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse\">" +
                     $"<tr><th>Tercero</th><th>Valor</th><th>Comprobante</th></tr>{filas}</table>";

        var destinatarios = lote.Pagos.Select(p => p.InstanciaDocumento.CreadoPorUsuarioId)
            .Append(lote.PagadoPorUsuarioId)
            .Distinct()
            .ToList();
        foreach (var usuarioId in destinatarios)
            await NotificarSinRomperAsync(() => notificacionService.NotificarHtmlAsync(
                lote.Pagos.First().InstanciaDocumentoId, usuarioId, asunto, cuerpo, "/pagos", ct));
    }

    public async Task ReportarConflictoComprobanteEgresoAsync(int instanciaDocumentoId, string mensaje, CancellationToken ct = default)
    {
        var instancia = await db.InstanciasDocumento.FirstOrDefaultAsync(i => i.Id == instanciaDocumentoId, ct)
            ?? throw new KeyNotFoundException("Documento no encontrado.");

        // Igual que ReportarConflictoWOAsync: se saca de la cola (no reintentar indefinidamente algo
        // que ya sabemos que va a volver a fallar) — queda visible en el detalle vía
        // ConflictoComprobanteEgresoWO, y la notificación al paso siguiente sigue pospuesta hasta
        // que Contabilidad corrija el número y vuelva a aprobar (lo que llama de nuevo a
        // EjecutarAccionAsync y reactiva PendienteBusquedaComprobanteEgresoWO).
        var numeroSolicitado = instancia.NumeroComprobanteEgresoSolicitado;
        instancia.PendienteBusquedaComprobanteEgresoWO = false;
        instancia.ConflictoComprobanteEgresoWO = mensaje;
        await db.SaveChangesAsync(ct);

        // A diferencia de un conflicto de escritura en WO (que queda visible pero mudo, ver
        // ReportarConflictoWOAsync), acá SÍ avisamos — a quien tiene el documento ahora (ej.
        // Gerencia General), no a Contabilidad: el paso de Contabilidad ya se resolvió, no tiene
        // ninguna acción pendiente sobre este documento hasta que alguien se lo devuelva. Sin este
        // aviso, el documento queda esperando en silencio un correo que nunca va a llegar (2026-09-22).
        if (instancia.PasoActualId is int pasoActualId && numeroSolicitado is not null)
            await NotificarSinRomperAsync(() => notificacionService.NotificarConflictoComprobanteEgresoAsync(instancia.Id, pasoActualId, numeroSolicitado, mensaje, ct));
    }

    public async Task<AdjuntoDto> AgregarAdjuntoAsync(int id, string nombreArchivo, string? contentType, Stream contenido, int usuarioId, bool esSoportePago = false, CancellationToken ct = default)
    {
        if (!await db.InstanciasDocumento.AnyAsync(i => i.Id == id, ct))
            throw new KeyNotFoundException("Documento no encontrado.");

        if (esSoportePago && contentType != "application/pdf" && !nombreArchivo.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El soporte de pago tiene que ser un PDF.");

        var rutaArchivo = await almacenamiento.GuardarAsync(nombreArchivo, contenido, ct);

        var adjunto = new Adjunto
        {
            InstanciaDocumentoId = id,
            NombreArchivo = nombreArchivo,
            RutaArchivo = rutaArchivo,
            ContentType = contentType,
            TamanoBytes = contenido.Length,
            SubidoPorUsuarioId = usuarioId,
            FechaCarga = timeProvider.GetUtcNow().UtcDateTime
        };
        db.Adjuntos.Add(adjunto);
        await db.SaveChangesAsync(ct);

        return new AdjuntoDto(adjunto.Id, adjunto.NombreArchivo, adjunto.ContentType, adjunto.TamanoBytes, adjunto.FechaCarga);
    }

    public async Task<(Stream Contenido, string NombreArchivo, string? ContentType)> DescargarAdjuntoAsync(int adjuntoId, CancellationToken ct = default)
    {
        var adjunto = await db.Adjuntos.FirstOrDefaultAsync(a => a.Id == adjuntoId, ct)
            ?? throw new KeyNotFoundException("Adjunto no encontrado.");

        var contenido = await almacenamiento.AbrirAsync(adjunto.RutaArchivo, ct);
        return (contenido, adjunto.NombreArchivo, adjunto.ContentType);
    }

    private static List<RenglonInstanciaDocumento> ConstruirRenglones(IReadOnlyList<RenglonInputDto>? renglones) =>
        (renglones ?? []).Select((r, indice) => new RenglonInstanciaDocumento
        {
            Orden = indice + 1,
            Codigo = r.Codigo,
            Descripcion = r.Descripcion,
            Cantidad = r.Cantidad,
            UnidadMedida = r.UnidadMedida,
            ValorUnitario = r.ValorUnitario,
            PorcentajeIva = r.PorcentajeIva,
            Total = r.Cantidad * r.ValorUnitario
        }).ToList();

    /// <summary>
    /// El valor real del documento (subtotal + IVA), mismo criterio que "TOTAL DOCUMENTO" en
    /// <see cref="DocumentoPdfGenerador"/> — <see cref="RenglonInstanciaDocumento.Total"/> es el
    /// subtotal por renglón (sin impuesto, coincide con la columna "Total" de la tabla), así que
    /// hay que sumarle el IVA de cada renglón aparte. Sin renglones (carga manual sin detalle), se
    /// usa el valor tipeado a mano tal cual, sin poder calcularle IVA.
    /// </summary>
    private static decimal? CalcularValorConIva(IReadOnlyList<RenglonInstanciaDocumento> renglones, decimal? valorSinRenglones)
    {
        if (renglones.Count == 0) return valorSinRenglones;
        var subtotal = renglones.Sum(r => r.Total);
        var iva = renglones.Sum(r => r.Total * r.PorcentajeIva);
        return subtotal + iva;
    }

    private List<DocumentoVinculado> ConstruirDocumentosVinculados(IReadOnlyList<DocumentoVinculadoInputDto>? documentosVinculados) =>
        (documentosVinculados ?? []).Select(d => new DocumentoVinculado
        {
            PrefijoOrigen = d.PrefijoOrigen,
            NumeroReferencia = d.NumeroReferencia,
            Fecha = d.Fecha,
            FechaVencimiento = d.FechaVencimiento,
            FechaCreacion = timeProvider.GetUtcNow().UtcDateTime,
            Cliente = d.Cliente,
            NitCliente = d.NitCliente,
            Direccion = d.Direccion,
            Ciudad = d.Ciudad,
            Telefono = d.Telefono,
            ElaboradoPor = d.ElaboradoPor,
            FormaDePago = d.FormaDePago,
            CampoEntrega = d.CampoEntrega,
            Renglones = d.Renglones.Select((r, indice) => new RenglonDocumentoVinculado
            {
                Orden = indice + 1,
                Codigo = r.Codigo,
                Descripcion = r.Descripcion,
                Cantidad = r.Cantidad,
                UnidadMedida = r.UnidadMedida,
                ValorUnitario = r.ValorUnitario,
                PorcentajeIva = r.PorcentajeIva,
                Total = r.Cantidad * r.ValorUnitario
            }).ToList()
        }).ToList();

    /// <summary>
    /// Una copia NUEVA de una Cotización que ya existe en el sistema (otra OC ligada a la misma
    /// Cotización, o el reenvío de una OC) arranca con el avance que ya tienen sus hermanas: cantidad
    /// disponible, despachos de Remisión y las marcas de aviso — si no, arrancaría en 0 sobre una Hoja
    /// que Transporte ya completó y dispararía la alerta de "sigue incompleta" (bug real 2026-11-04,
    /// Hoja 16109). Los renglones se emparejan por Código y posición entre los que comparten Código
    /// (por Orden si no traen código), el mismo criterio de ActualizarCantidadesAsync.
    /// </summary>
    private async Task HeredarAvanceDeCopiasHermanasAsync(IReadOnlyList<DocumentoVinculado> nuevas, CancellationToken ct)
    {
        foreach (var nueva in nuevas)
        {
            var hermana = await db.DocumentosVinculados
                .Include(d => d.Renglones).ThenInclude(r => r.DespachosRemision)
                .Where(d => d.Empresa == nueva.Empresa && d.PrefijoOrigen == nueva.PrefijoOrigen && d.NumeroReferencia == nueva.NumeroReferencia)
                .OrderBy(d => d.Id)
                .FirstOrDefaultAsync(ct);
            if (hermana is null) continue;

            nueva.UltimaAlertaEnviada = hermana.UltimaAlertaEnviada;
            nueva.AvisoCompletaRemisionadaEnviado = hermana.AvisoCompletaRemisionadaEnviado;

            List<RenglonDocumentoVinculado> Candidatos(DocumentoVinculado doc, RenglonDocumentoVinculado r) =>
                r.Codigo != null
                    ? doc.Renglones.Where(x => x.Codigo == r.Codigo).OrderBy(x => x.Orden).ToList()
                    : doc.Renglones.Where(x => x.Codigo == null && x.Orden == r.Orden).ToList();

            foreach (var renglon in nueva.Renglones)
            {
                var posicion = Candidatos(nueva, renglon).IndexOf(renglon);
                var candidatos = Candidatos(hermana, renglon);
                if (posicion < 0 || posicion >= candidatos.Count) continue;

                var origen = candidatos[posicion];
                renglon.CantidadDisponible = Math.Min(origen.CantidadDisponible, renglon.Cantidad);
                foreach (var despacho in origen.DespachosRemision)
                    renglon.DespachosRemision.Add(new RemisionRenglon
                    {
                        NumeroRemision = despacho.NumeroRemision,
                        Fecha = despacho.Fecha,
                        CantidadRemisionada = despacho.CantidadRemisionada
                    });
            }
        }
    }

    private static void ValidarCamposRequeridos(IEnumerable<CampoTipoDocumento> campos, Dictionary<string, string>? datos)
    {
        var faltantes = campos
            .Where(c => c.Requerido)
            .Where(c => datos is null || !datos.TryGetValue(c.Nombre, out var v) || string.IsNullOrWhiteSpace(v))
            .Select(c => c.Etiqueta)
            .ToList();

        if (faltantes.Count > 0)
            throw new InvalidOperationException($"Faltan campos requeridos: {string.Join(", ", faltantes)}.");
    }

    // Una notificación que falla (SMTP caído, datos raros, lo que sea) nunca debe tumbar la
    // acción de negocio que la disparó — ya se aprobó/devolvió/etc. en la base, eso es lo que
    // importa. NotificacionService ya maneja sus propios errores de envío puntuales; esto cubre
    // cualquier otra falla inesperada al armar la notificación.
    private static async Task NotificarSinRomperAsync(Func<Task> notificar)
    {
        try
        {
            await notificar();
        }
        catch
        {
            // Deliberadamente silencioso: ver comentario arriba.
        }
    }

    private async Task<List<int>> ObtenerRolesIdsAsync(int usuarioId, CancellationToken ct) =>
        await db.UsuarioRoles.Where(ur => ur.UsuarioId == usuarioId).Select(ur => ur.RolId).ToListAsync(ct);

    private IQueryable<InstanciaDocumento> CargarConDetalle() =>
        db.InstanciasDocumento
            .Include(i => i.TipoDocumento)
            .Include(i => i.PasoActual).ThenInclude(p => p!.DestinosDevolucion).ThenInclude(d => d.PasoFlujoDestino)
            .Include(i => i.CreadoPorUsuario)
            .Include(i => i.Adjuntos)
            .Include(i => i.Renglones)
            .Include(i => i.Historial).ThenInclude(h => h.Usuario)
            .Include(i => i.Historial).ThenInclude(h => h.PasoFlujo)
            .Include(i => i.DocumentosVinculados).ThenInclude(d => d.Renglones)
            .Include(i => i.ComprobanteEgreso).ThenInclude(c => c!.Renglones)
            .Include(i => i.SoportePagoAdjunto)
            .Include(i => i.PagoProveedor).ThenInclude(p => p!.RelacionPago)
            .Include(i => i.PagoProveedor).ThenInclude(p => p!.LotePago)
            .Include(i => i.FacturasPagoProveedor)
            .Include(i => i.AnticiposPagoProveedor)
            .AsSplitQuery();

    private static PagoProveedorDto? MapearPagoProveedor(InstanciaDocumento i)
    {
        if (i.PagoProveedor is not { } p) return null;

        var totalFacturas = p.EsExtraordinario ? (i.Valor ?? 0) : i.FacturasPagoProveedor.Sum(f => f.Valor);
        var totalAnticipos = i.AnticiposPagoProveedor.Sum(a => a.Valor);
        return new PagoProveedorDto(
            p.Identificacion, totalFacturas, totalAnticipos, totalFacturas - totalAnticipos,
            i.AnticiposPagoProveedor.OrderBy(a => a.Fecha).Select(a => new AnticipoPagoProveedorDto(
                a.IdDocumentoDetalleWO, a.Prefijo, a.NumeroDocumento, a.Fecha, a.Nota, a.Valor)).ToList(),
            p.IdCuentaBancoWO, p.NombreBanco, p.FechaPago, p.PendienteCreacionCE, p.ConflictoCreacionCE,
            p.PrefijoCE, p.NumeroCE, p.FechaCreacionCE,
            p.EsExtraordinario, p.NombreTercero, p.NombreTipoGiro, p.Concepto, p.RelacionPago?.Numero, p.LotePago?.Numero);
    }

    private static InstanciaDocumentoResumenDto MapearResumenDto(InstanciaDocumento i) => new(
        i.Id, i.TipoDocumento.Nombre, i.NumeroReferencia, i.Proveedor, i.Valor, i.FechaDocumento,
        i.Estado, i.PasoActual?.Nombre, i.FechaCreacion, i.Empresa.Nombre());

    /// <summary>"El emisor" (si corresponde, con el id de mentira DestinoEmisorId) + los pasos anteriores puntuales — mismo orden que usa el switch de EjecutarAccionAsync.</summary>
    private static List<PasoResumenDto> DestinosDevolucionDisponibles(PasoFlujo? paso)
    {
        if (paso is null) return [];

        var opciones = new List<PasoResumenDto>();
        if (paso.PermiteDevolverAlEmisor) opciones.Add(new PasoResumenDto(DestinoEmisorId, "El emisor"));
        opciones.AddRange(paso.DestinosDevolucion.Select(d => new PasoResumenDto(d.PasoFlujoDestinoId, d.PasoFlujoDestino.Nombre)));
        return opciones;
    }

    private static InstanciaDocumentoDetalleDto MapearDetalleDto(InstanciaDocumento i) => new(
        i.Id, i.TipoDocumento.Nombre, i.NumeroReferencia, i.Proveedor, i.Valor, i.FechaDocumento,
        i.DatosJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(i.DatosJson),
        i.Estado, i.PasoActualId, i.PasoActual?.Nombre,
        i.PasoActual?.PermiteDevolver ?? false, i.PasoActual?.PermiteRechazar ?? false,
        DestinosDevolucionDisponibles(i.PasoActual),
        i.PasoActual?.RequiereComprobanteEgresoAlAprobar ?? false,
        i.PasoActual?.RequiereComentarioAlAprobar ?? false,
        i.PasoActual?.PasoRedirigirAPagoContadoId is not null,
        i.PasoActual?.RequiereSoportePagoAlAprobar ?? false,
        i.CreadoPorUsuario.Nombre, i.FechaCreacion,
        i.Adjuntos.Select(a => new AdjuntoDto(a.Id, a.NombreArchivo, a.ContentType, a.TamanoBytes, a.FechaCarga)).ToList(),
        i.Historial.OrderBy(h => h.Fecha).Select(h => new HistorialAccionDto(
            h.Id, h.PasoFlujo?.Nombre, h.Usuario.Nombre, h.Accion, h.Comentario, h.Fecha)).ToList(),
        i.Renglones.OrderBy(r => r.Orden).Select(r => new RenglonDto(
            r.Id, r.Codigo, r.Descripcion, r.Cantidad, r.UnidadMedida, r.ValorUnitario, r.PorcentajeIva, r.Total)).ToList(),
        i.IdAsientoContableOrigen, i.PrefijoOrigen, i.PendienteEscrituraWO, i.ConflictoWO, i.FechaEscrituraWO,
        i.DocumentosVinculados.Select(d => new DocumentoVinculadoDto(
            d.Id, d.PrefijoOrigen, d.NumeroReferencia, d.Fecha, d.FechaVencimiento, d.Cliente, d.NitCliente,
            d.Direccion, d.Ciudad, d.Telefono, d.ElaboradoPor, d.FormaDePago, d.CampoEntrega,
            d.FechaCreacion, d.Renglones.All(r => r.CantidadDisponible >= r.Cantidad),
            d.Renglones.OrderBy(r => r.Orden).Select(r => new RenglonDocumentoVinculadoDto(
                r.Id, r.Codigo, r.Descripcion, r.Cantidad, r.UnidadMedida, r.ValorUnitario, r.PorcentajeIva, r.Total, r.CantidadDisponible)).ToList()
        )).ToList(),
        i.SolicitanteWO,
        i.NumeroComprobanteEgresoSolicitado, i.PendienteBusquedaComprobanteEgresoWO, i.ConflictoComprobanteEgresoWO,
        i.ComprobanteEgreso is null ? null : new ComprobanteEgresoDto(
            i.ComprobanteEgreso.Id, i.ComprobanteEgreso.NumeroReferencia, i.ComprobanteEgreso.Fecha, i.ComprobanteEgreso.Valor,
            i.ComprobanteEgreso.Beneficiario, i.ComprobanteEgreso.NitBeneficiario, i.ComprobanteEgreso.Direccion,
            i.ComprobanteEgreso.Ciudad, i.ComprobanteEgreso.Telefono, i.ComprobanteEgreso.Concepto, i.ComprobanteEgreso.ElaboradoPor,
            i.ComprobanteEgreso.Renglones.OrderBy(r => r.Orden).Select(r => new RenglonComprobanteEgresoDto(
                r.Id, r.CodigoCuenta, r.NombreCuenta, r.Concepto, r.Tercero, r.Debito, r.Credito)).ToList()),
        i.SoportePagoAdjunto is null ? null : new AdjuntoDto(
            i.SoportePagoAdjunto.Id, i.SoportePagoAdjunto.NombreArchivo, i.SoportePagoAdjunto.ContentType,
            i.SoportePagoAdjunto.TamanoBytes, i.SoportePagoAdjunto.FechaCarga),
        MapearPagoProveedor(i),
        EsConfirmacionPagoProveedor(i, i.PasoActual));
}
