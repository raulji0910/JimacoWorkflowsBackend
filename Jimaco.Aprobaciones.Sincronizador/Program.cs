using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Sincronizador;
using Jimaco.Aprobaciones.Sincronizador.Models;
using Microsoft.Extensions.Configuration;

// Pensado para correr como una Tarea Programada de Windows en la red local de World Office
// (cada N minutos) — una corrida = un pase completo de "traer lo nuevo" y salir. No es un
// servicio de larga duración; ver CLAUDE.md de este proyecto para el porqué.

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Local.json", optional: true) // credenciales reales, nunca commiteado
    .AddEnvironmentVariables()
    .Build();

var connectionStringWorldOffice = config["WorldOffice:ConnectionString"]
    ?? throw new InvalidOperationException("Falta configurar WorldOffice:ConnectionString.");
var jimacoApiUrl = config["Jimaco:ApiBaseUrl"]
    ?? throw new InvalidOperationException("Falta configurar Jimaco:ApiBaseUrl.");
var jimacoEmail = config["Jimaco:UsuarioServicio:Email"]
    ?? throw new InvalidOperationException("Falta configurar Jimaco:UsuarioServicio:Email.");
var jimacoPassword = config["Jimaco:UsuarioServicio:Password"]
    ?? throw new InvalidOperationException("Falta configurar Jimaco:UsuarioServicio:Password.");

// Por defecto true (compatibilidad con el Sincronizador real, que siempre la corrió) — se apaga
// explícitamente en una copia secundaria apuntada a otro ambiente (ej. QA, ver CLAUDE.md /
// "Sincronizador secundario para QA"), para que NO compita con el Sincronizador real por el MISMO
// correo de "Solicitar Aprobación": las dos copias leen la MISMA bandeja real (no hay una bandeja
// de pruebas aparte), y la primera que lo lea lo marca como procesado — si fuera la copia de QA,
// esa OC real nunca le llegaría al Sincronizador de producción. El resto de fases (escribir de
// vuelta en WO, buscar comprobantes de egreso) son de lectura/lectura-y-empuje sin este problema:
// no "consumen" nada compartido, así que no necesitan un interruptor propio.
var procesarCorreoNuevo = !bool.TryParse(config["Sincronizador:ProcesarCorreoNuevo"], out var pcn) || pcn;

// ---- Empresa y mapeo de campos (2026-11-13, FERREALIADOS) ----
// El mismo exe sirve a las dos empresas del grupo: cada copia apunta a la base de WO de su empresa y
// se identifica ante la Api con el encabezado X-Empresa (la Api solo le muestra/le deja tocar lo de
// esa empresa). Sin "Empresa:Nombre" = Jimaco, como siempre.
var empresa = string.IsNullOrWhiteSpace(config["Empresa:Nombre"]) ? "Jimaco" : config["Empresa:Nombre"]!.Trim();
// Los campos personalizados de WO no significan lo mismo en cada empresa (confirmado con datos reales
// de FERRALIADOS2026): en Ferrealiados la COT va en el Personalizado2 de la REM (Jimaco: 4), el lugar de
// entrega en el Personalizado1 de la COT (Jimaco: 2) y las observaciones del asesor en el
// Personalizado1 de la OC (Jimaco: 4). El vínculo OC → COT es el mismo en las dos: NúmDocumentoExterno.
var campoEntregaEnCotizacion = WorldOfficeReader.ColumnaPersonalizado(config["MapeoWO:CampoEntregaEnCotizacion"] ?? "Personalizado2");
var observacionesEnOC = WorldOfficeReader.ColumnaPersonalizado(config["MapeoWO:ObservacionesAdicionalesEnOC"] ?? "Personalizado4");
var cotizacionEnRemision = WorldOfficeReader.ColumnaPersonalizado(config["MapeoWO:CotizacionEnRemision"] ?? "Personalizado4");
// Prefijo del CE que Contabilidad escribe solo con el número al aprobar el paso contable: Jimaco "CEJ",
// Ferrealiados "CE" (confirmado en FERRALIADOS2026).
var prefijoCEPorDefecto = string.IsNullOrWhiteSpace(config["MapeoWO:PrefijoCE"]) ? "CEJ" : config["MapeoWO:PrefijoCE"]!.Trim();
Console.WriteLine($"[{DateTime.Now:s}] Empresa: {empresa} — COT en REM: {cotizacionEnRemision}, campo entrega en COT: {campoEntregaEnCotizacion}, observaciones en OC: {observacionesEnOC}.");

var lector = new WorldOfficeReader(connectionStringWorldOffice);

using var http = new HttpClient { BaseAddress = new Uri(jimacoApiUrl) };
http.DefaultRequestHeaders.Add("X-Empresa", empresa);
var jimaco = new JimacoAprobacionesClient(http, jimacoEmail, jimacoPassword);

