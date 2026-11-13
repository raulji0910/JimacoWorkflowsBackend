using System.Text.Json;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class TipoDocumentoService(AppDbContext db) : ITipoDocumentoService
{
    public async Task<IReadOnlyList<TipoDocumentoDto>> ListarAsync(CancellationToken ct = default, Empresa? empresa = null)
    {
        // empresa = null → todos (la página). Cada Sincronizador pide SOLO los de su empresa: dos
        // tipos pueden compartir el prefijo "OC" siempre que sean de empresas distintas.
        var tipos = await db.TiposDocumento.Include(t => t.Campos)
            .Where(t => empresa == null || t.Empresa == empresa)
            .OrderBy(t => t.Nombre).ToListAsync(ct);
        return tipos.Select(MapearDto).ToList();
    }

    public async Task<TipoDocumentoDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var tipo = await db.TiposDocumento.Include(t => t.Campos).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("Tipo de documento no encontrado.");
        return MapearDto(tipo);
    }

    public async Task<TipoDocumentoDto> CrearAsync(CrearTipoDocumentoDto dto, CancellationToken ct = default)
    {
        if (await db.TiposDocumento.AnyAsync(t => t.Nombre == dto.Nombre, ct))
            throw new InvalidOperationException($"Ya existe un tipo de documento llamado \"{dto.Nombre}\".");

        // Dos tipos de documento con el mismo prefijo de WO serían ambiguos para el
        // Sincronizador — no sabría a cuál de los dos crear el documento sincronizado.
        if (await db.TiposDocumento.AnyAsync(t => t.PrefijoWorldOffice == dto.PrefijoWorldOffice && t.Empresa == dto.Empresa, ct))
            throw new InvalidOperationException($"Ya hay un tipo de documento de {dto.Empresa.Nombre()} usando el prefijo de World Office \"{dto.PrefijoWorldOffice}\".");

        var tipo = new TipoDocumento
        {
            Nombre = dto.Nombre,
            Descripcion = dto.Descripcion,
            PrefijoWorldOffice = dto.PrefijoWorldOffice,
            Empresa = dto.Empresa,
            Activo = true,
            Campos = dto.Campos.Select(MapearCampo).ToList()
        };

        db.TiposDocumento.Add(tipo);
        await db.SaveChangesAsync(ct);

        return MapearDto(tipo);
    }

    public async Task<TipoDocumentoDto> ActualizarAsync(int id, ActualizarTipoDocumentoDto dto, CancellationToken ct = default)
    {
        var tipo = await db.TiposDocumento.Include(t => t.Campos).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new KeyNotFoundException("Tipo de documento no encontrado.");

        if (await db.TiposDocumento.AnyAsync(t => t.Id != id && t.Nombre == dto.Nombre, ct))
            throw new InvalidOperationException($"Ya existe un tipo de documento llamado \"{dto.Nombre}\".");

        if (await db.TiposDocumento.AnyAsync(t => t.Id != id && t.PrefijoWorldOffice == dto.PrefijoWorldOffice && t.Empresa == dto.Empresa, ct))
            throw new InvalidOperationException($"Ya hay un tipo de documento de {dto.Empresa.Nombre()} usando el prefijo de World Office \"{dto.PrefijoWorldOffice}\".");

        // La empresa no se cambia si ya hay documentos de este tipo: cada uno guardó la suya al crearse.
        if (tipo.Empresa != dto.Empresa && await db.InstanciasDocumento.AnyAsync(i => i.TipoDocumentoId == id, ct))
            throw new InvalidOperationException("No se puede cambiar la empresa de un tipo de documento que ya tiene documentos.");
        tipo.Empresa = dto.Empresa;

        tipo.Nombre = dto.Nombre;
        tipo.Descripcion = dto.Descripcion;
        tipo.PrefijoWorldOffice = dto.PrefijoWorldOffice;
        tipo.Activo = dto.Activo;

        db.CamposTipoDocumento.RemoveRange(tipo.Campos);
        tipo.Campos = dto.Campos.Select(MapearCampo).ToList();

        await db.SaveChangesAsync(ct);

        return MapearDto(tipo);
    }

    private static CampoTipoDocumento MapearCampo(CampoTipoDocumentoInputDto c) => new()
    {
        Nombre = c.Nombre,
        Etiqueta = c.Etiqueta,
        TipoCampo = c.TipoCampo,
        Requerido = c.Requerido,
        Orden = c.Orden,
        OpcionesJson = c.Opciones is { Count: > 0 } ? JsonSerializer.Serialize(c.Opciones) : null
    };

    private static TipoDocumentoDto MapearDto(TipoDocumento t) => new(
        t.Id, t.Nombre, t.Descripcion, t.PrefijoWorldOffice, t.Activo,
        t.Campos.OrderBy(c => c.Orden).Select(c => new CampoTipoDocumentoDto(
            c.Id, c.Nombre, c.Etiqueta, c.TipoCampo, c.Requerido, c.Orden,
            c.OpcionesJson is null ? null : JsonSerializer.Deserialize<List<string>>(c.OpcionesJson))).ToList(),
        t.Empresa);
}
