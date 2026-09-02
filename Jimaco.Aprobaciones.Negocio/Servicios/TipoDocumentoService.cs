using System.Text.Json;
using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class TipoDocumentoService(AppDbContext db) : ITipoDocumentoService
{
    public async Task<IReadOnlyList<TipoDocumentoDto>> ListarAsync(CancellationToken ct = default)
    {
        var tipos = await db.TiposDocumento.Include(t => t.Campos).OrderBy(t => t.Nombre).ToListAsync(ct);
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

        var tipo = new TipoDocumento
        {
            Nombre = dto.Nombre,
            Descripcion = dto.Descripcion,
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

        tipo.Nombre = dto.Nombre;
        tipo.Descripcion = dto.Descripcion;
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
        t.Id, t.Nombre, t.Descripcion, t.Activo,
        t.Campos.OrderBy(c => c.Orden).Select(c => new CampoTipoDocumentoDto(
            c.Id, c.Nombre, c.Etiqueta, c.TipoCampo, c.Requerido, c.Orden,
            c.OpcionesJson is null ? null : JsonSerializer.Deserialize<List<string>>(c.OpcionesJson))).ToList());
}
