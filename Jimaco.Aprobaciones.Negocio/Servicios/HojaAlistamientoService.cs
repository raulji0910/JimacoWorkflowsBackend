using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class HojaAlistamientoService(AppDbContext db, IConfiguration configuration, TimeProvider timeProvider, INotificacionService notificacionService) : IHojaAlistamientoService
{
    private double HorasUmbralAlerta =>
        double.TryParse(configuration["Alistamiento:HorasUmbralAlerta"], out var horas) ? horas : 2;

    public async Task<IReadOnlyList<HojaAlistamientoResumenDto>> ListarAsync(int usuarioId, CancellationToken ct = default, Empresa empresa = Empresa.Jimaco)
    {
        await ExigirPermisoAsync(usuarioId, gestionar: false, empresa, ct);

        var ahora = timeProvider.GetUtcNow().UtcDateTime;
        var horasUmbral = HorasUmbralAlerta;

        // Filtrar PRIMERO por DocumentosVinculados (tabla chica, solo OC con Cotización ligada) y
        // recién ahí calcular la fecha de aprobación de esos pocos — al revés (como quedó en la
        // primera versión) escaneaba el historial de TODA OC creada desde 2026-09-02, no solo las
        // que tienen hoja, y con el volumen real de producción se puso notablemente lento.
        // Hojas sin OC asociada (2026-10-21) — Cotización detectada directo en World Office, nunca
        // pasó por el flujo de aprobación — entran igual, sin el filtro de HistorialAcciones (no
        // existe ninguna aprobación de la que colgarse) y con FechaCreacion como reloj en vez de la
        // fecha de aprobación comercial.
        var hojas = await db.DocumentosVinculados
            .Include(d => d.InstanciaDocumento)
            .Include(d => d.Renglones).ThenInclude(r => r.DespachosRemision)
            .Where(d => d.Empresa == empresa)
            .Where(d => d.InstanciaDocumentoId == null || db.HistorialAcciones.Any(h =>
                h.InstanciaDocumentoId == d.InstanciaDocumentoId &&
                ((h.Accion == TipoAccion.Aprobado && h.PasoFlujo!.Orden == 1) || h.Accion == TipoAccion.AprobadoAutomatico)))
            .ToListAsync(ct);

        if (hojas.Count == 0) return [];

        var idsHojas = hojas.Where(d => d.InstanciaDocumentoId != null).Select(d => d.InstanciaDocumentoId!.Value).ToHashSet();
        var fechasAprobacion = await db.HistorialAcciones
            .Where(h => idsHojas.Contains(h.InstanciaDocumentoId) && ((h.Accion == TipoAccion.Aprobado && h.PasoFlujo!.Orden == 1) || h.Accion == TipoAccion.AprobadoAutomatico))
            .GroupBy(h => h.InstanciaDocumentoId)
            .Select(g => new { InstanciaDocumentoId = g.Key, Fecha = g.Max(h => h.Fecha) })
            .ToDictionaryAsync(x => x.InstanciaDocumentoId, x => x.Fecha, ct);

        DateTime FechaAnclaje(DocumentoVinculado d) =>
            d.InstanciaDocumentoId is int idAprobacion && fechasAprobacion.TryGetValue(idAprobacion, out var fecha) ? fecha : d.FechaCreacion;

        // Una misma Cotización real de WO puede venir ligada a más de una OC — cada una trae su
        // propia copia de DocumentoVinculado (fila independiente). Para este control es UNA sola
        // hoja física: se agrupan por Prefijo+Número real y se muestran una sola vez (pedido
        // explícito del usuario, 2026-09-29), aunque el control de OC en el resto del sistema siga
        // siendo por separado. El representante (para entrar al detalle) es cualquiera del grupo —
        // ObtenerAsync ya devuelve la info fusionada sin importar cuál se use.
        return hojas
            .GroupBy(d => (d.PrefijoOrigen, d.NumeroReferencia))
            .Select(grupo =>
            {
                var representante = grupo.OrderBy(d => d.Id).First();
                var fechaAprobacion = grupo.Min(FechaAnclaje);
                var completa = representante.Renglones.All(r => r.CantidadDisponible >= r.Cantidad);
                var fechaCompletada = completa ? grupo.Max(d => d.FechaCompletada) : null;
                var enAlerta = !completa && (ahora - fechaAprobacion).TotalHours >= horasUmbral;
                var ocs = grupo
                    .Where(d => d.InstanciaDocumentoId is not null)
                    .OrderBy(d => d.InstanciaDocumento!.NumeroReferencia)
                    .Select(d => new OcVinculadaAlistamientoDto(d.InstanciaDocumentoId!.Value, d.InstanciaDocumento!.NumeroReferencia))
                    .ToList();
                var subtotal = representante.Renglones.Sum(r => r.Total);
                var total = subtotal + representante.Renglones.Sum(r => r.Total * r.PorcentajeIva);
                var remisionado = representante.Renglones.Count > 0 && representante.Renglones.All(RenglonRemisionadoCompleto);
                var remisionadoParcial = !remisionado && representante.Renglones.Any(RenglonTieneAlgoRemisionado);
                return new HojaAlistamientoResumenDto(
                    representante.Id, ocs, representante.NumeroReferencia,
                    representante.Cliente, representante.CampoEntrega, representante.ElaboradoPor,
                    fechaAprobacion, completa, enAlerta,
                    representante.Renglones.Count, representante.Renglones.Count(r => r.CantidadDisponible >= r.Cantidad),
                    total, remisionado, remisionadoParcial, fechaCompletada);
            })
            .OrderBy(dto => dto.FechaAprobacionComercial)
            .ToList();
    }

    public async Task<HojaAlistamientoDetalleDto> ObtenerAsync(int documentoVinculadoId, int usuarioId, CancellationToken ct = default)
    {
        var hoja = await db.DocumentosVinculados
            .Include(d => d.Renglones).ThenInclude(r => r.DespachosRemision)
            .FirstOrDefaultAsync(d => d.Id == documentoVinculadoId, ct)
            ?? throw new KeyNotFoundException("Hoja de Alistamiento no encontrada.");
        await ExigirPermisoAsync(usuarioId, gestionar: false, hoja.Empresa, ct);

        // Se buscan TODAS las copias que comparten Prefijo+Número (misma Cotización real de WO
        // ligada a más de una OC) para fusionarlas en una sola vista — ver la nota en ListarAsync.
        var grupo = await db.DocumentosVinculados
            .Include(d => d.InstanciaDocumento)
            .Where(d => d.Empresa == hoja.Empresa && d.PrefijoOrigen == hoja.PrefijoOrigen && d.NumeroReferencia == hoja.NumeroReferencia)
            .ToListAsync(ct);

        var fechasAprobacion = new List<DateTime>();
        var ocs = new List<OcVinculadaAlistamientoDto>();
        foreach (var copia in grupo)
        {
            if (copia.InstanciaDocumentoId is not int idInstanciaCopia)
            {
                // Copia sin OC asociada — no hay aprobación comercial que esperar, cuenta desde ya.
                fechasAprobacion.Add(copia.FechaCreacion);
                continue;
            }

            var fecha = await ObtenerFechaAprobacionComercialAsync(idInstanciaCopia, ct);
            if (fecha is null) continue; // esta OC en particular todavía no pasó su aprobación comercial — no cuenta para el grupo visible
            fechasAprobacion.Add(fecha.Value);
            ocs.Add(new OcVinculadaAlistamientoDto(idInstanciaCopia, copia.InstanciaDocumento!.NumeroReferencia));
        }

        if (fechasAprobacion.Count == 0)
            throw new InvalidOperationException("Esta Hoja de Alistamiento todavía no está disponible — falta la aprobación comercial de la OC.");

        var subtotal = hoja.Renglones.Sum(r => r.Total);
        var totalIva = hoja.Renglones.Sum(r => r.Total * r.PorcentajeIva);
        var remisionado = hoja.Renglones.Count > 0 && hoja.Renglones.All(RenglonRemisionadoCompleto);
        var remisionadoParcial = !remisionado && hoja.Renglones.Any(RenglonTieneAlgoRemisionado);

        return new HojaAlistamientoDetalleDto(
            hoja.Id, ocs, hoja.NumeroReferencia,
            hoja.Cliente, hoja.Direccion, hoja.Ciudad, hoja.Telefono, hoja.CampoEntrega,
            fechasAprobacion.Min(), hoja.Renglones.All(r => r.CantidadDisponible >= r.Cantidad),
            subtotal, totalIva, subtotal + totalIva,
            remisionado, remisionadoParcial,
            hoja.Renglones.OrderBy(r => r.Orden).Select(r => new RenglonHojaAlistamientoDto(
                r.Id, r.Codigo, r.Descripcion, r.Cantidad, r.UnidadMedida, r.CantidadDisponible,
                r.DespachosRemision.Sum(x => x.CantidadRemisionada),
                r.DespachosRemision.OrderBy(x => x.Fecha)
                    .Select(x => new DespachoRemisionDto(x.NumeroRemision, x.Fecha, x.CantidadRemisionada)).ToList()
            )).ToList());
    }

    public async Task ActualizarCantidadesAsync(int documentoVinculadoId, int usuarioId, ActualizarCantidadesDisponiblesDto dto, CancellationToken ct = default)
    {
        var hoja = await db.DocumentosVinculados
            .Include(d => d.Renglones)
            .FirstOrDefaultAsync(d => d.Id == documentoVinculadoId, ct)
            ?? throw new KeyNotFoundException("Hoja de Alistamiento no encontrada.");
        await ExigirPermisoAsync(usuarioId, gestionar: true, hoja.Empresa, ct);

        // Sin OC asociada no hay aprobación comercial que exigir — está disponible desde que se creó.
        if (hoja.InstanciaDocumentoId is int idInstanciaHoja && await ObtenerFechaAprobacionComercialAsync(idInstanciaHoja, ct) is null)
            throw new InvalidOperationException("Esta Hoja de Alistamiento todavía no está disponible — falta la aprobación comercial de la OC.");

        // La misma Cotización puede estar ligada a más de una OC (una copia de DocumentoVinculado
        // por cada una) — para este control se manejan como una sola hoja, así que la cantidad
        // registrada se replica en TODAS las copias que comparten Prefijo+Número (pedido explícito
        // del usuario, 2026-09-29). Las copias no comparten Id de renglón, así que se emparejan por
        // Código (o por Orden si el ítem no trae código).
        var grupo = await db.DocumentosVinculados
            .Include(d => d.Renglones).ThenInclude(r => r.DespachosRemision)
            .Where(d => d.Empresa == hoja.Empresa && d.PrefijoOrigen == hoja.PrefijoOrigen && d.NumeroReferencia == hoja.NumeroReferencia)
            .ToListAsync(ct);

        var nombreUsuario = await db.Usuarios.Where(u => u.Id == usuarioId).Select(u => u.Nombre).FirstOrDefaultAsync(ct);
        var ahora = timeProvider.GetUtcNow().UtcDateTime;
        var completasAntes = grupo.Where(EstaCompleta).Select(c => c.Id).ToHashSet();

        var renglonesPorId = hoja.Renglones.ToDictionary(r => r.Id);
        foreach (var cambio in dto.Renglones)
        {
            if (!renglonesPorId.TryGetValue(cambio.RenglonId, out var renglon))
                throw new InvalidOperationException($"El renglón {cambio.RenglonId} no pertenece a esta Hoja de Alistamiento.");
            if (cambio.CantidadDisponible < 0)
                throw new InvalidOperationException("La cantidad disponible no puede ser negativa.");
            if (cambio.CantidadDisponible > renglon.Cantidad)
                throw new InvalidOperationException($"La cantidad disponible de \"{renglon.Descripcion}\" no puede superar la pedida ({renglon.Cantidad}).");

            // Bug real encontrado con datos de producción (2026-10-09, Hoja 16172): cuando un mismo
            // código aparece MÁS DE UNA VEZ en la propia hoja (dos renglones reales distintos con el
            // mismo ítem), el FirstOrDefault de abajo siempre resolvía a la PRIMERA ocurrencia — ni
            // siquiera distinguía la copia propia de las hermanas, así que guardar el segundo renglón
            // terminaba pisando el primero una y otra vez, y el segundo quedaba eternamente en 0. Fix:
            // se ubica la POSICIÓN del renglón editado entre los que comparten su mismo código dentro
            // de su propia hoja, y en cada copia se busca esa misma posición entre sus renglones del
            // mismo código — así cada duplicado se empareja con su correspondiente, no con el primero.
            List<RenglonDocumentoVinculado> ObtenerCandidatos(DocumentoVinculado doc) =>
                renglon.Codigo != null
                    ? doc.Renglones.Where(r => r.Codigo == renglon.Codigo).OrderBy(r => r.Orden).ToList()
                    : doc.Renglones.Where(r => r.Orden == renglon.Orden).ToList();

            var posicion = ObtenerCandidatos(hoja).FindIndex(r => r.Id == renglon.Id);

            // Bitácora (2026-11-09): una sola entrada por cambio, aunque se replique a varias copias.
            if (renglon.CantidadDisponible != cambio.CantidadDisponible)
                db.HistorialDisponible.Add(new HistorialDisponible
                {
                    Empresa = hoja.Empresa,
                    PrefijoOrigen = hoja.PrefijoOrigen,
                    NumeroReferencia = hoja.NumeroReferencia,
                    Codigo = renglon.Codigo,
                    Descripcion = renglon.Descripcion,
                    CantidadAnterior = renglon.CantidadDisponible,
                    CantidadNueva = cambio.CantidadDisponible,
                    Origen = HistorialDisponible.OrigenUsuario,
                    UsuarioId = usuarioId,
                    NombreUsuario = nombreUsuario,
                    Fecha = ahora
                });

            foreach (var copia in grupo)
            {
                var candidatos = ObtenerCandidatos(copia);
                var renglonCopia = posicion >= 0 && posicion < candidatos.Count ? candidatos[posicion] : candidatos.FirstOrDefault();
                if (renglonCopia is not null)
                    renglonCopia.CantidadDisponible = cambio.CantidadDisponible;
            }
        }

        MarcarFechaCompletada(grupo, completasAntes, ahora);
        await db.SaveChangesAsync(ct);
        await AvisarSiQuedoCompletaYRemisionadaAsync(grupo, ct);
    }

    // ---- Remisionado (2026-10-09) — mismo patrón "pedir y recoger" que el Comprobante de Egreso:
    // el Sincronizador pide esta lista y, por cada una, revisa en WO si ya existe una Remisión que
    // la cruce (campo libre "COTIZACION", texto a mano — ver nota en DocumentoVinculado). ----

    public async Task<IReadOnlyList<PendienteRemisionDto>> ListarPendientesRemisionAsync(CancellationToken ct = default, Empresa empresa = Empresa.Jimaco) =>
        await db.DocumentosVinculados
            .Where(d => d.Empresa == empresa)
            .Where(d => d.Renglones.Any(r => r.DespachosRemision.Sum(x => x.CantidadRemisionada) < r.Cantidad) && (d.InstanciaDocumentoId == null || db.HistorialAcciones.Any(h =>
                h.InstanciaDocumentoId == d.InstanciaDocumentoId &&
                ((h.Accion == TipoAccion.Aprobado && h.PasoFlujo!.Orden == 1) || h.Accion == TipoAccion.AprobadoAutomatico))))
            .Select(d => new { d.PrefijoOrigen, d.NumeroReferencia })
            .Distinct()
            .Select(d => new PendienteRemisionDto(d.PrefijoOrigen, d.NumeroReferencia))
            .ToListAsync(ct);

    // ---- Refrescar (2026-10-14) — lista independiente de la de arriba: el corte es "no Completa",
    // no "no Remisionada". Antes el Sincronizador reusaba ListarPendientesRemisionAsync para esto,
    // lo que dejaba de refrescar una Hoja apenas se marcaba Remisionada aunque Transporte siguiera
    // sin terminarla (caso real: Hoja 16159). ----

    public async Task<IReadOnlyList<PendienteRefrescoDto>> ListarPendientesRefrescoAsync(CancellationToken ct = default, Empresa empresa = Empresa.Jimaco) =>
        await db.DocumentosVinculados
            .Where(d => d.Empresa == empresa)
            .Where(d => d.Renglones.Any(r => r.CantidadDisponible < r.Cantidad) && (d.InstanciaDocumentoId == null || db.HistorialAcciones.Any(h =>
                h.InstanciaDocumentoId == d.InstanciaDocumentoId &&
                ((h.Accion == TipoAccion.Aprobado && h.PasoFlujo!.Orden == 1) || h.Accion == TipoAccion.AprobadoAutomatico))))
            .Select(d => new { d.PrefijoOrigen, d.NumeroReferencia })
            .Distinct()
            .Select(d => new PendienteRefrescoDto(d.PrefijoOrigen, d.NumeroReferencia))
            .ToListAsync(ct);

    // Hojas sin OC asociada (2026-10-21) — pedido real: Cotizaciones que Transporte necesita alistar
    // aunque nunca haya pasado (ni vaya a pasar) una Orden de Compra por el flujo de aprobación. El
    // Sincronizador escanea Cotizaciones recientes directo en World Office (ver Program.cs, marca de
    // agua propia) y llama acá por cada una que todavía no exista en nuestro sistema — no importa si
    // más adelante SÍ llega a tener una OC real: ese caso normal sigue entrando por
    // InstanciaDocumentoService.ConstruirDocumentosVinculados, como siempre.
    public async Task CrearSinOcAsync(DocumentoVinculadoInputDto dto, CancellationToken ct = default, Empresa empresa = Empresa.Jimaco)
    {
        var yaExiste = await db.DocumentosVinculados
            .AnyAsync(d => d.Empresa == empresa && d.PrefijoOrigen == dto.PrefijoOrigen && d.NumeroReferencia == dto.NumeroReferencia, ct);
        if (yaExiste) return;

        var ahora = timeProvider.GetUtcNow().UtcDateTime;
        db.DocumentosVinculados.Add(new DocumentoVinculado
        {
            Empresa = empresa,
            PrefijoOrigen = dto.PrefijoOrigen,
            NumeroReferencia = dto.NumeroReferencia,
            Fecha = dto.Fecha,
            FechaVencimiento = dto.FechaVencimiento,
            Cliente = dto.Cliente,
            NitCliente = dto.NitCliente,
            Direccion = dto.Direccion,
            Ciudad = dto.Ciudad,
            Telefono = dto.Telefono,
            ElaboradoPor = dto.ElaboradoPor,
            FormaDePago = dto.FormaDePago,
            CampoEntrega = dto.CampoEntrega,
            FechaCreacion = ahora,
            Renglones = dto.Renglones.Select((r, indice) => new RenglonDocumentoVinculado
            {
                Orden = indice + 1,
                Codigo = r.Codigo,
                Descripcion = r.Descripcion,
                Cantidad = r.Cantidad,
                UnidadMedida = r.UnidadMedida,
                ValorUnitario = r.ValorUnitario,
                PorcentajeIva = r.PorcentajeIva,
                Total = r.Cantidad * r.ValorUnitario,
                CantidadDisponible = 0m
            }).ToList()
        });

        await db.SaveChangesAsync(ct);
    }

    // Remisionado por renglón (2026-10-26) — pedido real: un mismo ítem puede llegar repartido en
    // VARIAS Remisiones parciales distintas (ej. Hoja 16099, 16228), así que ya no alcanza con un
    // flag a nivel de documento. El Sincronizador manda los renglones REALES de la Remisión (código +
    // cantidad, leídos de WO igual que los de Cotización/OC) y acá se cruzan por Código contra los
    // renglones de la Hoja — mismo criterio de posición (n-ésima ocurrencia de un código) que
    // ActualizarCantidadesAsync, para el caso de ítems repetidos dentro de la misma Hoja.
    public async Task MarcarRemisionadaAsync(MarcarRemisionadaDto dto, CancellationToken ct = default, Empresa empresa = Empresa.Jimaco)
    {
        // Mismo criterio de grupo que ActualizarCantidadesAsync: todas las copias que comparten
        // Prefijo+Número reciben el despacho a la vez, sin importar por cuál OC se detectó.
        var copias = await db.DocumentosVinculados
            .Include(d => d.Renglones).ThenInclude(r => r.DespachosRemision)
            .Where(d => d.Empresa == empresa && d.PrefijoOrigen == dto.PrefijoOrigen && d.NumeroReferencia == dto.NumeroReferencia)
            .ToListAsync(ct);

        if (copias.Count == 0) return;

        foreach (var copia in copias)
            AplicarDespachoRemision(copia, dto);

        await db.SaveChangesAsync(ct);
        await AvisarSiQuedoCompletaYRemisionadaAsync(copias, ct);
    }

    // Bug real de producción (2026-10-26, Hoja 15509): emparejar renglón a renglón POR POSICIÓN
    // entre dos documentos distintos (Cotización y Remisión) no es seguro cuando un Código se repite
    // — World Office no garantiza que el orden de las líneas se preserve entre los dos documentos.
    // Caso real: ítem "PJ-3759" repetido en la Hoja con cantidades pedidas 10 y 5; la Remisión real
    // lo traía en el orden contrario (5 y 10) y el emparejamiento por posición los cruzó al revés —
    // el renglón de 10 quedó con 5 remisionado, el de 5 quedó con 10 (más de lo pedido). Corregido:
    // se suma TODO lo que esta Remisión trae de cada Código, y se reparte en orden (Orden ascendente
    // de la Hoja) entre los renglones que comparten ese Código, llenando primero el que menos tenga
    // despachado por OTRAS Remisiones — da el mismo resultado de antes cuando el Código no se repite
    // (reparte todo a la única ocurrencia), y es inmune al orden real de las líneas cuando sí se repite.
    private static void AplicarDespachoRemision(DocumentoVinculado copia, MarcarRemisionadaDto dto)
    {
        var cantidadPorCodigo = new Dictionary<string, decimal>();
        foreach (var renglonRemision in dto.Renglones)
        {
            if (renglonRemision.Codigo is null) continue; // sin código no hay con qué cruzar de forma confiable
            cantidadPorCodigo[renglonRemision.Codigo] = cantidadPorCodigo.GetValueOrDefault(renglonRemision.Codigo) + renglonRemision.Cantidad;
        }

        // Bug real de producción (2026-11-06, Hoja 16372): la Remisión 29413 se editó en WO después de
        // aplicada y le quitaron el ítem PJ-4691 — pero acá solo se recorrían los códigos que la
        // Remisión TRAE, así que el despacho viejo de ese ítem quedaba para siempre ("29413 (7/7)"
        // en un ítem que en WO sigue pendiente). Ahora se recorren TODOS los códigos de la Hoja: los
        // que la Remisión ya no trae reciben 0 y su despacho de esta Remisión se borra. Mismo motivo
        // para no cortar el reparto apenas se agota la cantidad: un renglón repetido que antes
        // recibía algo y ahora no, también tiene que quedar en 0.
        var codigosHoja = copia.Renglones.Where(r => r.Codigo is not null).Select(r => r.Codigo!).Distinct().ToList();

        foreach (var codigo in codigosHoja)
        {
            var renglones = copia.Renglones.Where(r => r.Codigo == codigo).OrderBy(r => r.Orden).ToList();
            var cantidadTotal = cantidadPorCodigo.GetValueOrDefault(codigo);

            var restante = cantidadTotal;
            foreach (var renglon in renglones)
            {
                // Idempotente por (renglón, Número de Remisión): si el Sincronizador vuelve a mandar
                // la misma Remisión (reintento, o la cantidad real cambió en WO), se actualiza en vez
                // de duplicar el despacho — y se excluye del cálculo de "ya despachado" para no
                // restarse a sí mismo.
                var despachoExistente = renglon.DespachosRemision.FirstOrDefault(x => x.NumeroRemision == dto.NumeroRemision);
                var yaDespachadoPorOtras = renglon.DespachosRemision
                    .Where(x => x.NumeroRemision != dto.NumeroRemision)
                    .Sum(x => x.CantidadRemisionada);
                var capacidad = Math.Max(0, renglon.Cantidad - yaDespachadoPorOtras);
                var aplicar = Math.Min(restante, capacidad);

                if (despachoExistente is not null && aplicar <= 0)
                    renglon.DespachosRemision.Remove(despachoExistente);
                else if (despachoExistente is not null)
                    despachoExistente.CantidadRemisionada = aplicar;
                else if (aplicar > 0)
                    renglon.DespachosRemision.Add(new RemisionRenglon
                    {
                        NumeroRemision = dto.NumeroRemision,
                        Fecha = dto.Fecha,
                        CantidadRemisionada = aplicar
                    });

                restante -= aplicar;
            }
        }
    }

    private static bool RenglonRemisionadoCompleto(RenglonDocumentoVinculado r) =>
        r.DespachosRemision.Sum(x => x.CantidadRemisionada) >= r.Cantidad;

    private static bool RenglonTieneAlgoRemisionado(RenglonDocumentoVinculado r) =>
        r.DespachosRemision.Sum(x => x.CantidadRemisionada) > 0;

    // Pedido real (2026-10-13/14): una Cotización sigue siendo editable en World Office DESPUÉS de
    // que ya se sincronizó como Hoja de Alistamiento — hasta ahora esa copia quedaba congelada para
    // siempre, sin ninguna forma de enterarse de cambios posteriores (casos reales encontrados: Hoja
    // 16160 con un ítem reemplazado, Hoja 16141 con el Campo Entrega desactualizado). El
    // Sincronizador vuelve a leer cada Cotización activa (no remisionada) en cada corrida y manda acá
    // el estado actual — esto reemplaza cabecera y renglones en TODAS las copias del grupo,
    // CONSERVANDO la cantidad disponible que Transporte ya haya marcado para los ítems que sigan
    // siendo los mismos (mismo criterio de desambiguación por código+posición que el fix de ítems
    // duplicados de ActualizarCantidadesAsync — repetido acá a propósito, no compartido, para no
    // tocar ese método ya probado).
    public async Task RefrescarDesdeWOAsync(DocumentoVinculadoInputDto dto, CancellationToken ct = default, Empresa empresa = Empresa.Jimaco)
    {
        var grupo = await db.DocumentosVinculados
            .Include(d => d.Renglones).ThenInclude(r => r.DespachosRemision)
            .Where(d => d.Empresa == empresa && d.PrefijoOrigen == dto.PrefijoOrigen && d.NumeroReferencia == dto.NumeroReferencia)
            .ToListAsync(ct);

        if (grupo.Count == 0) return; // ya no queda ninguna copia activa de esta hoja — nada que refrescar

        // Progreso "en depósito" de ítems que en lecturas anteriores desaparecieron de la Cotización
        // (ver HistorialDisponible.OrigenRetiradoEnWO) — se devuelve si el Código reaparece.
        var depositos = await db.HistorialDisponible
            .Where(h => h.Empresa == empresa && h.PrefijoOrigen == dto.PrefijoOrigen && h.NumeroReferencia == dto.NumeroReferencia
                        && h.Origen == HistorialDisponible.OrigenRetiradoEnWO && h.CantidadPorRestaurar > 0)
            .OrderBy(h => h.Id)
            .ToListAsync(ct);
        var depositoPorCodigo = depositos
            .Where(h => h.Codigo != null)
            .GroupBy(h => h.Codigo!)
            .ToDictionary(g => g.Key, g => g.ToList());
        var codigosRestaurados = new HashSet<string>();

        var ahora = timeProvider.GetUtcNow().UtcDateTime;
        var completasAntes = grupo.Where(EstaCompleta).Select(c => c.Id).ToHashSet();
        var primera = true;
        foreach (var copia in grupo.OrderBy(c => c.Id))
        {
            copia.Fecha = dto.Fecha;
            copia.FechaVencimiento = dto.FechaVencimiento;
            copia.Cliente = dto.Cliente;
            copia.NitCliente = dto.NitCliente;
            copia.Direccion = dto.Direccion;
            copia.Ciudad = dto.Ciudad;
            copia.Telefono = dto.Telefono;
            copia.ElaboradoPor = dto.ElaboradoPor;
            copia.FormaDePago = dto.FormaDePago;
            copia.CampoEntrega = dto.CampoEntrega;

            // La bitácora y el depósito se escriben una sola vez por grupo (las copias son réplicas).
            ReemplazarRenglonesConservandoProgreso(copia, dto.Renglones, depositoPorCodigo, codigosRestaurados, primera ? ahora : null);
            primera = false;
        }

        // Los depósitos devueltos se consumen después de aplicar TODAS las copias (cada una necesita
        // el mismo valor para quedar igual a sus hermanas).
        foreach (var codigo in codigosRestaurados)
            foreach (var deposito in depositoPorCodigo[codigo])
                deposito.CantidadPorRestaurar = 0;

        MarcarFechaCompletada(grupo, completasAntes, ahora);
        await db.SaveChangesAsync(ct);
        await AvisarSiQuedoCompletaYRemisionadaAsync(grupo, ct);
    }

    private static bool EstaCompleta(DocumentoVinculado d) =>
        d.Renglones.Count > 0 && d.Renglones.All(r => r.CantidadDisponible >= r.Cantidad);

    /// <summary>
    /// FechaCompletada solo se fija cuando la copia PASA de incompleta a completa en este guardado; si
    /// deja de estar completa se borra. Una hoja que ya estaba completa conserva lo que tenía (aunque
    /// sea null) — así una completa vieja no aparece como "recién completada" por un refresco de WO.
    /// </summary>
    private static void MarcarFechaCompletada(IEnumerable<DocumentoVinculado> copias, HashSet<int> completasAntes, DateTime ahora)
    {
        foreach (var copia in copias)
        {
            if (!EstaCompleta(copia))
                copia.FechaCompletada = null;
            else if (!completasAntes.Contains(copia.Id))
                copia.FechaCompletada = ahora;
        }
    }

    // Bug real reportado por Logística (2026-10-27, Hoja 16099/PJ-1522 repetido): emparejar renglón
    // a renglón POR POSICIÓN entre la versión vieja y la nueva no es seguro cuando un Código se
    // repite — World Office no garantiza que el orden de esas líneas se preserve entre dos lecturas
    // de la misma Cotización (ej. al editarla). Antes, si el orden cambiaba, una línea con progreso
    // guardado "dejaba de encontrar match", se borraba, y se recreaba en blanco con Disponible=0 —
    // exactamente lo que Transporte reportó como "se borran las cantidades". Mismo tipo de bug (y
    // misma solución) que AplicarDespachoRemision: en vez de emparejar por posición, se suma el
    // progreso de TODAS las líneas viejas de un Código y se reparte de nuevo entre las líneas
    // actuales de ese Código (llenando primero la primera) — así no importa el orden real. Los
    // renglones SIN código (raros) siguen emparejándose por posición, no hay otra señal posible ahí.
    //
    // IMPORTANTE (2026-10-14, bug real: Hoja 16197): un renglón que se reutiliza se ACTUALIZA en el
    // lugar (mismo Id), nunca se borra+recrea — si Transporte tiene la página abierta con los Ids
    // viejos cargados, su siguiente "Guardar" no debe fallar porque esa fila ya no existe.
    //
    // Bitácora + depósito (2026-11-09, Hoja 16100): `registrarEn` != null solo para la primera copia
    // del grupo. Un ítem que desaparece de la Cotización deja su progreso en depósito; si reaparece
    // con el mismo Código (lo quitaron y lo volvieron a poner en WO), se le devuelve.
    private void ReemplazarRenglonesConservandoProgreso(
        DocumentoVinculado copia,
        IReadOnlyList<RenglonDocumentoVinculadoInputDto> nuevos,
        IReadOnlyDictionary<string, List<HistorialDisponible>> depositoPorCodigo,
        HashSet<string> codigosRestaurados,
        DateTime? registrarEn)
    {
        void Registrar(string? codigo, string? descripcion, decimal anterior, decimal nueva, string origen, decimal? porRestaurar = null)
        {
            if (registrarEn is not { } fecha) return;
            db.HistorialDisponible.Add(new HistorialDisponible
            {
                Empresa = copia.Empresa,
                PrefijoOrigen = copia.PrefijoOrigen,
                NumeroReferencia = copia.NumeroReferencia,
                Codigo = codigo,
                Descripcion = descripcion,
                CantidadAnterior = anterior,
                CantidadNueva = nueva,
                Origen = origen,
                CantidadPorRestaurar = porRestaurar,
                Fecha = fecha
            });
        }

        var viejosPorCodigo = new Dictionary<string, List<RenglonDocumentoVinculado>>();
        var viejosSinCodigo = new List<RenglonDocumentoVinculado>();
        foreach (var viejo in copia.Renglones.OrderBy(r => r.Orden))
        {
            if (viejo.Codigo is null) { viejosSinCodigo.Add(viejo); continue; }
            if (!viejosPorCodigo.TryGetValue(viejo.Codigo, out var lista))
                viejosPorCodigo[viejo.Codigo] = lista = [];
            lista.Add(viejo);
        }

        var nuevosPorCodigo = new Dictionary<string, List<RenglonDocumentoVinculadoInputDto>>();
        var nuevosSinCodigo = new List<RenglonDocumentoVinculadoInputDto>();
        foreach (var nuevo in nuevos)
        {
            if (nuevo.Codigo is null) { nuevosSinCodigo.Add(nuevo); continue; }
            if (!nuevosPorCodigo.TryGetValue(nuevo.Codigo, out var lista))
                nuevosPorCodigo[nuevo.Codigo] = lista = [];
            lista.Add(nuevo);
        }

        var reutilizados = new HashSet<int>();
        var orden = 1;

        void ActualizarCampos(RenglonDocumentoVinculado renglon, RenglonDocumentoVinculadoInputDto nuevo)
        {
            renglon.Orden = orden++;
            renglon.Codigo = nuevo.Codigo;
            renglon.Descripcion = nuevo.Descripcion;
            renglon.Cantidad = nuevo.Cantidad;
            renglon.UnidadMedida = nuevo.UnidadMedida;
            renglon.ValorUnitario = nuevo.ValorUnitario;
            renglon.PorcentajeIva = nuevo.PorcentajeIva;
            renglon.Total = nuevo.Cantidad * nuevo.ValorUnitario;
        }

        foreach (var (codigo, listaNueva) in nuevosPorCodigo)
        {
            viejosPorCodigo.TryGetValue(codigo, out var listaVieja);
            listaVieja ??= [];
            var progresoDisponible = listaVieja.Sum(v => v.CantidadDisponible);
            var desdeDeposito = false;
            if (listaVieja.Count == 0 && depositoPorCodigo.TryGetValue(codigo, out var deposito))
            {
                progresoDisponible = deposito.Sum(d => d.CantidadPorRestaurar ?? 0);
                desdeDeposito = progresoDisponible > 0;
                if (desdeDeposito) codigosRestaurados.Add(codigo);
            }
            var anteriorPorRenglon = listaVieja.Select(v => v.CantidadDisponible).ToList();

            for (var i = 0; i < listaNueva.Count; i++)
            {
                var nuevo = listaNueva[i];
                RenglonDocumentoVinculado renglon;
                if (i < listaVieja.Count)
                {
                    renglon = listaVieja[i];
                    reutilizados.Add(renglon.Id);
                }
                else
                {
                    renglon = new RenglonDocumentoVinculado { Descripcion = nuevo.Descripcion };
                    copia.Renglones.Add(renglon);
                }

                ActualizarCampos(renglon, nuevo);
                var aplicar = Math.Min(progresoDisponible, nuevo.Cantidad);
                var anterior = i < anteriorPorRenglon.Count ? anteriorPorRenglon[i] : 0m;
                renglon.CantidadDisponible = aplicar;
                progresoDisponible -= aplicar;

                if (desdeDeposito && aplicar > 0)
                    Registrar(nuevo.Codigo, nuevo.Descripcion, 0, aplicar, HistorialDisponible.OrigenRestauradoDesdeWO);
                else if (anterior != aplicar)
                    Registrar(nuevo.Codigo, nuevo.Descripcion, anterior, aplicar, HistorialDisponible.OrigenRefrescoWO);
            }
        }

        // Códigos que ya no vienen en la lectura de WO: su progreso queda en depósito.
        foreach (var (codigo, listaVieja) in viejosPorCodigo)
        {
            if (nuevosPorCodigo.ContainsKey(codigo)) continue;
            var progreso = listaVieja.Sum(v => v.CantidadDisponible);
            if (progreso > 0)
                Registrar(codigo, listaVieja[0].Descripcion, progreso, 0, HistorialDisponible.OrigenRetiradoEnWO, progreso);
        }

        // Renglones sin código: sigue el emparejamiento por posición de siempre (no hay otra señal).
        for (var i = 0; i < nuevosSinCodigo.Count; i++)
        {
            var nuevo = nuevosSinCodigo[i];
            RenglonDocumentoVinculado renglon;
            if (i < viejosSinCodigo.Count)
            {
                renglon = viejosSinCodigo[i];
                reutilizados.Add(renglon.Id);
            }
            else
            {
                renglon = new RenglonDocumentoVinculado { Descripcion = nuevo.Descripcion, CantidadDisponible = 0m };
                copia.Renglones.Add(renglon);
            }

            ActualizarCampos(renglon, nuevo);
            renglon.CantidadDisponible = Math.Min(renglon.CantidadDisponible, nuevo.Cantidad);
        }

        // Los que ya no aparecen en la lectura actual de WO sí se borran de verdad (ítem quitado de
        // la Cotización) — su progreso, si tenía, ya quedó absorbido en progresoDisponible y
        // repartido arriba entre las líneas que sí siguen existiendo.
        foreach (var viejo in copia.Renglones.Where(r => r.Id != 0 && !reutilizados.Contains(r.Id)).ToList())
        {
            db.RenglonesDocumentoVinculado.Remove(viejo);
            copia.Renglones.Remove(viejo);
        }
    }

    // Pedido explícito del usuario (2026-10-09): un solo aviso (nunca repetido, ver
    // AvisoCompletaRemisionadaEnviado) a quienes ven el dashboard de Alistamiento Y al emisor real
    // de cada OC que comparte la hoja, apenas se cumplen LAS DOS condiciones a la vez — sin importar
    // cuál de las dos se cumplió de último (Transporte completando el último ítem, o el
    // Sincronizador detectando la Remisión).
    private async Task AvisarSiQuedoCompletaYRemisionadaAsync(List<DocumentoVinculado> grupo, CancellationToken ct)
    {
        var representante = grupo.OrderBy(d => d.Id).First();
        if (representante.AvisoCompletaRemisionadaEnviado) return;
        if (representante.Renglones.Count == 0) return;
        if (!representante.Renglones.All(r => r.CantidadDisponible >= r.Cantidad)) return;
        if (!representante.Renglones.All(RenglonRemisionadoCompleto)) return;

        var instanciaIdsDelGrupo = grupo.Where(d => d.InstanciaDocumentoId != null).Select(d => d.InstanciaDocumentoId!.Value).Distinct().ToList();
        await notificacionService.NotificarHojaCompletaYRemisionadaAsync(representante.Id, instanciaIdsDelGrupo, ct);

        foreach (var copia in grupo) copia.AvisoCompletaRemisionadaEnviado = true;
        await db.SaveChangesAsync(ct);
    }

    // Pedido explícito del usuario (2026-09-28): Transporte no debe ver ni poder tocar una Hoja de
    // Alistamiento hasta que Gerencia Comercial haya aprobado el primer paso de la OC — antes de
    // eso, la compra todavía puede no concretarse. Además (mismo pedido, ajustado después de ver la
    // primera versión en QA), el reloj de la alerta de atraso arranca en ESE momento, no cuando la
    // hoja se generó — si la aprobación tarda días, Transporte no debe heredar ese atraso ajeno.
    // Se usa el ÚLTIMO Aprobado del paso 1 (no el primero) — si la OC volvió a pasar por ahí más de
    // una vez, cuenta la aprobación más reciente como el arranque del ciclo actual.
    private async Task<DateTime?> ObtenerFechaAprobacionComercialAsync(int instanciaDocumentoId, CancellationToken ct) =>
        await db.HistorialAcciones
            .Where(h => h.InstanciaDocumentoId == instanciaDocumentoId && ((h.Accion == TipoAccion.Aprobado && h.PasoFlujo!.Orden == 1) || h.Accion == TipoAccion.AprobadoAutomatico))
            .Select(h => (DateTime?)h.Fecha)
            .MaxAsync(ct);

    private async Task ExigirPermisoAsync(int usuarioId, bool gestionar, Empresa empresa, CancellationToken ct)
    {
        // Cada empresa tiene sus propios permisos de alistamiento (2026-11-13) — el dashboard de
        // Ferrealiados es aparte y solo lo ve quien tenga los permisos "…Ferrealiados".
        var roles = db.UsuarioRoles.Where(ur => ur.UsuarioId == usuarioId && ur.Rol.Activo);
        var tienePermiso = empresa == Empresa.Ferrealiados
            ? await roles.AnyAsync(ur => gestionar
                ? ur.Rol.PuedeGestionarAlistamientoFerrealiados
                : ur.Rol.PuedeGestionarAlistamientoFerrealiados || ur.Rol.PuedeVerAlistamientoFerrealiados, ct)
            : await roles.AnyAsync(ur => gestionar
                ? ur.Rol.PuedeGestionarAlistamiento
                : ur.Rol.PuedeGestionarAlistamiento || ur.Rol.PuedeVerAlistamiento, ct);

        if (!tienePermiso)
            throw new UnauthorizedAccessException(empresa == Empresa.Ferrealiados
                ? "No tenés permiso para el control de alistamiento de Ferrealiados."
                : "No tenés permiso para el control de alistamiento.");
    }
}
