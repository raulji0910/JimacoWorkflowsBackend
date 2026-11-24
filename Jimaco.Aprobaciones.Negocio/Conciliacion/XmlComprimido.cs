using System.IO.Compression;
using System.Text;

namespace Jimaco.Aprobaciones.Negocio.Conciliacion;

// El XML de cada factura se guarda comprimido (gzip, UTF-8): Jimaco recibe unas 600 facturas al mes
// y un AttachedDocument pesa decenas de KB (en nvarchar(max) se duplica por UTF-16). Comprimido
// ocupa ~10 veces menos y se descomprime solo para descargarlo o (fase 2) generar la FCJ.
public static class XmlComprimido
{
    public static byte[] Comprimir(string xml)
    {
        using var salida = new MemoryStream();
        using (var gzip = new GZipStream(salida, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(xml);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return salida.ToArray();
    }

    public static string Descomprimir(byte[] gzip)
    {
        using var entrada = new MemoryStream(gzip);
        using var descompresor = new GZipStream(entrada, CompressionMode.Decompress);
        using var lector = new StreamReader(descompresor, Encoding.UTF8);
        return lector.ReadToEnd();
    }
}
