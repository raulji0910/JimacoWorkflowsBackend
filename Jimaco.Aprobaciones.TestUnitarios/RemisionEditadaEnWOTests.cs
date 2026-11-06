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
/// Bug real 2026-11-06 (Hoja 16372 / Remisión 29413): la Remisión se editó en WO después de
/// aplicada y le quitaron un ítem — el despacho viejo de ese ítem tiene que desaparecer de la Hoja.
/// </summary>
public class RemisionEditadaEnWOTests
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private HojaAlistamientoService CrearServicio() =>
        new(db, Mock.Of<IConfiguration>(), TimeProvider.System, Mock.Of<INotificacionService>());

    private static RenglonDocumentoVinculado Renglon(int orden, string codigo, decimal cantidad) =>
        new() { Orden = orden, Codigo = codigo, Descripcion = codigo, Cantidad = cantidad, CantidadDisponible = cantidad };

    private async Task SembrarHojaAsync()
    {
        db.DocumentosVinculados.Add(new DocumentoVinculado
        {
            PrefijoOrigen = "COT",
            NumeroReferencia = "16372",
            Renglones = [Renglon(1, "PJ-2889", 95), Renglon(2, "PJ-4691", 7), Renglon(3, "PJ-3759", 10), Renglon(4, "PJ-3759", 5)]
        });
        await db.SaveChangesAsync();
    }

    private static MarcarRemisionadaDto Remision(string numero, params (string Codigo, decimal Cantidad)[] renglones) =>
        new("COT", "16372", numero, new DateTime(2026, 10, 27), renglones.Select(r => new RenglonRemisionadoDto(r.Codigo, r.Cantidad)).ToList());

    private async Task<Dictionary<int, decimal>> RemisionadoPorOrdenAsync() =>
        (await db.RenglonesDocumentoVinculado.Include(r => r.DespachosRemision).ToListAsync())
            .ToDictionary(r => r.Orden, r => r.DespachosRemision.Sum(x => x.CantidadRemisionada));

    [Fact]
    public async Task ItemQuitadoDeLaRemisionEnWO_PierdeElDespacho()
    {
        await SembrarHojaAsync();
        var servicio = CrearServicio();

        await servicio.MarcarRemisionadaAsync(Remision("29413", ("PJ-2889", 95), ("PJ-4691", 7)));
        Assert.Equal(7, (await RemisionadoPorOrdenAsync())[2]);

        // En WO le quitaron el PJ-4691 a la Remisión 29413; el Sincronizador la vuelve a mandar.
        await servicio.MarcarRemisionadaAsync(Remision("29413", ("PJ-2889", 95)));

        var remisionado = await RemisionadoPorOrdenAsync();
        Assert.Equal(95, remisionado[1]);
        Assert.Equal(0, remisionado[2]);
        Assert.Empty(await db.RemisionesRenglon.Where(x => x.CantidadRemisionada == 0).ToListAsync());
    }

    [Fact]
    public async Task CodigoRepetido_QueAhoraTraeMenos_VaciaElSegundoRenglon()
    {
        await SembrarHojaAsync();
        var servicio = CrearServicio();

        await servicio.MarcarRemisionadaAsync(Remision("29413", ("PJ-3759", 15)));
        Assert.Equal((10m, 5m), ((await RemisionadoPorOrdenAsync())[3], (await RemisionadoPorOrdenAsync())[4]));

        await servicio.MarcarRemisionadaAsync(Remision("29413", ("PJ-3759", 8)));

        var remisionado = await RemisionadoPorOrdenAsync();
        Assert.Equal(8, remisionado[3]);
        Assert.Equal(0, remisionado[4]);
    }

    [Fact]
    public async Task OtraRemision_NoTocaLosDespachosDeLaPrimera()
    {
        await SembrarHojaAsync();
        var servicio = CrearServicio();

        await servicio.MarcarRemisionadaAsync(Remision("29413", ("PJ-4691", 7)));
        await servicio.MarcarRemisionadaAsync(Remision("29500", ("PJ-2889", 95)));

        var remisionado = await RemisionadoPorOrdenAsync();
        Assert.Equal(7, remisionado[2]);
        Assert.Equal(95, remisionado[1]);
    }
}
