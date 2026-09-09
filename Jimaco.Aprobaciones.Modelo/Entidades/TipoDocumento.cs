using System.ComponentModel.DataAnnotations;

namespace Jimaco.Aprobaciones.Modelo.Entidades;

public enum TipoCampo
{
    Texto = 1,
    Numero = 2,
    Fecha = 3,
    Adjunto = 4,
    Seleccion = 5
}

/// <summary>
/// Tipo de documento parametrizable (ej. "Orden de Compra"). Define qué campos adicionales
/// se capturan para ese tipo (aparte de los campos fijos de <see cref="InstanciaDocumento"/>)
/// y a qué <see cref="DefinicionFlujo"/> queda atado.
/// </summary>
public class TipoDocumento
{
    public int Id { get; set; }

    [MaxLength(150)]
    public required string Nombre { get; set; }

    [MaxLength(300)]
    public string? Descripcion { get; set; }

    /// <summary>
    /// El <c>prefijo</c> que usa World Office para este tipo de documento en
    /// <c>[CuentasContables - Asientos]</c> (ej. "OC") — obligatorio, porque dos tipos de
    /// documento con prefijos distintos son documentos distintos aunque se llamen parecido (ej.
    /// una OC real de prefijo "OC" no es lo mismo que una OC de pruebas con su propio prefijo).
    /// El Sincronizador usa este campo para saber qué prefijo de WO corresponde a qué
    /// TipoDocumento acá — no hay ningún prefijo hardcodeado en el código del Sincronizador.
    /// </summary>
    [MaxLength(20)]
    public required string PrefijoWorldOffice { get; set; }

    public bool Activo { get; set; } = true;

    public ICollection<CampoTipoDocumento> Campos { get; set; } = [];
    public ICollection<DefinicionFlujo> DefinicionesFlujo { get; set; } = [];
}

/// <summary>Campo dinámico definido para un tipo de documento (más allá de los campos fijos de la instancia).</summary>
public class CampoTipoDocumento
{
    public int Id { get; set; }

    public int TipoDocumentoId { get; set; }
    public TipoDocumento TipoDocumento { get; set; } = null!;

    /// <summary>Clave técnica del campo (usada como key en el JSON de datos), ej. "centroCosto".</summary>
    [MaxLength(100)]
    public required string Nombre { get; set; }

    /// <summary>Texto mostrado al usuario, ej. "Centro de costo".</summary>
    [MaxLength(200)]
    public required string Etiqueta { get; set; }

    public TipoCampo TipoCampo { get; set; }

    public bool Requerido { get; set; }

    public int Orden { get; set; }

    /// <summary>Solo para <see cref="TipoCampo.Seleccion"/>: opciones serializadas como array JSON de strings.</summary>
    public string? OpcionesJson { get; set; }
}
