using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

public class DocumentoPdfGeneradorTests
{
    [Fact]
    public void Generar_ConRenglonesYHistorial_ProduceUnPdfValido()
    {
        var documento = new InstanciaDocumentoDetalleDto(
            Id: 1,
            TipoDocumentoNombre: "Orden de Compra",
            NumeroReferencia: "19022",
            Proveedor: "FERRETERIA LUIS PENAGOS SAS",
            Valor: 484200m,
            FechaDocumento: new DateTime(2026, 9, 3),
            Datos: new Dictionary<string, string> { ["cotizacionOrigen"] = "15736" },
            Estado: EstadoInstanciaDocumento.EnProceso,
            PasoActualId: 1,
            PasoActualNombre: "Aprobación comercial",
            PasoActualPermiteDevolver: true,
            PasoActualPermiteRechazar: false,
            CreadoPorNombre: "Administrador",
            FechaCreacion: new DateTime(2026, 9, 3, 10, 0, 0),
            Adjuntos: [],
            Historial: [new HistorialAccionDto(1, null, "Administrador", TipoAccion.Creado, null, new DateTime(2026, 9, 3, 10, 0, 0))],
            Renglones: [new RenglonDto(1, "PJ-27671", "SOPLADORA INALAMBRICA 20V", 2, "Und.", 242100m, 0.19m, 484200m)]);

        var pdf = new DocumentoPdfGenerador().Generar(documento);

        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Generar_SinRenglonesNiDatos_NoFalla()
    {
        var documento = new InstanciaDocumentoDetalleDto(
            Id: 2,
            TipoDocumentoNombre: "Orden de Compra",
            NumeroReferencia: "19023",
            Proveedor: null,
            Valor: null,
            FechaDocumento: null,
            Datos: null,
            Estado: EstadoInstanciaDocumento.Completado,
            PasoActualId: null,
            PasoActualNombre: null,
            PasoActualPermiteDevolver: false,
            PasoActualPermiteRechazar: false,
            CreadoPorNombre: "Administrador",
            FechaCreacion: new DateTime(2026, 9, 3),
            Adjuntos: [],
            Historial: [],
            Renglones: []);

        var pdf = new DocumentoPdfGenerador().Generar(documento);

        Assert.NotEmpty(pdf);
    }
}