if (!procesarCorreoNuevo)
{
    Console.WriteLine($"[{DateTime.Now:s}] Sincronizador:ProcesarCorreoNuevo=false — se omite la detección de documentos nuevos por correo (esta es una copia secundaria, ver appsettings).");
}
else
{
    var correoHost = config["Correo:Host"]
        ?? throw new InvalidOperationException("Falta configurar Correo:Host.");
    var correoPuerto = int.TryParse(config["Correo:Puerto"], out var pc) ? pc : 993;
    var correoUsuario = config["Correo:Usuario"]
        ?? throw new InvalidOperationException("Falta configurar Correo:Usuario.");
    var correoPassword = config["Correo:Password"]
        ?? throw new InvalidOperationException("Falta configurar Correo:Password.");
    // Opcional: nombre completo de carpeta IMAP si un filtro del servidor mueve estos correos fuera
    // de la Bandeja de entrada (ej. "INBOX.Aprobaciones") — null/vacío usa la Bandeja de entrada.
    var correoCarpeta = config["Correo:Carpeta"];

    var correo = new CorreoAprobacionWatcher(correoHost, correoPuerto, correoUsuario, correoPassword, correoCarpeta);

    // Qué prefijos de World Office importan, y a qué TipoDocumento de Jimaco Aprobaciones corresponde
    // cada uno, sale de la configuración real (admin → Tipos de documento → "Prefijo en World
    // Office") — nada hardcodeado acá. Un prefijo de pruebas sin TipoDocumento configurado
    // simplemente no se sincroniza, en vez de mezclarse por accidente con el real.
    var tiposDocumento = await jimaco.ListarTiposDocumentoAsync();
    var tipoIdPorPrefijo = tiposDocumento
        .Where(t => t.Activo && !string.IsNullOrWhiteSpace(t.PrefijoWorldOffice))
        .ToDictionary(t => t.PrefijoWorldOffice, t => t.Id);

    Console.WriteLine($"[{DateTime.Now:s}] Prefijos configurados: {string.Join(", ", tipoIdPorPrefijo.Keys)}");

    // ---- Detección de documentos nuevos: por correo, no por escanear la base ----
    // World Office (con un complemento propio, ver CorreoAprobacionWatcher) manda un correo cada vez
    // que alguien aprieta "Solicitar Aprobación" — esa es la señal real de "esto ya está listo", a
    // diferencia de escanear la tabla madre por fecha/Id, que traería documentos a medio armar (WO no
    // tiene un cierre real, se puede seguir editando después de aprobado). El correo solo dice QUÉ
    // documento revisar; los datos siempre se leen de la base (fuente de verdad), nunca del texto del
    // correo.
    Console.WriteLine($"[{DateTime.Now:s}] Revisando bandeja de {correoUsuario} por solicitudes de aprobación nuevas...");
    var solicitudes = await correo.ObtenerSolicitudesNuevasAsync();
    Console.WriteLine($"[{DateTime.Now:s}] Encontradas {solicitudes.Count} solicitud(es) nueva(s).");

    foreach (var solicitud in solicitudes)
    {
        try
    {
        if (!tipoIdPorPrefijo.TryGetValue(solicitud.Prefijo, out var tipoDocumentoId))
        {
            Console.WriteLine($"[{DateTime.Now:s}]   {solicitud.Prefijo} {solicitud.DocumentoNumero} -> prefijo sin TipoDocumento configurado, se omite (correo queda sin marcar, para revisar a mano).");
            continue;
        }

        var oc = await lector.ObtenerDocumentoPorReferenciaAsync(solicitud.Prefijo, solicitud.DocumentoNumero);
        if (oc is null)
        {
            Console.WriteLine($"[{DateTime.Now:s}]   {solicitud.Prefijo} {solicitud.DocumentoNumero} -> no se encontró en World Office (o está anulado), se omite (correo queda sin marcar, para revisar a mano).");
            continue;
        }

        var proveedor = oc.IdTerceroExterno is int idProveedor
            ? await lector.ObtenerTerceroAsync(idProveedor)
            : null;
        var elaboradoPor = oc.IdTerceroInterno is int idElaborador
            ? await lector.ObtenerTerceroAsync(idElaborador)
            : null;
        var direccionProveedor = oc.IdTerceroExterno is int idProveedorDir
            ? await lector.ObtenerDireccionPrincipalAsync(idProveedorDir)
            : null;
        var formaDePago = oc.IdFormaDePago is int idFormaDePago
            ? await lector.ObtenerFormaDePagoAsync(idFormaDePago)
            : null;
        var valorTotal = await lector.ObtenerValorTotalAsync(oc.IdAsientoContable);
        var renglonesWO = await lector.ObtenerRenglonesAsync(oc.IdAsientoContable);

        var datos = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(oc.NumDocumentoExterno))
            datos["cotizacionOrigen"] = oc.NumDocumentoExterno;
        if (!string.IsNullOrWhiteSpace(elaboradoPor?.NombreCompleto))
            datos["elaboradoPor"] = elaboradoPor.NombreCompleto;
        if (!string.IsNullOrWhiteSpace(proveedor?.Identificacion))
            datos["nit"] = proveedor.Identificacion;
        if (!string.IsNullOrWhiteSpace(direccionProveedor))
            datos["direccionProveedor"] = direccionProveedor;
        if (!string.IsNullOrWhiteSpace(formaDePago))
            datos["formaDePago"] = formaDePago;
        if (!string.IsNullOrWhiteSpace(oc.Nota))
            datos["observaciones"] = oc.Nota;
        if (!string.IsNullOrWhiteSpace(oc.Personalizado(observacionesEnOC)))
            datos["observacionesAdicionales"] = oc.Personalizado(observacionesEnOC)!;

        var renglones = renglonesWO
            .Select(r => new RenglonInputDto(
                Codigo: r.Codigo,
                Descripcion: r.Descripcion ?? "(sin descripción)",
                Cantidad: r.Cantidad,
                UnidadMedida: r.UnidadMedida,
                ValorUnitario: r.ValorUnitario,
                PorcentajeIva: r.PorcentajeIva ?? 0m))
            .ToList();

        var documentosVinculados = await ObtenerDocumentosVinculadosAsync(oc.NumDocumentoExterno);

        var dto = new CrearInstanciaDocumentoDto(
            TipoDocumentoId: tipoDocumentoId,
            NumeroReferencia: oc.DocumentoNumero.ToString("0"),
            Proveedor: proveedor?.NombreCompleto,
            // Si hay renglones reales, el Valor lo termina calculando el motor a partir de ellos
            // (mismo criterio que la carga manual) — se manda igual el total ya sumado acá como
            // respaldo por si la OC no trae renglones (no debería pasar, pero mejor no dejar el
            // documento en $0 en ese caso).
            Valor: valorTotal,
            FechaDocumento: oc.Fecha,
            Datos: datos.Count > 0 ? datos : null,
            Renglones: renglones.Count > 0 ? renglones : null,
            IdAsientoContableOrigen: oc.IdAsientoContable,
            PrefijoOrigen: oc.Prefijo,
            DocumentosVinculados: documentosVinculados.Count > 0 ? documentosVinculados : null,
            SolicitanteWO: oc.SolicitanteWO);

        var creado = await jimaco.CrearDocumentoAsync(dto);
        Console.WriteLine($"[{DateTime.Now:s}]   {oc.Prefijo} {dto.NumeroReferencia} -> creado como documento #{creado.Id} ({proveedor?.NombreCompleto ?? "proveedor desconocido"}, ${valorTotal:N0})");

        // Recién ahora se marca el correo como procesado — si algo falla antes de esta línea, el
        // correo queda sin leer y la próxima corrida lo vuelve a intentar solo.
        await correo.MarcarProcesadoAsync(solicitud.MensajeId);
    }
    catch (Exception ex)
    {
        // A diferencia del viejo lote por marca de agua, acá cada solicitud es independiente —
        // una que falla no bloquea a las demás (no hay un orden que preservar entre correos).
        Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR procesando {solicitud.Prefijo} {solicitud.DocumentoNumero}: {ex.Message}");
    }
    }
}

