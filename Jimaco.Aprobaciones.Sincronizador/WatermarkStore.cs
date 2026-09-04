using System.Text.Json;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Guarda el último <c>IdAsientoContable</c> ya procesado en un archivo JSON local, para no
/// reprocesar la misma OC en la corrida siguiente. Un archivo alcanza porque el Sincronizador
/// corre como una sola instancia (una Tarea Programada de Windows, no varias en paralelo) — si
/// eso cambia algún día, esto tendría que migrar a algo con control de concurrencia real.
/// </summary>
public class WatermarkStore(string rutaArchivo)
{
    private record Contenido(int UltimoIdProcesado);

    public int Leer()
    {
        if (!File.Exists(rutaArchivo))
            return 0;

        var json = File.ReadAllText(rutaArchivo);
        var contenido = JsonSerializer.Deserialize<Contenido>(json);
        return contenido?.UltimoIdProcesado ?? 0;
    }

    public void Guardar(int ultimoIdProcesado)
    {
        var json = JsonSerializer.Serialize(new Contenido(ultimoIdProcesado));
        File.WriteAllText(rutaArchivo, json);
    }
}
