using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

/// <summary>
/// Un documento externo de World Office ligado a la instancia (ej. la Cotización que originó una
/// Orden de Compra, vía <c>NúmDocumentoExterno</c> — puede haber más de una, separadas por guión en
/// WO). A diferencia de <see cref="RenglonInstanciaDocumento"/>, esto no es un campo del documento
/// principal: es OTRO documento completo, con su propia cabecera y renglones, que se adjunta a la
/// vista previa en el mismo formato con el que se imprime en World Office (la "Hoja de
/// Alistamiento" para Cotizaciones).
///
/// Se guarda una copia de los datos al momento de sincronizar (no se consulta World Office de
/// nuevo al generar el PDF) porque la Api no tiene acceso a la base de WO — solo el Sincronizador,
/// que corre en la red de la ferretería, puede leerla.
/// </summary>
public class DocumentoVinculado
{
    public int Id { get; set; }

    /// <summary>Null cuando esta Hoja no está ligada a ninguna OC de nuestro flujo de aprobación (2026-10-21) — la Cotización se detectó directo en World Office, sin pasar por una Orden de Compra. Sigue apareciendo en el dashboard de Alistamiento igual que las demás, pero el reloj de antigüedad usa <see cref="FechaCreacion"/> en vez de la fecha de aprobación del primer paso (no existe ese evento acá).</summary>
    public int? InstanciaDocumentoId { get; set; }
    public InstanciaDocumento? InstanciaDocumento { get; set; }

    /// <summary>Prefijo real en WO del documento ligado (ej. "COT") — igual que <see cref="InstanciaDocumento.PrefijoOrigen"/>, para saber qué plantilla de impresión usar si en el futuro se agregan otros tipos de documento ligado.</summary>
    [MaxLength(20)]
    public required string PrefijoOrigen { get; set; }

    [MaxLength(50)]
    public required string NumeroReferencia { get; set; }

    public DateTime Fecha { get; set; }

    /// <summary>Calculada (Fecha + 45 días) — World Office no la guarda en ninguna columna, ver esquema-worldoffice-oc.md.</summary>
    public DateTime FechaVencimiento { get; set; }

    [MaxLength(200)]
    public string? Cliente { get; set; }

    [MaxLength(30)]
    public string? NitCliente { get; set; }

    [MaxLength(200)]
    public string? Direccion { get; set; }

    [MaxLength(100)]
    public string? Ciudad { get; set; }

    [MaxLength(50)]
    public string? Telefono { get; set; }

    [MaxLength(200)]
    public string? ElaboradoPor { get; set; }

    [MaxLength(100)]
    public string? FormaDePago { get; set; }

    /// <summary>"CAMPO ENTREGA - CIUDAD - DIRECCION" en la Hoja de Alistamiento real — sale de <c>Personalizado2</c> en WO.</summary>
    [MaxLength(300)]
    public string? CampoEntrega { get; set; }

    // ---- Control de alistamiento (2026-09-28) — pedido real: Transporte necesita ir registrando
    // cuánto tiene disponible de cada ítem, con un dashboard de completitud/antigüedad para
    // Transporte y Gerencia Comercial. Ver Rol.PuedeGestionarAlistamiento/PuedeVerAlistamiento y
    // HojaAlistamientoAlertaService. Por ahora NO contempla qué pasa si la OC se devuelve y esta
    // hoja se reemplaza al reenviarse (sigue reemplazándose entera, ver InstanciaDocumentoService.
    // CrearAsync) — decisión explícita del usuario, se evalúa más adelante con datos reales. ----

    /// <summary>Momento en que esta hoja entró al sistema (no la <see cref="Fecha"/> del documento en WO) — es el reloj de la alerta de atraso.</summary>
    public DateTime FechaCreacion { get; set; }

    /// <summary>Null mientras nunca se mandó un aviso de atraso para esta hoja — una vez se pone, el chequeo periódico de alertas de alistamiento ya no vuelve a avisar (2026-10-14, solo una vez por hoja).</summary>
    public DateTime? UltimaAlertaEnviada { get; set; }