// ---- Fase 2: escribir de vuelta en World Office las aprobaciones del primer paso ----
// Opt-in a propósito: si no está configurada la connection string de escritura, se omite esta
// fase entera (permite seguir usando el Sincronizador solo para lectura hasta que WO tenga
// listo un login con permiso de escritura — ver exploracion-worldoffice.sql).
var connectionStringEscritura = config["WorldOffice:ConnectionStringEscritura"];
// Sincronizador:EscribirAprobacionesWO=false (2026-11-06): la copia de QA necesita la conexión de
// escritura para crear los CE de prueba (prefijo CEJ-2), pero NUNCA debe reflejar en la WO real
// las aprobaciones de OC hechas en QA.
var escribirAprobacionesWO = !bool.TryParse(config["Sincronizador:EscribirAprobacionesWO"], out var eaw) || eaw;
if (string.IsNullOrWhiteSpace(connectionStringEscritura))
{
    Console.WriteLine($"[{DateTime.Now:s}] WorldOffice:ConnectionStringEscritura no configurado — se omite la escritura de vuelta a WO.");
}
else if (!escribirAprobacionesWO)
{
    Console.WriteLine($"[{DateTime.Now:s}] Sincronizador:EscribirAprobacionesWO=false — no se reflejan aprobaciones en WO (copia secundaria).");
}
else
{
    var escritor = new WorldOfficeWriter(connectionStringEscritura);

    Console.WriteLine($"[{DateTime.Now:s}] Revisando aprobaciones pendientes de reflejar en World Office...");
    var pendientes = await jimaco.ListarPendientesEscrituraWOAsync();
    Console.WriteLine($"[{DateTime.Now:s}] {pendientes.Count} documento(s) pendiente(s) de escribir en WO.");

    foreach (var p in pendientes)
    {
        try
        {
            var resultado = await escritor.EscribirPrimerPasoAsync(p.PrefijoOrigen, p.IdAsientoContableOrigen, p.UsuarioWO, p.Resultado);
            var etiquetaAccion = p.Resultado switch
            {
                ResultadoPrimerPaso.Rechazado => "rechazado",
                ResultadoPrimerPaso.Devuelto => "devuelto a pendiente",
                _ => "aprobado"
            };

            switch (resultado)
            {
                case ResultadoEscrituraWO.Escrito:
                case ResultadoEscrituraWO.YaEstabaAprobado:
                    await jimaco.ConfirmarEscrituraWOAsync(p.InstanciaDocumentoId);
                    Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} ({p.NumeroReferencia}) -> {resultado} ({etiquetaAccion}) en WO.");
                    break;

                case ResultadoEscrituraWO.Anulado:
                    await jimaco.ReportarConflictoWOAsync(p.InstanciaDocumentoId,
                        $"El documento ya estaba anulado en World Office al momento de sincronizar el {etiquetaAccion}.");
                    Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} -> CONFLICTO: anulado en WO, no se escribió nada.");
                    break;

                case ResultadoEscrituraWO.EstadoDistinto:
                    await jimaco.ReportarConflictoWOAsync(p.InstanciaDocumentoId,
                        $"Se quiso marcar como {etiquetaAccion} en World Office, pero la fila ya tenía otro estado distinto (probablemente alguien lo aprobó/rechazó por Access). No se pisó nada.");
                    Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} -> CONFLICTO: estado distinto en WO, no se escribió nada.");
                    break;

                case ResultadoEscrituraWO.NoEncontrado:
                    await jimaco.ReportarConflictoWOAsync(p.InstanciaDocumentoId,
                        $"No se encontró en World Office la fila prefijo={p.PrefijoOrigen} IdAsientoContable={p.IdAsientoContableOrigen}.");
                    Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} -> CONFLICTO: no encontrado en WO.");
                    break;
            }
        }
        catch (Exception ex)
        {
            // A diferencia del lote de OC nuevas (que se detiene entero ante un error), acá cada
            // documento pendiente es independiente — uno que falla no bloquea a los demás, y
            // sigue en la cola para que la próxima corrida lo reintente solo.
            Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR escribiendo en WO el documento #{p.InstanciaDocumentoId}: {ex.Message}");

            // Un error "durante ejecución de trigger" en SQL Server siempre trae el mensaje real
            // (el que disparó el RAISERROR/THROW dentro del trigger) en la colección Errors, no
            // necesariamente en ex.Message — sin esto, el mensaje útil queda oculto.
            if (ex is Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                foreach (Microsoft.Data.SqlClient.SqlError error in sqlEx.Errors)
                    Console.Error.WriteLine($"[{DateTime.Now:s}]     SQL Error {error.Number} (Procedure: {error.Procedure}, Line: {error.LineNumber}): {error.Message}");
            }
        }
    }
}

