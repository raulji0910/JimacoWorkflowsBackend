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

/// <summary>2026-11-11: las Hojas recién completadas quedan arriba — FechaCompletada solo en la transición.</summary>
public class FechaCompletadaHojaTests
{
    private sealed class Reloj : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Reloj reloj = new();

    private HojaAlistamientoService Servicio() =>
        new(db, Mock.Of<IConfiguration>(), reloj, Mock.Of<INotificacionService>());

    private static RenglonDocumentoVinculado Renglon(int orden, string codigo, decimal cantidad, decimal disponible) =>
        new() { Orden = orden, Codigo = codigo, Descripcion = codigo, Cantidad = cantidad, CantidadDisponible = disponible };

    private async Task<(int UsuarioId, int HojaId)> SembrarAsync(decimal disponibleUltimo)
    {
        var rol = new Rol { Nombre = "Transporte", Activo = true, PuedeGestionarAlistamiento = true, PuedeVerAlistamiento = true };
        var usuario = new Usuario { Nombre = "Logistica", Email = "logistica@jimaco.local", PasswordHash = "x" };
        db.AddRange(rol, usuario);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuario.Id, RolId = rol.Id });

        DocumentoVinculado Copia() => new()
        {
            PrefijoOrigen = "COT", NumeroReferencia = "16300",
            Renglones = [Renglon(1, "A", 5, 5), Renglon(2, "B", 3, disponibleUltimo)]
        };
        var primera = Copia();
        db.DocumentosVinculados.AddRange(primera, Copia());
        await db.SaveChangesAsync();
        return (usuario.Id, primera.Id);
    }

    private async Task<int> RenglonBAsync(int hojaId) =>
        await db.RenglonesDocumentoVinculado.Where(r => r.DocumentoVinculadoId == hojaId && r.Codigo == "B").Select(r => r.Id).SingleAsync();

    private static DocumentoVinculadoInputDto Cotizacion(params (string Codigo, decimal Cantidad)[] renglones) => new(
        "COT", "16300", new DateTime(2026, 11, 1), new DateTime(2026, 12, 1), "CLIENTE", "900", null, null, null, null, null, null,
        renglones.Select(r => new RenglonDocumentoVinculadoInputDto(r.Codigo, r.Codigo, r.Cantidad, "Und", 1000, 0.19m)).ToList());

    [Fact]
    public async Task AlCompletarElUltimoItem_QuedaLaFecha_EnTodasLasCopias_YSaleEnElListado()
    {
        var (usuarioId, hojaId) = await SembrarAsync(disponibleUltimo: 1);

        await Servicio().ActualizarCantidadesAsync(hojaId, usuarioId,
            new ActualizarCantidadesDisponiblesDto([new ActualizarCantidadRenglonDto(await RenglonBAsync(hojaId), 3)]));

        var esperada = reloj.Ahora.UtcDateTime;
        Assert.All(await db.DocumentosVinculados.ToListAsync(), d => Assert.Equal(esperada, d.FechaCompletada));
        var fila = Assert.Single(await Servicio().ListarAsync(usuarioId));
        Assert.True(fila.Completa);
        Assert.Equal(esperada, fila.FechaCompletada);
    }

    [Fact]
    public async Task GuardarOtraVezUnaHojaYaCompleta_NoLeCambiaLaFecha()
    {
        var (usuarioId, hojaId) = await SembrarAsync(disponibleUltimo: 1);
        var renglonB = await RenglonBAsync(hojaId);
        await Servicio().ActualizarCantidadesAsync(hojaId, usuarioId, new ActualizarCantidadesDisponiblesDto([new ActualizarCantidadRenglonDto(renglonB, 3)]));
        var primera = reloj.Ahora.UtcDateTime;

        reloj.Ahora = reloj.Ahora.AddHours(3);
        await Servicio().RefrescarDesdeWOAsync(Cotizacion(("A", 5), ("B", 3)));

        Assert.All(await db.DocumentosVinculados.ToListAsync(), d => Assert.Equal(primera, d.FechaCompletada));
    }

    [Fact]
    public async Task UnaHojaCompletaDeAntesSinFecha_NoApareceComoRecienCompletadaPorUnRefresco()
    {
        var (usuarioId, _) = await SembrarAsync(disponibleUltimo: 3); // ya completa, sin fecha (como las viejas)

        await Servicio().RefrescarDesdeWOAsync(Cotizacion(("A", 5), ("B", 3)));

        Assert.All(await db.DocumentosVinculados.ToListAsync(), d => Assert.Null(d.FechaCompletada));
        Assert.Null(Assert.Single(await Servicio().ListarAsync(usuarioId)).FechaCompletada);
    }

    [Fact]
    public async Task SiDejaDeEstarCompleta_SeBorra_YAlVolverACompletarseTomaLaNueva()
    {
        var (usuarioId, hojaId) = await SembrarAsync(disponibleUltimo: 1);
        var renglonB = await RenglonBAsync(hojaId);
        await Servicio().ActualizarCantidadesAsync(hojaId, usuarioId, new ActualizarCantidadesDisponiblesDto([new ActualizarCantidadRenglonDto(renglonB, 3)]));

        // En WO le agregan un ítem: deja de estar completa
        reloj.Ahora = reloj.Ahora.AddHours(1);
        await Servicio().RefrescarDesdeWOAsync(Cotizacion(("A", 5), ("B", 3), ("C", 2)));
        Assert.All(await db.DocumentosVinculados.ToListAsync(), d => Assert.Null(d.FechaCompletada));

        // Logística completa el ítem nuevo: fecha nueva
        reloj.Ahora = reloj.Ahora.AddHours(1);
        var renglonC = await db.RenglonesDocumentoVinculado.Where(r => r.DocumentoVinculadoId == hojaId && r.Codigo == "C").Select(r => r.Id).SingleAsync();
        await Servicio().ActualizarCantidadesAsync(hojaId, usuarioId, new ActualizarCantidadesDisponiblesDto([new ActualizarCantidadRenglonDto(renglonC, 2)]));
        Assert.All(await db.DocumentosVinculados.ToListAsync(), d => Assert.Equal(reloj.Ahora.UtcDateTime, d.FechaCompletada));
    }
}
