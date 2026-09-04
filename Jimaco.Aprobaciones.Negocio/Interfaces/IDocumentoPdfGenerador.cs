using Jimaco.Aprobaciones.Negocio.DTOs;

namespace Jimaco.Aprobaciones.Negocio.Interfaces;

public interface IDocumentoPdfGenerador
{
    byte[] Generar(InstanciaDocumentoDetalleDto documento);
}