// ---- Comprobante de Egreso (2026-09-22) — "pedir y recoger": Contabilidad escribió el número acá
// al aprobar su paso, se busca en WO (prefijo "CEJ" — confirmado con datos reales el 2026-09-22,
// el "ce" apuntado antes fue una mala lectura de una captura; misma tabla madre que OC/COT) y se
// empuja de vuelta armado, para adjuntarlo a la OC en el paso de Gerencia General. Usa la
// connection string de SOLO LECTURA (nunca se escribe nada en WO acá) — corre siempre, no depende
// de ConnectionStringEscritura. ----
// ---- Pagos a proveedor confirmados (2026-11-06): crear el Comprobante de Egreso en WO. Va ANTES
// de la búsqueda de comprobantes de abajo a propósito: al confirmar el CE creado, la Api lo deja en
// esa misma cola y en esta misma corrida se trae la copia y sale el aviso de "pago completado".
// Escribe en WO → necesita ConnectionStringEscritura (la misma de la Fase 2, Integrated Security). ----
if (string.IsNullOrWhiteSpace(connectionStringEscritura))
{
    Console.WriteLine($"[{DateTime.Now:s}] WorldOffice:ConnectionStringEscritura no configurado — no se crean comprobantes de egreso de pagos a proveedor.");
}
else if (bool.TryParse(config["CuentasPorPagar:CrearCE"], out var crearCeActivo) && !crearCeActivo)
{
    // Freno de emergencia local: aunque en la página esté encendido, CrearCE=false en el appsettings apaga.
    Console.WriteLine($"[{DateTime.Now:s}] CuentasPorPagar:CrearCE=false — no se crean comprobantes de egreso de pagos a proveedor.");
}
else if (await ObtenerConfiguracionCEAsync() is not { } configuracionCe)
{
    // Ya avisado adentro (Api sin el endpoint o caída): sin configuración NO se crea nada.
}
else if (!configuracionCe.CrearCEEnWorldOffice)
{
    Console.WriteLine($"[{DateTime.Now:s}] Crear CE en World Office está APAGADO en la página (Administración → Cuentas por pagar) — no se crean comprobantes de egreso.");
}
else
{
    var configCe = new ConfiguracionCE(
        // Desde 2026-11-11 el prefijo lo maneja un administrador en la página (arranca en CEJ-2, de prueba).
        Prefijo: configuracionCe.PrefijoCE,
        IdTerceroElabora: int.TryParse(config["CuentasPorPagar:IdTerceroElaboraCE"], out var idElabora) ? idElabora : 9700,
        UsuarioWO: config["CuentasPorPagar:UsuarioWOCE"] ?? "CAMILO",
        NomPc: config["CuentasPorPagar:NomPcCE"] ?? "APROBACIONES");
    var escritorCe = new ComprobanteEgresoWriter(connectionStringEscritura, configCe);

    IReadOnlyList<PendienteCreacionCEDto> pendientesCe;
    try
    {
        if (!await escritorCe.PrefijoExisteAsync())
        {
            Console.Error.WriteLine($"[{DateTime.Now:s}] ERROR: el prefijo de CE \"{configCe.Prefijo}\" configurado en la página no tiene ningún CE en World Office — no se crea nada (revisar Administración → Cuentas por pagar).");
            pendientesCe = [];
        }
        else
        {
            pendientesCe = await jimaco.ListarPendientesCreacionCEAsync();
        }
    }
    catch (Exception ex)
    {
        // Ej. una Api que todavía no tiene este endpoint — no debe frenar el resto de la corrida.
        Console.Error.WriteLine($"[{DateTime.Now:s}] ERROR listando pagos a proveedor pendientes de CE: {ex.Message}");
        pendientesCe = [];
    }
    Console.WriteLine($"[{DateTime.Now:s}] {pendientesCe.Count} pago(s) a proveedor pendiente(s) de crear el CE en WO (prefijo {configCe.Prefijo}).");

    foreach (var p in pendientesCe)
    {
        try
        {
            var resultado = await escritorCe.CrearAsync(p);
            switch (resultado.Resultado)
            {
                case ResultadoCreacionCE.Creado:
                case ResultadoCreacionCE.YaExistia:
                    await jimaco.ConfirmarCECreadoAsync(p.InstanciaDocumentoId,
                        new CECreadoDto(resultado.Prefijo!, resultado.Numero!.Value, resultado.IdAsientoContable!.Value));
                    Console.WriteLine($"[{DateTime.Now:s}]   Pago #{p.InstanciaDocumentoId} ({p.NumeroReferencia}) -> CE {resultado.Prefijo} {resultado.Numero:0} " +
                                      $"{(resultado.Resultado == ResultadoCreacionCE.YaExistia ? "ya existía (corrida anterior)" : "creado")} por ${p.Neto:N0}.");
                    break;

                case ResultadoCreacionCE.Conflicto:
                    await jimaco.ReportarConflictoCreacionCEAsync(p.InstanciaDocumentoId, resultado.Mensaje!);
                    Console.WriteLine($"[{DateTime.Now:s}]   Pago #{p.InstanciaDocumentoId} -> CONFLICTO, no se creó el CE: {resultado.Mensaje}");
                    break;
            }
        }
        catch (Exception ex)
        {
            // Error técnico (WO caído, red...): sigue en la cola y se reintenta en la próxima corrida.
            // Si el CE alcanzó a grabarse, la marca en la nota evita duplicarlo (ver ComprobanteEgresoWriter).
            Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR creando el CE del pago #{p.InstanciaDocumentoId}: {ex.Message}");
            if (ex is Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                foreach (Microsoft.Data.SqlClient.SqlError error in sqlEx.Errors)
                    Console.Error.WriteLine($"[{DateTime.Now:s}]     SQL Error {error.Number} (Procedure: {error.Procedure}, Line: {error.LineNumber}): {error.Message}");
            }
        }
    }
}

Console.WriteLine($"[{DateTime.Now:s}] Revisando comprobantes de egreso pendientes de buscar en World Office...");
var pendientesComprobante = await jimaco.ListarPendientesComprobanteEgresoAsync();
Console.WriteLine($"[{DateTime.Now:s}] {pendientesComprobante.Count} documento(s) pendiente(s) de comprobante de egreso.");

