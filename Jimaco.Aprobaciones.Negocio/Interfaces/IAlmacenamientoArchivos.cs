namespace Jimaco.Aprobaciones.Negocio.Interfaces;

/// <summary>Abstrae dónde viven físicamente los adjuntos, para no acoplar el motor de workflow al disco local.</summary>
public interface IAlmacenamientoArchivos
{
    /// <summary>Guarda el contenido y devuelve la ruta relativa a usar como <c>Adjunto.RutaArchivo</c>.</summary>
    Task<string> GuardarAsync(string nombreArchivoSugerido, Stream contenido, CancellationToken ct = default);

    Task<Stream> AbrirAsync(string rutaArchivo, CancellationToken ct = default);
}
