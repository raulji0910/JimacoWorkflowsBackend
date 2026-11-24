using Jimaco.Aprobaciones.Negocio.DTOs;
using MimeKit;

namespace Jimaco.Aprobaciones.Sincronizador;

/// <summary>
/// Carga única de facturas bajadas a mano a una carpeta (p. ej. el histórico de
/// un buzón que no se puede leer por IMAP, que no se puede leer por IMAP con usuario y clave). Recorre la
/// carpeta y subcarpetas y acepta lo que deja bajar Outlook: ZIP de los proveedores, el ZIP de
/// "Descargar todo", XML sueltos y correos guardados (.eml). La API descarta duplicados por CUFE,
/// así que no importa si alguna ya había llegado a facturacion@.
/// </summary>
public static class CarpetaFacturasReader
{
    public static IReadOnlyList<FacturaXmlEntradaDto> Leer(string carpeta)
    {
        var xmls = new List<FacturaXmlEntradaDto>();

        foreach (var ruta in Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories).Order())
        {
            var nombre = Path.GetFileName(ruta);
            var fecha = File.GetLastWriteTimeUtc(ruta);
            var antes = xmls.Count;

            if (nombre.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    xmls.AddRange(CorreoFacturasWatcher.ExtraerXmls(MimeMessage.Load(ruta)));
                }
                catch (FormatException)
                {
                    Console.WriteLine($"[{DateTime.Now:s}]   {nombre} no es un correo válido, se omite.");
                }
            }
            else
            {
                using var contenido = File.OpenRead(ruta);
                CorreoFacturasWatcher.ExtraerDeArchivo(nombre, contenido, $"Carga desde carpeta: {nombre}", fecha, xmls);
            }

            if (xmls.Count == antes && !nombre.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[{DateTime.Now:s}]   {nombre}: sin XML, se omite.");
        }

        return xmls;
    }
}