foreach (var p in pendientesComprobante)
{
    try
    {
        // "15400" (escrito por Contabilidad, siempre CEJ) o "CEJ-2 22" (CE creado por el sistema,
        // que trae su prefijo porque en pruebas no es CEJ).
        var textoComprobante = p.NumeroComprobanteEgresoSolicitado.Trim();
        var prefijoComprobante = prefijoCEPorDefecto;
        var espacio = textoComprobante.LastIndexOf(' ');
        if (espacio > 0)
        {
            prefijoComprobante = textoComprobante[..espacio].Trim();
            textoComprobante = textoComprobante[(espacio + 1)..];
        }

        if (!decimal.TryParse(textoComprobante, System.Globalization.CultureInfo.InvariantCulture, out var numeroComprobante))
        {
            await jimaco.ReportarConflictoComprobanteEgresoAsync(p.InstanciaDocumentoId,
                $"\"{p.NumeroComprobanteEgresoSolicitado}\" no es un número de comprobante válido.");
            Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} -> CONFLICTO: número de comprobante inválido.");
            continue;
        }

        // Mismo mecanismo que ObtenerDocumentosVinculadosAsync (misma tabla madre, prefijo "CEJ" en
        // vez de "COT") — ver comentario de esquema en WorldOfficeReader.
        var ce = await lector.ObtenerDocumentoVinculadoAsync(prefijoComprobante, numeroComprobante);
        if (ce is null)
        {
            await jimaco.ReportarConflictoComprobanteEgresoAsync(p.InstanciaDocumentoId,
                $"No se encontró el comprobante de egreso {p.NumeroComprobanteEgresoSolicitado} en World Office (o está anulado). Verificá el número.");
            Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} -> CONFLICTO: comprobante {p.NumeroComprobanteEgresoSolicitado} no encontrado en WO.");
            continue;
        }

        var beneficiario = ce.IdTerceroExterno is int idBeneficiario ? await lector.ObtenerTerceroAsync(idBeneficiario) : null;
        var elaboradoPorCe = ce.IdTerceroInterno is int idElaboradorCe ? await lector.ObtenerTerceroAsync(idElaboradorCe) : null;
        var direccionCe = ce.IdDireccionExterno is int idDireccionCe ? await lector.ObtenerDireccionPorIdAsync(idDireccionCe) : null;

        var detalle = await lector.ObtenerDetalleComprobanteEgresoAsync(ce.IdAsientoContable);
        var cuentas = (await lector.ObtenerCuentasContablesAsync(detalle.Select(d => d.IdCuentaContable).Distinct().ToList()))
            .ToDictionary(c => c.IdCuentaContable);

        // Partida doble: débitos y créditos deben sumar igual — el valor del comprobante es esa
        // suma (mismo criterio que ObtenerValorTotalAsync para la OC).
        var valorComprobante = detalle.Sum(d => d.Debito);

        var renglonesComprobante = detalle.Select(d =>
        {
            cuentas.TryGetValue(d.IdCuentaContable, out var cuenta);
            return new RenglonComprobanteEgresoInputDto(
                CodigoCuenta: cuenta?.CodigoCuentaContable,
                NombreCuenta: cuenta?.CuentaContable,
                Concepto: d.Nota,
                Tercero: null,
                Debito: d.Debito,
                Credito: d.Credito);
        }).ToList();

        var encontrado = new ComprobanteEgresoEncontradoDto(
            NumeroReferencia: ce.DocumentoNumero.ToString("0"),
            Fecha: ce.Fecha,
            Valor: valorComprobante,
            Beneficiario: beneficiario?.NombreCompleto,
            NitBeneficiario: beneficiario?.Identificacion,
            Direccion: direccionCe?.Direccion,
            Ciudad: direccionCe?.Ciudad,
            Telefono: direccionCe?.Telefonos,
            Concepto: ce.Nota,
            ElaboradoPor: elaboradoPorCe?.NombreCompleto,
            Renglones: renglonesComprobante);

        await jimaco.AdjuntarComprobanteEgresoAsync(p.InstanciaDocumentoId, encontrado);
        Console.WriteLine($"[{DateTime.Now:s}]   Documento #{p.InstanciaDocumentoId} -> comprobante de egreso {p.NumeroComprobanteEgresoSolicitado} adjuntado (${valorComprobante:N0}, {beneficiario?.NombreCompleto ?? "beneficiario desconocido"}).");
    }
    catch (Exception ex)
    {
        // Mismo criterio que el resto de las colas: cada documento pendiente es independiente, uno
        // que falla no bloquea a los demás y sigue en la cola para la próxima corrida.
        Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR buscando comprobante de egreso del documento #{p.InstanciaDocumentoId}: {ex.Message}");
    }
}

// ---- Refrescar Hojas de Alistamiento activas (2026-10-14) — pedido real: una Cotización sigue
// siendo editable en World Office DESPUÉS del primer sync (cambian ítems, cantidades, el campo de
// entrega...), pero hasta ahora el Sincronizador solo la leía una vez — se detectaron casos reales
// (Hojas 16160 y 16141) con datos viejos sin ninguna forma de refrescarse sola. Esta lista es
// INDEPENDIENTE de la de "pendientes de remisión" de abajo (2026-10-14) — el corte acá es "no
// Completa", no "no Remisionada". Antes se reusaba la lista de remisión para esto, lo que dejaba de
// refrescar una Hoja apenas se marcaba Remisionada aunque Transporte todavía no hubiera terminado de
// alistarla (caso real: Hoja 16159, Remisionada pero con ítems sin completar, que quedó congelada).
// Las cantidades que Transporte ya marcó como disponibles se CONSERVAN para los ítems que sigan
// siendo los mismos (ver HojaAlistamientoService.RefrescarDesdeWOAsync del lado de la Api — ahí se
// hace el emparejamiento real, acá solo se manda la foto actual). ----
Console.WriteLine($"[{DateTime.Now:s}] Revisando Hojas de Alistamiento pendientes de refresco...");
var pendientesRefresco = await jimaco.ListarPendientesRefrescoAsync();
Console.WriteLine($"[{DateTime.Now:s}] {pendientesRefresco.Count} Hoja(s) pendiente(s) de refresco.");

foreach (var p in pendientesRefresco)
{
    try
    {
        if (!decimal.TryParse(p.NumeroReferencia, System.Globalization.CultureInfo.InvariantCulture, out var numeroCotizacion))
        {
            Console.WriteLine($"[{DateTime.Now:s}]   Cotización \"{p.NumeroReferencia}\" no es un número válido, se omite el refresco.");
            continue;
        }

        var dtoRefresco = await ObtenerDocumentoVinculadoInputAsync(numeroCotizacion);
        if (dtoRefresco is null)
        {
            Console.WriteLine($"[{DateTime.Now:s}]   Cotización {p.NumeroReferencia} ya no se encuentra en WO (o quedó anulada), se omite el refresco.");
            continue;
        }

        await jimaco.RefrescarHojaAlistamientoAsync(dtoRefresco);
    }
    catch (Exception ex)
    {
        // Independiente por Cotización — si una falla, las demás se refrescan igual, y esta se
        // reintenta sola en la próxima corrida.
        Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR refrescando la Hoja de Alistamiento de la Cotización {p.NumeroReferencia}: {ex.Message}");
    }
}

Console.WriteLine($"[{DateTime.Now:s}] Revisando Hojas de Alistamiento pendientes de remisión...");
var pendientesRemision = await jimaco.ListarPendientesRemisionAsync();
Console.WriteLine($"[{DateTime.Now:s}] {pendientesRemision.Count} Cotización(es) pendiente(s) de remisión.");

