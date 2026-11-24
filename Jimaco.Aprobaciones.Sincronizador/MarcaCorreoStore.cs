using System.Text.Json;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Guarda hasta qué correo (UID IMAP) ya se procesó la bandeja de facturación, en un JSON local.
/// El UID solo vale mientras la carpeta conserve su UIDVALIDITY, por eso se guardan los dos.
/// Un archivo alcanza porque el Sincronizador corre como una sola instancia (Tarea Programada).
/// </summary>
public class MarcaCorreoStore(string rutaArchivo)
{
    public record Marca(uint UidValidity, uint UltimoUid);

    public Marca? Leer()
    {
        if (!File.Exists(rutaArchivo))
            return null;

        return JsonSerializer.Deserialize<Marca>(File.ReadAllText(rutaArchivo));
    }

    public void Guardar(Marca marca)
        => File.WriteAllText(rutaArchivo, JsonSerializer.Serialize(marca));
}
