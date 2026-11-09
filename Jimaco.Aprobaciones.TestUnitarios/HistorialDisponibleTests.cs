using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>
/// 2026-11-09 (Hoja 16100, "lo que marcamos vuelve a 0"): bitácora de cada cambio de Disponible, y
/// devolución del progreso de un ítem que se quita y se vuelve a poner en la Cotización de WO.
/// </summary>
public class HistorialDisponibleTests
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private HojaAlistamientoService CrearServicio() =>
        new(db, Mock.Of<IConfiguration>(), TimeProvider.System, Mock.Of<INotificacionService>());

    private static RenglonDocumentoVinculado Renglon(int orden, string codigo, decimal cantidad, decimal disponible) =>
        new() { Orden = orden, Codigo = codigo, Descripcion = codigo, Cantidad = cantidad, CantidadDisponible = disponible };

    /// <summary>Dos copias (dos OC ligadas a la misma Cotización), sin OC asociada para no exigir aprobación.</summary>
    private async Task<(int UsuarioId, int HojaId)> SembrarAsync()
    {
        var rol = new Rol { Nombre = "Transporte", Activo = true, PuedeGestionarAlistamiento = true };
        var usuario = new Usuario { Nombre = "Operario Bodega", Email = "bodega@jimaco.local", PasswordHash = "x" };
        db.AddRange(rol, usuario);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rol.Id });

        DocumentoVinculado Copia() => new()
        {
            PrefijoOrigen = "COT",
            NumeroReferencia = "16100",
            Renglones = [Renglon(1, "PJ-4010", 8, 8), Renglon(2, "PJ-4006", 6, 6), Renglon(3, "PJ-16385", 2, 0)]
        };
        var primera = Copia();
        db.DocumentosVinculados.AddRange(primera, Copia());
        await db.SaveChangesAsync();
        return (usuario.Id, primera.Id);
    }

    private static DocumentoVinculadoInputDto Cotizacion(params (string Codigo, decimal Cantidad)[] renglones) => new(
        "COT", "16100", new DateTime(2026, 10, 30), new DateTime(2026, 12, 30), "CONSORCIO", "900", null, null, null, null, null, null,
        renglones.Select(r => new RenglonDocumentoVinculadoInputDto(r.Codigo, r.Codigo, r.Cantidad, "Und", 1000, 0.19m)).ToList());

    private async Task<List<decimal>> DisponiblePorCopiaAsync(string codigo) =>
        await db.RenglonesDocumentoVinculado.Where(r => r.Codigo == codigo).OrderBy(r => r.DocumentoVinculadoId)
            .Select(r => r.CantidadDisponible).ToListAsync();

    [Fact]
    public async Task GuardarDisponible_QuedaEnLaBitacoraUnaSolaVezConElUsuario()
    {
        var (usuarioId, hojaId) = await SembrarAsync();
        var renglonId = await db.RenglonesDocumentoVinculado
            .Where(r => r.DocumentoVinculadoId == hojaId && r.Codigo == "PJ-16385").Select(r => r.Id).SingleAsync();

        await CrearServicio().ActualizarCantidadesAsync(hojaId, usuarioId,
            new ActualizarCantidadesDisponiblesDto([new ActualizarCantidadRenglonDto(renglonId, 2)]));

        var entrada = Assert.Single(await db.HistorialDisponible.ToListAsync());
        Assert.Equal(HistorialDisponible.OrigenUsuario, entrada.Origen);
        Assert.Equal("PJ-16385", entrada.Codigo);
        Assert.Equal(0, entrada.CantidadAnterior);
        Assert.Equal(2, entrada.CantidadNueva);
        Assert.Equal("Operario Bodega", entrada.NombreUsuario);
        Assert.Equal([2m, 2m], await DisponiblePorCopiaAsync("PJ-16385"));
    }

    [Fact]
    public async Task ItemQuitadoYVueltoAPonerEnWO_RecuperaLoMarcado()
    {
        await SembrarAsync();
        var servicio = CrearServicio();

        // Lectura 1: en WO quitaron PJ-4006 (estaba marcado 6/6).
        await servicio.RefrescarDesdeWOAsync(Cotizacion(("PJ-4010", 8), ("PJ-16385", 2)));
        Assert.Empty(await DisponiblePorCopiaAsync("PJ-4006"));
        var deposito = Assert.Single(await db.HistorialDisponible.ToListAsync());
        Assert.Equal(HistorialDisponible.OrigenRetiradoEnWO, deposito.Origen);
        Assert.Equal(6, deposito.CantidadPorRestaurar);

        // Lectura 2: lo vuelven a poner — vuelve con lo que ya estaba marcado, en las dos copias.
        await servicio.RefrescarDesdeWOAsync(Cotizacion(("PJ-4006", 6), ("PJ-4010", 8), ("PJ-16385", 2)));
        Assert.Equal([6m, 6m], await DisponiblePorCopiaAsync("PJ-4006"));

        var historial = await db.HistorialDisponible.OrderBy(h => h.Id).ToListAsync();
        Assert.Equal(0, historial[0].CantidadPorRestaurar); // el depósito se consumió
        Assert.Equal(HistorialDisponible.OrigenRestauradoDesdeWO, historial[1].Origen);
        Assert.Equal(2, historial.Count);

        // Lectura 3 (sin cambios): no se vuelve a devolver ni se registra nada nuevo.
        await servicio.RefrescarDesdeWOAsync(Cotizacion(("PJ-4006", 6), ("PJ-4010", 8), ("PJ-16385", 2)));
        Assert.Equal(2, await db.HistorialDisponible.CountAsync());
        Assert.Equal([6m, 6m], await DisponiblePorCopiaAsync("PJ-4006"));
    }

    [Fact]
    public async Task ItemQueVuelveConMenosCantidad_SeRecortaYQuedaRegistrado()
    {
        await SembrarAsync();
        var servicio = CrearServicio();

        await servicio.RefrescarDesdeWOAsync(Cotizacion(("PJ-4006", 6), ("PJ-4010", 5), ("PJ-16385", 2)));

        Assert.Equal([5m, 5m], await DisponiblePorCopiaAsync("PJ-4010"));
        var entrada = Assert.Single(await db.HistorialDisponible.ToListAsync());
        Assert.Equal(HistorialDisponible.OrigenRefrescoWO, entrada.Origen);
        Assert.Equal(8, entrada.CantidadAnterior);
        Assert.Equal(5, entrada.CantidadNueva);
    }

    [Fact]
    public async Task RefrescoSinCambios_NoEscribeBitacora()
    {
        await SembrarAsync();
        await CrearServicio().RefrescarDesdeWOAsync(Cotizacion(("PJ-4010", 8), ("PJ-4006", 6), ("PJ-16385", 2)));
        Assert.Empty(await db.HistorialDisponible.ToListAsync());
    }
}