if (pendientesRemision.Count > 0)
{
    var diasAtrasRemision = int.TryParse(config["Alistamiento:DiasAtrasRemision"], out var dar) ? dar : 60;
    var remisionesRecientes = await lector.ObtenerRemisionesRecientesAsync(diasAtrasRemision, columnaCotizacion: cotizacionEnRemision);

    // Por cada número que aparece en el campo libre de una Remisión, TODAS las Remisiones que lo
    // mencionan cuentan (2026-10-26, antes solo se quedaba con la más antigua) — un mismo ítem puede
    // llegar repartido en varias Remisiones parciales distintas, así que hay que aplicarlas todas,
    // no solo la primera evidencia.
    var remisionesPorCotizacion = new Dictionary<string, List<RemisionRow>>();
    foreach (var rem in remisionesRecientes)
    {
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(rem.Cotizacion ?? "", @"\d+"))
        {
            var numero = NormalizarNumero(m.Value);
            if (!remisionesPorCotizacion.TryGetValue(numero, out var lista))
                remisionesPorCotizacion[numero] = lista = [];
            lista.Add(rem);
        }
    }

    foreach (var p in pendientesRemision)
    {
        if (!remisionesPorCotizacion.TryGetValue(NormalizarNumero(p.NumeroReferencia), out var remisiones)) continue;

        foreach (var rem in remisiones)
        {
            try
            {
                // Renglones REALES de esta Remisión (código + cantidad) — se cruzan por Código contra
                // los de la Hoja del lado de la Api (HojaAlistamientoService.AplicarDespachoRemision),
                // nunca se asume que todo el documento se despachó entero.
                var renglonesRemision = await lector.ObtenerRenglonesAsync(rem.IdAsientoContable);
                var renglonesDto = renglonesRemision
                    .Select(r => new RenglonRemisionadoDto(r.Codigo, r.Cantidad))
                    .ToList();

                await jimaco.MarcarRemisionadaAsync(new MarcarRemisionadaDto(p.PrefijoOrigen, p.NumeroReferencia, rem.DocumentoNumero.ToString("0"), rem.Fecha, renglonesDto));
                Console.WriteLine($"[{DateTime.Now:s}]   Cotización {p.NumeroReferencia} -> despacho de Remisión {rem.DocumentoNumero:0} ({rem.Fecha:d}), {renglonesDto.Count} renglón(es).");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR marcando remisión de la Cotización {p.NumeroReferencia} (Remisión {rem.DocumentoNumero:0}): {ex.Message}");
            }
        }
    }
}

// ---- Hojas de Alistamiento sin OC asociada (2026-10-21/22) — pedido real: Cotizaciones que
// Transporte necesita alistar aunque nunca pase (ni vaya a pasar) una Orden de Compra por el flujo
// de aprobación. El filtro de ruido lo resuelve ObtenerCotizacionesRecientesAsync (el vendedor marca
// a propósito el campo "Contacto" en WO con la palabra "INVENTARIO") — acá no hay ninguna heurística
// de negocio, solo se recorre lo que YA viene filtrado. No depende de procesarCorreoNuevo — a
// diferencia de la detección por correo, esto no compite por ningún recurso compartido (cada copia
// del Sincronizador apunta a su propia Api). El endpoint es idempotente (CrearSinOcAsync no-opea si
// ya existe cualquier copia para esa Cotización, con o sin OC), así que acá simplemente se reescanea
// una ventana corta de días en cada corrida — más simple y más robusto que llevar una marca de agua
// propia (si una corrida se cae a mitad de camino, la próxima retoma sola, sin estado que perder).
// Ferrealiados no usa la marca "INVENTARIO" (0 cotizaciones) y su Personalizado1 es texto libre del
// asesor — se apaga con Alistamiento:CotizacionesSinOcActivo=false para no crear hojas por error.
var cotizacionesSinOcActivo = !bool.TryParse(config["Alistamiento:CotizacionesSinOcActivo"], out var csoa) || csoa;
if (!cotizacionesSinOcActivo)
    Console.WriteLine($"[{DateTime.Now:s}] Alistamiento:CotizacionesSinOcActivo=false — no se crean Hojas de Alistamiento sin OC.");
else
{
Console.WriteLine($"[{DateTime.Now:s}] Revisando Cotizaciones recientes marcadas para alistamiento sin OC...");
// Piso fijo, no una ventana móvil (pedido explícito del usuario, 2026-10-22: "que solo busque a
// partir de hoy, lo antiguo no lo traiga") — se configura UNA vez, el día en que esto se activa en
// cada ambiente (QA, después producción), y nunca se mueve solo. A propósito sin valor por defecto:
// si falta, es mejor que la corrida truene acá a que arranque trayendo Cotizaciones viejas por
// descuido.
var fechaInicioCotizacionesSinOc = DateTime.TryParse(config["Alistamiento:FechaInicioCotizacionesSinOc"], out var fic)
    ? fic
    : throw new InvalidOperationException("Falta configurar Alistamiento:FechaInicioCotizacionesSinOc (fecha, formato \"yyyy-MM-dd\") — el día desde el cual se empiezan a traer Cotizaciones marcadas con \"INVENTARIO\", nunca antes.");
var diasAtrasCotizacionesSinOc = int.TryParse(config["Alistamiento:DiasAtrasCotizacionesSinOc"], out var dac) ? dac : 60;
var cotizacionesRecientes = await lector.ObtenerCotizacionesRecientesAsync(fechaInicioCotizacionesSinOc, diasAtrasCotizacionesSinOc);
Console.WriteLine($"[{DateTime.Now:s}] {cotizacionesRecientes.Count} Cotización(es) reciente(s) encontrada(s).");

foreach (var cot in cotizacionesRecientes)
{
    try
    {
        var dtoSinOc = await ObtenerDocumentoVinculadoInputAsync(cot.DocumentoNumero);
        if (dtoSinOc is null) continue; // se anuló entre la lectura de la lista y acá, o algo raro — se omite, no es fatal

        await jimaco.CrearHojaAlistamientoSinOcAsync(dtoSinOc);
    }
    catch (Exception ex)
    {
        // Independiente por Cotización — si una falla, las demás se procesan igual, y esta se
        // reintenta sola en la próxima corrida (reescanea la misma ventana de días).
        Console.Error.WriteLine($"[{DateTime.Now:s}]   ERROR creando Hoja de Alistamiento sin OC para la Cotización {cot.DocumentoNumero:0}: {ex.Message}");
    }
}
}

