using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

/// <summary>
/// Implementación simple: guarda los adjuntos en una carpeta local (montada como volumen Docker
/// en producción). Cada archivo se nombra con un GUID + su extensión original para evitar
/// colisiones y problemas de path traversal con el nombre original del usuario.
/// </summary>
public class AlmacenamientoArchivosDisco : IAlmacenamientoArchivos
{
    private readonly string _rutaBase;

    public AlmacenamientoArchivosDisco(IConfiguration configuration)
    {
        _rutaBase = configuration["Almacenamiento:RutaAdjuntos"] ?? "adjuntos";
        Directory.CreateDirectory(_rutaBase);
    }

    public async Task<string> GuardarAsync(string nombreArchivoSugerido, Stream contenido, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(nombreArchivoSugerido);
        var nombreFisico = $"{Guid.NewGuid():N}{extension}";
        var rutaCompleta = Path.Combine(_rutaBase, nombreFisico);

        await using var destino = File.Create(rutaCompleta);
        await contenido.CopyToAsync(destino, ct);

        return nombreFisico;
    }

    public Task<Stream> AbrirAsync(string rutaArchivo, CancellationToken ct = default)
    {
        var rutaCompleta = Path.Combine(_rutaBase, rutaArchivo);
        Stream stream = File.OpenRead(rutaCompleta);
        return Task.FromResult(stream);
    }
}
