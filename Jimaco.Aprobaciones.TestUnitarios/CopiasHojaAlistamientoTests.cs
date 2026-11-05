using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.DTOs;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>
/// Bug real 2026-11-04 (OC 19857 / Hoja 16109): reenviar desde WO una OC devuelta dejaba la copia
/// vieja de la Hoja huérfana y creaba una nueva en 0 → alerta de "sigue incompleta" sobre una Hoja
/// completa. Una copia nueva de una Cotización ya existente tiene que heredar el avance de sus hermanas.
/// </summary>
public class CopiasHojaAlistamientoTests
{
    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private InstanciaDocumentoService CrearServicio() =>
        new(db, Mock.Of<IAlmacenamientoArchivos>(), Mock.Of<INotificacionService>(), TimeProvider.System);

    private async Task<(int tipoId, int emisorId, int comercialId)> SembrarAsync()
    {
        var rol = new Rol { Nombre = "Comercial" };
        var emisor = new Usuario { Nombre = "Emisor", Email = "emisor@test", PasswordHash = "x" };
        var comercial = new Usuario { Nombre = "Comercial", Email = "comercial@test", PasswordHash = "x" };
        var tipo = new TipoDocumento { Nombre = "Orden de Compra", PrefijoWorldOffice = "OC" };
        db.AddRange(rol, emisor, comercial, tipo);
        await db.SaveChangesAsync();
        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = comercial.Id, RolId = rol.Id });

        var paso = new PasoFlujo { Nombre = "Aprobación comercial", Orden = 1, PermiteDevolver = true, PermiteDevolverAlEmisor = true };
        db.DefinicionesFlujo.Add(new DefinicionFlujo { Nombre = "Flujo", TipoDocumentoId = tipo.Id, Activo = true, Pasos = [paso] });
        await db.SaveChangesAsync();
        db.PasoFlujoRoles.Add(new PasoFlujoRol { PasoFlujoId = paso.Id, RolId = rol.Id });
        await db.SaveChangesAsync();
        return (tipo.Id, emisor.Id, comercial.Id);
    }

    private static DocumentoVinculadoInputDto Cotizacion(string numero) => new(
        "COT", numero, new DateTime(2026, 10, 29), new DateTime(2026, 12, 13), "VERANO ENERGY", "900", null, null, null,
        "MARIA DEL PILAR JIMENEZ ROJAS", "Credito", null,
        [
            new RenglonDocumentoVinculadoInputDto("PJ-2163", "CONECTOR", 10, "Und", 1000, 0.19m),
            new RenglonDocumentoVinculadoInputDto("PJ-2884", "FERULA", 2, "Und", 500, 0.19m)
        ]);

    private static CrearInstanciaDocumentoDto Oc(int tipoId, int asiento, params string[] cotizaciones) => new(
        tipoId, asiento.ToString(), "Proveedor", null, null, null,
        IdAsientoContableOrigen: asiento, PrefijoOrigen: "OC",
        DocumentosVinculados: cotizaciones.Select(Cotizacion).ToList());

    /// <summary>Deja la copia de la Cotización como Transporte la dejó: todo disponible, remisionado y avisado.</summary>
    private async Task CompletarHojaAsync(string numero)
    {
        foreach (var copia in await db.DocumentosVinculados.Include(d => d.Renglones).Where(d => d.NumeroReferencia == numero).ToListAsync())
        {
            copia.AvisoCompletaRemisionadaEnviado = true;
            foreach (var r in copia.Renglones)
            {
                r.CantidadDisponible = r.Cantidad;
                r.DespachosRemision.Add(new RemisionRenglon { NumeroRemision = "29485", Fecha = new DateTime(2026, 10, 30), CantidadRemisionada = r.Cantidad });
            }
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task OcNuevaConUnaCotizacionYaCompleta_LaCopiaNuevaHeredaElAvance()
    {
        var (tipoId, emisorId, _) = await SembrarAsync();
        var servicio = CrearServicio();
        await servicio.CrearAsync(Oc(tipoId, 1, "16109"), emisorId);
        await CompletarHojaAsync("16109");

        var segunda = await servicio.CrearAsync(Oc(tipoId, 2, "16109"), emisorId);

        var copia = await db.DocumentosVinculados.Include(d => d.Renglones).ThenInclude(r => r.DespachosRemision)
            .SingleAsync(d => d.InstanciaDocumentoId == segunda.Id);
        Assert.All(copia.Renglones, r => Assert.Equal(r.Cantidad, r.CantidadDisponible));
        Assert.All(copia.Renglones, r => Assert.Equal(r.Cantidad, r.DespachosRemision.Sum(x => x.CantidadRemisionada)));
        Assert.True(copia.AvisoCompletaRemisionadaEnviado);
    }

    [Fact]
    public async Task ReenvioDesdeWODeUnaOcDevuelta_ConservaLaMismaCopia_SinDejarHuerfanas()
    {
        var (tipoId, emisorId, comercialId) = await SembrarAsync();
        var servicio = CrearServicio();
        var oc = await servicio.CrearAsync(Oc(tipoId, 1, "16109"), emisorId);
        await CompletarHojaAsync("16109");
        var idCopiaOriginal = (await db.DocumentosVinculados.SingleAsync()).Id;
        await servicio.EjecutarAccionAsync(oc.Id, comercialId, new EjecutarAccionDto(TipoAccion.Devuelto, "Corregir precio"));

        await servicio.CrearAsync(Oc(tipoId, 1, "16109"), emisorId); // el correo de "Solicitar Aprobación" otra vez

        var copias = await db.DocumentosVinculados.Include(d => d.Renglones).ToListAsync();
        var copia = Assert.Single(copias);
        Assert.Equal(idCopiaOriginal, copia.Id);
        Assert.Equal(oc.Id, copia.InstanciaDocumentoId);
        Assert.All(copia.Renglones, r => Assert.Equal(r.Cantidad, r.CantidadDisponible));
    }

    [Fact]
    public async Task ReenvioDesdeWOQueCambiaLaCotizacion_BorraLaViejaYCreaLaNueva()
    {
        var (tipoId, emisorId, comercialId) = await SembrarAsync();
        var servicio = CrearServicio();
        var oc = await servicio.CrearAsync(Oc(tipoId, 1, "16109"), emisorId);
        await servicio.EjecutarAccionAsync(oc.Id, comercialId, new EjecutarAccionDto(TipoAccion.Devuelto, "Era otra cotización"));

        await servicio.CrearAsync(Oc(tipoId, 1, "16200"), emisorId);

        var copia = Assert.Single(await db.DocumentosVinculados.ToListAsync());
        Assert.Equal("16200", copia.NumeroReferencia);
        Assert.Equal(oc.Id, copia.InstanciaDocumentoId);
    }
}