// ---- Cuentas por pagar (2026-11-05) — foto completa en cada corrida (no hay marca de agua: lo que
// importa es el saldo de HOY, y la vista de WO ya descuenta los abonos). Son pocas cientos de filas,
// la Api reemplaza la foto anterior entera. Solo lectura, misma connection string que el resto. Un
// error acá no frena nada más: la pantalla sigue mostrando la foto anterior con su hora de corte. ----
if (bool.TryParse(config["CuentasPorPagar:Activo"], out var cxpActivo) && !cxpActivo)
{
    Console.WriteLine($"[{DateTime.Now:s}] CuentasPorPagar:Activo=false — se omiten las cuentas por pagar.");
}
else
{
    try
    {
        var prefijosCuenta = (config["CuentasPorPagar:PrefijosCuenta"] ?? "2205,2210,2335")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var cuentaAnticipos = config["CuentasPorPagar:CuentaAnticipos"] ?? "133005";

        var prefijosBanco = (config["CuentasPorPagar:CuentasBanco"] ?? "1105,1110")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        // Bancolombia cta. cte. — de ahí salen ~95% de los CE (exploración 2026-11-05).
        var bancoPorDefecto = config["CuentasPorPagar:BancoPorDefecto"] ?? "11100502";

        var partidasCxp = await lector.ObtenerCuentasPorPagarAsync(prefijosCuenta);
        var anticiposCxp = string.IsNullOrWhiteSpace(cuentaAnticipos) ? [] : await lector.ObtenerAnticiposProveedorAsync(cuentaAnticipos);
        var bancosCxp = await lector.ObtenerCuentasBancoAsync(prefijosBanco);

        await jimaco.EnviarCuentasPorPagarAsync(new FotoCuentasPorPagarDto(
            Partidas: partidasCxp.Select(p => new PartidaCuentaPorPagarInputDto(
                IdDocumentoDetalleWO: p.AbonaADocumento,
                Identificacion: p.Identificacion ?? "",
                NombreTercero: p.NombresTerceros ?? "",
                Documento: p.Documento,
                Prefijo: p.Prefijo,
                NumeroDocumento: p.DocumentoNumero?.ToString("0"),
                NumeroDocumentoExterno: p.NumDocumentoExterno,
                Fecha: p.Fecha,
                Vencimiento: p.Vencimiento,
                SaldoWO: p.Saldo,
                CodigoCuenta: p.CodigoCuentaContable)).ToList(),
            Anticipos: anticiposCxp.Select(a => new AnticipoProveedorInputDto(
                a.Identificacion ?? "", a.NombreTercero ?? "", a.Movimientos, a.Saldo,
                a.IdDocumentoDetalle, a.Prefijo, a.DocumentoNumero?.ToString("0"), a.Fecha, a.Nota)).ToList(),
            Bancos: bancosCxp.Select(b => new CuentaBancoInputDto(
                b.IdCuentaContable, b.CodigoCuentaContable ?? "", b.CuentaContable ?? "",
                PorDefecto: b.CodigoCuentaContable?.Trim() == bancoPorDefecto)).ToList()));

        Console.WriteLine($"[{DateTime.Now:s}] Cuentas por pagar: {partidasCxp.Count} partida(s) en {string.Join("/", prefijosCuenta)}, {anticiposCxp.Count} anticipo(s) sin cruzar, {bancosCxp.Count} banco(s)/caja(s).");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:s}] ERROR enviando cuentas por pagar: {ex.Message}");
    }
}

// ---- Catálogo de terceros de WO (2026-11-09) — para el beneficiario de un pago extraordinario.
// Son miles de filas: se manda solo si la última vez fue hace más de CuentasPorPagar:HorasTerceros
// (default 6), no en cada corrida. Un error acá no frena nada más. ----
if (!(bool.TryParse(config["CuentasPorPagar:Activo"], out var cxpTercerosActivo) && !cxpTercerosActivo))
{
    try
    {
        var horasTerceros = double.TryParse(config["CuentasPorPagar:HorasTerceros"], System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 6;
        var ultimaTerceros = await jimaco.ObtenerFechaCorteTercerosAsync();
        if (ultimaTerceros is null || DateTime.UtcNow - DateTime.SpecifyKind(ultimaTerceros.Value, DateTimeKind.Utc) > TimeSpan.FromHours(horasTerceros))
        {
            var terceros = await lector.ObtenerTercerosAsync();
            await jimaco.EnviarTercerosAsync(terceros
                .Where(t => !string.IsNullOrWhiteSpace(t.Identificacion))
                .Select(t => new TerceroWOInputDto(t.IdTercero, t.Identificacion!.Trim(), t.NombreCompleto.Trim()))
                .ToList());
            Console.WriteLine($"[{DateTime.Now:s}] Catálogo de terceros enviado: {terceros.Count} tercero(s).");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:s}] ERROR enviando el catálogo de terceros: {ex.Message}");
    }
}

// ---- Entregas (2026-11-12): Remisiones CONTABILIZADAS → dashboard de Logística. Desde la fecha de
// inicio que fija la Api (el día que el módulo salió a ese ambiente). Contabilizada = tiene
// movimientos contables (WO no tiene bandera en el encabezado). Ya contabilizada WO no deja
// editarla, así que se manda una sola vez; si después se anula, se avisa para sacarla del
// dashboard. Entregas:Activo=false lo apaga. Un error acá no frena nada más. ----
if (bool.TryParse(config["Entregas:Activo"], out var entregasActivo) && !entregasActivo)
{
    Console.WriteLine($"[{DateTime.Now:s}] Entregas:Activo=false — se omiten las remisiones.");
}
else
{
    try
    {
        var estadoRem = await jimaco.ObtenerEstadoRemisionesAsync();
        var conocidas = estadoRem.IdsConocidos.ToHashSet();
        var remisionesWO = await lector.ObtenerRemisionesParaEntregaAsync(estadoRem.FechaInicio);

        var anuladas = remisionesWO.Where(r => r.Anulada && conocidas.Contains(r.IdAsientoContable))
            .Select(r => r.IdAsientoContable).ToList();
        if (anuladas.Count > 0)
            await jimaco.MarcarRemisionesAnuladasAsync(anuladas);

        var nuevas = remisionesWO.Where(r => !r.Anulada && r.Contabilizada && !conocidas.Contains(r.IdAsientoContable)).ToList();
        var lote = new List<RemisionInputDto>();
        foreach (var rem in nuevas)
        {
            var renglonesRem = await lector.ObtenerRenglonesAsync(rem.IdAsientoContable);
            if (renglonesRem.Count == 0) continue;
            var clienteRem = rem.IdTerceroExterno is int idCli ? await lector.ObtenerTerceroAsync(idCli) : null;
            var vendedorRem = rem.IdTerceroInterno is int idVen ? await lector.ObtenerTerceroAsync(idVen) : null;
            var direccionRem = rem.IdDireccionExterno is int idDir ? await lector.ObtenerDireccionPorIdAsync(idDir) : null;
            var formaPagoRem = rem.IdFormaDePago is int idFp ? await lector.ObtenerFormaDePagoAsync(idFp) : null;

            lote.Add(new RemisionInputDto(
                IdAsientoContableWO: rem.IdAsientoContable,
                Prefijo: "REM",
                Numero: rem.DocumentoNumero.ToString("0"),
                Fecha: rem.Fecha,
                Cliente: clienteRem?.NombreCompleto,
                NitCliente: clienteRem?.Identificacion,
                Direccion: direccionRem?.Direccion,
                Ciudad: direccionRem?.Ciudad,
                Telefono: direccionRem?.Telefonos,
                Vendedor: vendedorRem?.NombreCompleto,
                ElaboradoPorWO: rem.IdUsuarioWO,
                FormaDePago: formaPagoRem,
                DireccionEntrega: rem.Personalizado1,
                CampoEntrega: rem.Personalizado2,
                OrdenCompraCliente: rem.Personalizado3,
                Cotizacion: rem.Personalizado4,
                Nota: rem.Nota,
                Renglones: renglonesRem.Select(x => new RenglonRemisionInputDto(
                    x.Codigo, x.Descripcion ?? "(sin descripción)", x.Cantidad, x.UnidadMedida)).ToList()));
        }

        if (lote.Count > 0)
        {
            var resultadoRem = await jimaco.RegistrarRemisionesAsync(lote);
            Console.WriteLine($"[{DateTime.Now:s}] Entregas: {resultadoRem.Creadas} remisión(es) nueva(s) al dashboard.");
        }
        var sinContabilizar = remisionesWO.Count(r => !r.Anulada && !r.Contabilizada && !conocidas.Contains(r.IdAsientoContable));
        Console.WriteLine($"[{DateTime.Now:s}] Entregas: desde {estadoRem.FechaInicio:yyyy-MM-dd} — {lote.Count} enviada(s), {sinContabilizar} sin contabilizar todavía, {anuladas.Count} anulada(s).");
    }
    catch (Exception ex)
    {
        // Ej. una Api que todavía no tiene el módulo de Entregas.
        Console.Error.WriteLine($"[{DateTime.Now:s}] ERROR sincronizando remisiones para entregas: {ex.Message}");
    }
}

