using System.IO.Compression;
using System.Text;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

// Botón "Cargar ZIP de la DIAN": varios ZIP a la vez, ZIP dentro de ZIP, duplicados con lo que
// llegó por correo y facturas que no son para Jimaco.
public class CargaArchivosDianTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 9);

    private static AppDbContext CrearContexto() => ConciliacionTestsDatos.CrearContexto();

    private static ConciliacionService CrearServicio(AppDbContext db)
        => new(db, new TiempoFijo(Hoy.ToDateTime(TimeOnly.MinValue)));

    private static string Factura(string numero, string receptor = "900632353")
    {
        var xml = LectorFacturaElectronicaTests.AttachedDocument(LectorFacturaElectronicaTests.Invoice(numero: numero));
        return receptor == "900632353" ? xml : xml.Replace(">900632353<", $">{receptor}<");
    }

    // Lo que baja la DIAN por factura: un ZIP con el XML y el PDF.
    private static byte[] Zip(params (string Nombre, byte[] Contenido)[] archivos)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (nombre, contenido) in archivos)
            {
                using var e = zip.CreateEntry(nombre).Open();
                e.Write(contenido);
            }
        }
        return ms.ToArray();
    }

    private static (string, byte[]) Xml(string nombre, string contenido) => (nombre, Encoding.UTF8.GetBytes(contenido));

    private static ArchivoCargado Archivo(string nombre, byte[] contenido) => new(nombre, new MemoryStream(contenido));

    [Fact]
    public async Task VariosZip_RegistraTodasLasFacturasComoDian()
    {
        using var db = CrearContexto();

        var resultado = await CrearServicio(db).CargarArchivosAsync([
            Archivo("z1.zip", Zip(Xml("FE1.xml", Factura("FE1")), ("FE1.pdf", [1, 2, 3]))),
            Archivo("z2.zip", Zip(Xml("FE2.xml", Factura("FE2")))),
            Archivo("FE3.xml", Encoding.UTF8.GetBytes(Factura("FE3"))),
        ], ConciliacionTestsDatos.Ana);

        Assert.Equal(3, resultado.Archivos);
        Assert.Equal(3, resultado.Nuevas);
        Assert.Empty(resultado.Avisos);
        Assert.All(await db.FacturasProveedor.ToListAsync(), f => Assert.Equal(OrigenFactura.Dian, f.Origen));
    }

    [Fact]
    public async Task ZipDentroDeZip_LeeLosXmlDeAdentro()
    {
        using var db = CrearContexto();
        var interno = Zip(Xml("FE10.xml", Factura("FE10")));

        var resultado = await CrearServicio(db).CargarArchivosAsync([
            Archivo("todo.zip", Zip(("FE10.zip", interno), Xml("FE11.xml", Factura("FE11")))),
        ], ConciliacionTestsDatos.Ana);

        Assert.Equal(2, resultado.Nuevas);
    }

    [Fact]
    public async Task FacturaQueYaLlegoPorCorreo_EsDuplicadaYConservaElOrigen()
    {
        using var db = CrearContexto();
        var servicio = CrearServicio(db);
        await servicio.RegistrarFacturasAsync([new FacturaXmlEntradaDto(Factura("FE20"), "FE20.xml", "Factura FE20", null)]);

        var resultado = await servicio.CargarArchivosAsync([Archivo("FE20.zip", Zip(Xml("FE20.xml", Factura("FE20"))))], ConciliacionTestsDatos.Ana);

        var renglon = Assert.Single(resultado.Facturas);
        Assert.Equal(EstadoCarga.Duplicada, renglon.Estado);
        Assert.Equal(OrigenFactura.Correo, renglon.OrigenExistente);
        Assert.Equal(OrigenFactura.Correo, (await db.FacturasProveedor.SingleAsync()).Origen);
    }

    [Fact]
    public async Task MismaFacturaEnDosZipDeLaMismaCarga_SoloUna()
    {
        using var db = CrearContexto();

        var resultado = await CrearServicio(db).CargarArchivosAsync([
            Archivo("a.zip", Zip(Xml("FE30.xml", Factura("FE30")))),
            Archivo("b.zip", Zip(Xml("FE30.xml", Factura("FE30")))),
        ], ConciliacionTestsDatos.Ana);

        Assert.Equal(1, resultado.Nuevas);
        Assert.Equal(1, resultado.Duplicadas);
        Assert.Equal(1, await db.FacturasProveedor.CountAsync());
    }

    [Fact]
    public async Task FacturaParaOtraEmpresa_SeRechaza()
    {
        using var db = CrearContexto();

        var resultado = await CrearServicio(db).CargarArchivosAsync([
            Archivo("venta.zip", Zip(Xml("FV1.xml", Factura("FV1", receptor: "800123456")))),
        ], ConciliacionTestsDatos.Ana);

        Assert.Equal(1, resultado.OtroReceptor);
        Assert.Equal(0, await db.FacturasProveedor.CountAsync());
    }

    [Fact]
    public async Task ZipDanadoOArchivoNoSoportado_AvisaYSigueConLosDemas()
    {
        using var db = CrearContexto();

        var resultado = await CrearServicio(db).CargarArchivosAsync([
            Archivo("roto.zip", [1, 2, 3, 4]),
            // Caso real de la prueba en local: con este contenido el lector de ZIP no da
            // InvalidDataException sino ArgumentOutOfRangeException, y tumbaba toda la carga.
            Archivo("texto.zip", Encoding.UTF8.GetBytes("esto no es un zip")),
            Archivo("factura.pdf", [1, 2, 3]),
            Archivo("solo-pdf.zip", Zip(("FE40.pdf", [1, 2, 3]))),
            Archivo("bien.zip", Zip(Xml("FE41.xml", Factura("FE41")))),
        ], ConciliacionTestsDatos.Ana);

        Assert.Equal(1, resultado.Nuevas);
        Assert.Contains(resultado.Avisos, a => a.StartsWith("roto.zip") && a.Contains("no es un ZIP válido"));
        Assert.Contains(resultado.Avisos, a => a.StartsWith("texto.zip") && a.Contains("no es un ZIP válido"));
        Assert.Contains(resultado.Avisos, a => a.StartsWith("factura.pdf") && a.Contains("solo se aceptan"));
        Assert.Contains(resultado.Avisos, a => a.StartsWith("solo-pdf.zip") && a.Contains("no trae ningún XML"));
    }

    [Fact]
    public async Task XmlQueNoEsFactura_NoReconocida()
    {
        using var db = CrearContexto();

        var resultado = await CrearServicio(db).CargarArchivosAsync([Archivo("x.zip", Zip(Xml("x.xml", "<Otro/>")))], ConciliacionTestsDatos.Ana);

        Assert.Equal(1, resultado.NoReconocidas);
    }

    [Fact]
    public async Task CargaPorCarpetaDelSincronizador_QuedaComoCarpeta()
    {
        using var db = CrearContexto();

        await CrearServicio(db).RegistrarFacturasAsync([
            new FacturaXmlEntradaDto(Factura("FE50"), "FE50.xml", "Carga desde carpeta: FE50.zip", null),
        ]);

        Assert.Equal(OrigenFactura.Carpeta, (await db.FacturasProveedor.SingleAsync()).Origen);
    }

    private sealed class TiempoFijo(DateTime ahora) : TimeProvider
    {
        private readonly DateTimeOffset _ahora = new(ahora, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
