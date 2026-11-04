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
            DestinosDevolucionDisponibles: [],
            PasoActualRequiereComprobanteEgreso: false,
            PasoActualRequiereComentarioAlAprobar: false,
            PasoActualPermiteRedirigirAPagoContado: false,
            PasoActualRequiereSoportePagoAlAprobar: false,
            CreadoPorNombre: "Administrador",
            FechaCreacion: new DateTime(2026, 9, 3, 10, 0, 0),
            Adjuntos: [],
            Historial: [new HistorialAccionDto(1, null, "Administrador", TipoAccion.Creado, null, new DateTime(2026, 9, 3, 10, 0, 0))],
            Renglones: [new RenglonDto(1, "PJ-27671", "SOPLADORA INALAMBRICA 20V", 2, "Und.", 242100m, 0.19m, 484200m)],
            IdAsientoContableOrigen: null,
            PrefijoOrigen: null,
            PendienteEscrituraWO: false,
            ConflictoWO: null,
            FechaEscrituraWO: null,
            DocumentosVinculados: [],
            SolicitanteWO: null,
            NumeroComprobanteEgresoSolicitado: null,
            PendienteBusquedaComprobanteEgresoWO: false,
            ConflictoComprobanteEgresoWO: null,
            ComprobanteEgreso: null,
            SoportePago: null);

        var pdf = new DocumentoPdfGenerador().Generar(documento);

        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Generar_ConDocumentoVinculado_AgregaHojaDeAlistamiento()
    {
        // Datos reales de HojaAlistamiento-15864.pdf (ver WorkflowDocumentos/), para verificar
        // que el generador no falla con un documento vinculado real.
        var vinculado = new DocumentoVinculadoDto(
            Id: 1,
            PrefijoOrigen: "COT",
            NumeroReferencia: "15864",
            Fecha: new DateTime(2026, 9, 11),
            FechaVencimiento: new DateTime(2026, 10, 26),
            Cliente: "VENTUS COLOMBIA S.A.S.",
            NitCliente: "901115366",
            Direccion: "CRA 9 115 06 P 17",
            Ciudad: "Bogota D.C.",
            Telefono: "6016398498",
            ElaboradoPor: "LAURA SOFIA HERNANDEZ CARO",
            FormaDePago: "Credito",
            CampoEntrega: "O&M QUIFA/ 2016350",
            FechaCreacion: new DateTime(2026, 9, 11),
            Completa: false,
            Renglones:
            [
                new RenglonDocumentoVinculadoDto(1, "PJ-20523", "BOTA DE SEGURIDAD DIELECTRICA 20.000 VOLT FRONT DE RELAMPAGO DE ARCO SALISBURY HONEYWELL 52000", 1, "Und.", 1248000m, 0.19m, 1248000m, 0),
                new RenglonDocumentoVinculadoDto(2, "PJ-27983", "CIZALLA AISLADA DIELECTRICA 36\" FIXTOOLS", 1, "Und.", 1547000m, 0.19m, 1547000m, 0)
            ]);

        var documento = new InstanciaDocumentoDetalleDto(
            Id: 3,
            TipoDocumentoNombre: "Orden de Compra",
            NumeroReferencia: "19150",
            Proveedor: "PERDOMO BUSINESS GROUP S.A.S.",
            Valor: 100000m,
            FechaDocumento: new DateTime(2026, 9, 14),
            Datos: null,
            Estado: EstadoInstanciaDocumento.EnProceso,
            PasoActualId: 1,
            PasoActualNombre: "Aprobación comercial",
            PasoActualPermiteDevolver: true,
            PasoActualPermiteRechazar: false,
            DestinosDevolucionDisponibles: [],
            PasoActualRequiereComprobanteEgreso: false,
            PasoActualRequiereComentarioAlAprobar: false,
            PasoActualPermiteRedirigirAPagoContado: false,
            PasoActualRequiereSoportePagoAlAprobar: false,
            CreadoPorNombre: "Sincronizador (servicio)",
            FechaCreacion: new DateTime(2026, 9, 14),
            Adjuntos: [],
            Historial: [],
            Renglones: [new RenglonDto(1, "PJ-1", "PRODUCTO", 1, "Und.", 100000m, 0.19m, 100000m)],
            IdAsientoContableOrigen: 123456,
            PrefijoOrigen: "OC",
            PendienteEscrituraWO: false,
            ConflictoWO: null,
            FechaEscrituraWO: null,
            DocumentosVinculados: [vinculado],
            SolicitanteWO: "YAZMIN RENDON",
            NumeroComprobanteEgresoSolicitado: null,
            PendienteBusquedaComprobanteEgresoWO: false,
            ConflictoComprobanteEgresoWO: null,
            ComprobanteEgreso: null,
            SoportePago: null);

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
            DestinosDevolucionDisponibles: [],
            PasoActualRequiereComprobanteEgreso: false,
            PasoActualRequiereComentarioAlAprobar: false,
            PasoActualPermiteRedirigirAPagoContado: false,
            PasoActualRequiereSoportePagoAlAprobar: false,
            CreadoPorNombre: "Administrador",
            FechaCreacion: new DateTime(2026, 9, 3),
            Adjuntos: [],
            Historial: [],
            Renglones: [],
            IdAsientoContableOrigen: null,
            PrefijoOrigen: null,
            PendienteEscrituraWO: false,
            ConflictoWO: null,
            FechaEscrituraWO: null,
            DocumentosVinculados: [],
            SolicitanteWO: null,
            NumeroComprobanteEgresoSolicitado: null,
            PendienteBusquedaComprobanteEgresoWO: false,
            ConflictoComprobanteEgresoWO: null,
            ComprobanteEgreso: null,
            SoportePago: null);

        var pdf = new DocumentoPdfGenerador().Generar(documento);

        Assert.NotEmpty(pdf);
    }
}