Console.WriteLine($"[{DateTime.Now:s}] Listo.");

// Texto libre escrito a mano ("16113", "COT16113"...) normalizado a la misma forma canónica que
// usa el resto del Sincronizador para números de documento (sin ceros a la izquierda).
static string NormalizarNumero(string texto) =>
    decimal.TryParse(texto, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n.ToString("0") : texto;

// ---- Documentos externos ligados (ej. la(s) Cotización(es) de origen de una OC) ----
// NúmDocumentoExterno puede traer más de un número, separados por guión (ej.
// "15864-15777-15878") — se procesa cada uno por separado. Siempre se asume prefijo "COT"
// (es la única relación confirmada hasta ahora, ver esquema-worldoffice-oc.md) — si en el
// futuro aparece otro tipo de documento ligado, esto hay que generalizarlo.
async Task<List<DocumentoVinculadoInputDto>> ObtenerDocumentosVinculadosAsync(string? numDocumentoExterno)
{
    var resultado = new List<DocumentoVinculadoInputDto>();
    if (string.IsNullOrWhiteSpace(numDocumentoExterno)) return resultado;

    foreach (var parte in numDocumentoExterno.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    {
        if (!decimal.TryParse(parte, System.Globalization.CultureInfo.InvariantCulture, out var numero))
        {
            Console.WriteLine($"[{DateTime.Now:s}]     Documento externo \"{parte}\" no es un número válido, se omite.");
            continue;
        }

        var dto = await ObtenerDocumentoVinculadoInputAsync(numero);
        if (dto is null)
        {
            Console.WriteLine($"[{DateTime.Now:s}]     Cotización COT {numero} (documento externo) no se encontró en WO, se omite.");
            continue;
        }

        resultado.Add(dto);
    }

    return resultado;
}

// Extraído tal cual estaba adentro de ObtenerDocumentosVinculadosAsync (2026-10-14) — mismo cuerpo,
// mismo comportamiento, solo para reusarlo también en la fase de "refrescar" de abajo (una
// Cotización puede seguir editándose en WO después del primer sync, y antes no había forma de
// que el Sincronizador se enterara — ver pedido real del usuario).
async Task<DocumentoVinculadoInputDto?> ObtenerDocumentoVinculadoInputAsync(decimal numero)
{
    var cot = await lector.ObtenerDocumentoVinculadoAsync("COT", numero);
    if (cot is null) return null;

    var cliente = cot.IdTerceroExterno is int idCliente ? await lector.ObtenerTerceroAsync(idCliente) : null;
    var vendedor = cot.IdTerceroInterno is int idVendedor ? await lector.ObtenerTerceroAsync(idVendedor) : null;
    var direccion = cot.IdDireccionExterno is int idDireccion ? await lector.ObtenerDireccionPorIdAsync(idDireccion) : null;
    var formaDePago = cot.IdFormaDePago is int idFormaDePagoCot ? await lector.ObtenerFormaDePagoAsync(idFormaDePagoCot) : null;
    var renglonesCot = await lector.ObtenerRenglonesAsync(cot.IdAsientoContable);

    return new DocumentoVinculadoInputDto(
        PrefijoOrigen: cot.Prefijo,
        NumeroReferencia: cot.DocumentoNumero.ToString("0"),
        Fecha: cot.Fecha,
        // World Office no guarda la fecha de vencimiento en ninguna columna — se calcula
        // (confirmado empíricamente contra un caso real: Fecha + 45 días = fecha real
        // impresa en la Hoja de Alistamiento).
        FechaVencimiento: cot.Fecha.AddDays(45),
        Cliente: cliente?.NombreCompleto,
        NitCliente: cliente?.Identificacion,
        Direccion: direccion?.Direccion,
        Ciudad: direccion?.Ciudad,
        Telefono: direccion?.Telefonos,
        ElaboradoPor: vendedor?.NombreCompleto,
        FormaDePago: formaDePago,
        CampoEntrega: cot.Personalizado(campoEntregaEnCotizacion),
        Renglones: renglonesCot.Select(r => new RenglonDocumentoVinculadoInputDto(
            Codigo: r.Codigo,
            Descripcion: r.Descripcion ?? "(sin descripción)",
            Cantidad: r.Cantidad,
            UnidadMedida: r.UnidadMedida,
            ValorUnitario: r.ValorUnitario,
            PorcentajeIva: r.PorcentajeIva ?? 0m)).ToList());
}

// Configuración de CE de la página (2026-11-11). null = no se pudo leer → no se crea ningún CE.
async Task<ConfiguracionCuentasPorPagarDto?> ObtenerConfiguracionCEAsync()
{
    try
    {
        return await jimaco.ObtenerConfiguracionCEAsync();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTime.Now:s}] ERROR leyendo la configuración de CE de la Api (no se crean CE en esta corrida): {ex.Message}");
        return null;
    }
}
