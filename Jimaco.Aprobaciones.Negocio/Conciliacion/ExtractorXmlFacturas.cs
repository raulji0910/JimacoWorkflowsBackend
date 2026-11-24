using System.IO.Compression;
using System.Text;
using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Conciliacion;

// Saca los XML de factura de lo que llega: adjuntos del correo (Sincronizador), archivos de una
// carpeta (Sincronizador --carpeta) o ZIP que se suben desde la pantalla (descargados de la DIAN).
// Acepta XML sueltos y ZIP, incluso ZIP dentro de ZIP (el "Descargar todo" de Outlook o un ZIP
// armado a mano con los ZIP de la DIAN). Los PDF y demás se ignoran.
//
// Como los archivos ahora también los sube un usuario desde la web, hay topes contra ZIP malformados
// o "bombas" (un ZIP pequeño que descomprimido pesa gigas): profundidad de anidamiento, tamaño por
// archivo y total descomprimido.
public static class ExtractorXmlFacturas
{
    public const int ProfundidadMaxima = 3;
    public const long TamanoMaximoArchivo = 20L * 1024 * 1024;
    public const long TamanoMaximoTotal = 300L * 1024 * 1024;

    public static bool EsArchivoSoportado(string nombre)
        => nombre.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || nombre.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Agrega a <paramref name="xmls"/> los XML que encuentre. Los problemas (ZIP dañado,
    /// archivo demasiado grande) van a <paramref name="avisos"/> y no detienen el resto.</summary>
    public static void Extraer(string nombre, Stream contenido, string? asunto, DateTime? fecha,
        List<FacturaXmlEntradaDto> xmls, List<string> avisos)
    {
        long total = 0;
        Extraer(nombre, contenido, asunto, fecha, xmls, avisos, 0, ref total);
    }

    private static void Extraer(string nombre, Stream contenido, string? asunto, DateTime? fecha,
        List<FacturaXmlEntradaDto> xmls, List<string> avisos, int profundidad, ref long total)
    {
        if (nombre.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            if (profundidad >= ProfundidadMaxima)
            {
                avisos.Add($"{nombre}: demasiados ZIP uno dentro de otro, se omite.");
                return;
            }

            try
            {
                using var zip = new ZipArchive(contenido, ZipArchiveMode.Read);
                foreach (var entrada in zip.Entries.Where(e => EsArchivoSoportado(e.Name)))
                {
                    if (entrada.Length > TamanoMaximoArchivo || total + entrada.Length > TamanoMaximoTotal)
                    {
                        avisos.Add($"{entrada.Name} (dentro de {nombre}): demasiado grande, se omite.");
                        continue;
                    }

                    using var interno = new MemoryStream();
                    bool cupo;
                    using (var origen = entrada.Open())
                        cupo = CopiarConTope(origen, interno, TamanoMaximoArchivo);
                    if (!cupo)
                    {
                        avisos.Add($"{entrada.Name} (dentro de {nombre}): demasiado grande, se omite.");
                        continue;
                    }
                    total += interno.Length;
                    interno.Position = 0;
                    Extraer(entrada.Name, interno, asunto, fecha, xmls, avisos, profundidad + 1, ref total);
                }
            }
            // Un ZIP truncado o que no es ZIP no siempre da InvalidDataException: según el stream
            // también sale ArgumentOutOfRangeException o IOException. Cualquiera, se omite ese archivo.
            catch (Exception e) when (e is InvalidDataException or ArgumentException or IOException or NotSupportedException)
            {
                avisos.Add($"{nombre}: no es un ZIP válido, se omite.");
            }
        }
        else if (nombre.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            using var lector = new StreamReader(contenido, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            xmls.Add(new FacturaXmlEntradaDto(lector.ReadToEnd(), nombre, asunto, fecha));
        }
    }

    // El tamaño declarado en el ZIP puede mentir: se corta al copiar, no solo al leer el encabezado.
    private static bool CopiarConTope(Stream origen, Stream destino, long tope)
    {
        var buffer = new byte[81920];
        long copiado = 0;
        int leidos;
        while ((leidos = origen.Read(buffer, 0, buffer.Length)) > 0)
        {
            copiado += leidos;
            if (copiado > tope)
                return false;
            destino.Write(buffer, 0, leidos);
        }
        return true;
    }
}