    // ---- Remisionado (2026-10-09, llevado a nivel de renglón el 2026-10-26) — pedido real: la
    // Cotización puede despacharse por World Office (Remisión, prefijo "REM") sin que eso tenga
    // ninguna relación con cuánto marcó Transporte como disponible acá — son dos cosas
    // independientes que se muestran combinadas (ej. "Atrasada" + "Remisionada" a la vez). El cruce
    // lo detecta el Sincronizador, revisando periódicamente el campo libre "COTIZACION"
    // (Personalizado4 en los documentos REM, texto libre escrito a mano — puede traer prefijos como
    // "COT" o varios números separados por guión) de las Remisiones reales. Descubierto con datos
    // reales (Hoja 16099/16228): un mismo ítem puede llegar repartido en VARIAS Remisiones
    // parciales distintas — por eso esto ya no es un flag a nivel de documento, se mueve a
    // <see cref="RenglonDocumentoVinculado.DespachosRemision"/> (uno por cada Remisión que aportó
    // cantidad a ese renglón puntual), cruzando por Código contra los renglones reales de la
    // Remisión (ver WorldOfficeReader.ObtenerRenglonesAsync, mismo mecanismo que Cotización/OC).

    /// <summary>
    /// True apenas se avisó (una sola vez) que esta hoja llegó a estar Completa Y Remisionada (TODOS
    /// los renglones al 100%) a la vez (2026-10-09) — evita mandar el aviso de nuevo si, por
    /// ejemplo, alguien corrige una cantidad después. Se replica en todas las copias del grupo.
    /// </summary>
    public bool AvisoCompletaRemisionadaEnviado { get; set; }

    public ICollection<RenglonDocumentoVinculado> Renglones { get; set; } = [];
}

/// <summary>Un renglón del documento vinculado (ej. un ítem de la Cotización) — misma forma que <see cref="RenglonInstanciaDocumento"/>, entidad separada porque pertenece a otro documento.</summary>
public class RenglonDocumentoVinculado
{
    public int Id { get; set; }

    public int DocumentoVinculadoId { get; set; }
    public DocumentoVinculado DocumentoVinculado { get; set; } = null!;

    public int Orden { get; set; }

    [MaxLength(50)]
    public string? Codigo { get; set; }

    [MaxLength(300)]
    public required string Descripcion { get; set; }

    public decimal Cantidad { get; set; }

    [MaxLength(20)]
    public string? UnidadMedida { get; set; }

    public decimal ValorUnitario { get; set; }

    /// <summary>Ej. 0.19 para 19%.</summary>
    public decimal PorcentajeIva { get; set; }

    public decimal Total { get; set; }

    /// <summary>Cuánto tiene Transporte disponible de este ítem hoy — se va actualizando manualmente, no viene de World Office. La hoja se considera completa cuando esto alcanza <see cref="Cantidad"/> en TODOS sus renglones.</summary>
    public decimal CantidadDisponible { get; set; }

    /// <summary>Remisiones reales de World Office que aportaron cantidad a este renglón (2026-10-26) — puede haber más de una si el ítem se despachó en partes. Este renglón se considera remisionado cuando la suma de <see cref="RemisionRenglon.CantidadRemisionada"/> alcanza <see cref="Cantidad"/>.</summary>
    public ICollection<RemisionRenglon> DespachosRemision { get; set; } = [];
}

/// <summary>Un despacho real: cuánto de un renglón concreto llegó en una Remisión concreta de World Office (2026-10-26) — un mismo renglón puede tener varias filas acá si se despachó en partes. Detectado por el Sincronizador cruzando por Código contra los renglones reales de la Remisión.</summary>
public class RemisionRenglon
{
    public int Id { get; set; }

    public int RenglonDocumentoVinculadoId { get; set; }
    public RenglonDocumentoVinculado RenglonDocumentoVinculado { get; set; } = null!;

    [MaxLength(50)]
    public required string NumeroRemision { get; set; }

    public DateTime Fecha { get; set; }

    public decimal CantidadRemisionada { get; set; }
}
