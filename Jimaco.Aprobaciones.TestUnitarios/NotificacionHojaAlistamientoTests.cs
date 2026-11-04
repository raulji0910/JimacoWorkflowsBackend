using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Jimaco.Aprobaciones.Negocio.Utilidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>
/// Destinatarios de los avisos de Hoja de Alistamiento: Logística + quien elaboró la Cotización
/// (vía Usuario.NombresCompletosWO), con respaldo en SolicitanteWO — nunca todas las vendedoras.
/// </summary>
public class NotificacionHojaAlistamientoTests
{
    private const string Laura = "LAURA SOFIA HERNANDEZ CARO";
    private const string Leidy = "LEIDY CATALINA JIMENEZ CARO";
    private const string Lady = "LADY JOHANNA CARDONA RODRIGUEZ";
    private const string Yazmin = "YAZMIN RENDON RODRIGUEZ";

    private readonly AppDbContext db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly List<string> enviadosA = [];

    private NotificacionService CrearServicio()
    {
        var emailSender = new Mock<IEmailSender>();
        emailSender
            .Setup(e => e.EnviarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AdjuntoCorreo>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IReadOnlyList<AdjuntoCorreo>?, CancellationToken>((destinatario, _, _, _, _) => enviadosA.Add(destinatario))
            .Returns(Task.CompletedTask);

        return new NotificacionService(db, emailSender.Object, Mock.Of<IJwtGenerador>(),
            Mock.Of<IConfiguration>(), TimeProvider.System, Mock.Of<IAlmacenamientoArchivos>(),
            NullLogger<NotificacionService>.Instance);
    }

    private async Task SembrarUsuariosAsync()
    {
        var rolLogistica = new Rol { Nombre = "Area de Transporte", PuedeGestionarAlistamiento = true };
        var rolEmisor = new Rol { Nombre = "Emisor de OC", PuedeVerAlistamiento = true };
        db.AddRange(rolLogistica, rolEmisor);

        var usuarios = new[]
        {
            new Usuario { Nombre = "Logistica", Email = "logistica@test", PasswordHash = "x" },
            new Usuario { Nombre = "Laura", Email = "laura@test", PasswordHash = "x", UsuarioWO = "LAURA", NombresCompletosWO = $"{Laura}; {Yazmin}" },
            new Usuario { Nombre = "Catalina", Email = "catalina@test", PasswordHash = "x", UsuarioWO = "CATALINA JIMENEZ", NombresCompletosWO = Leidy },
            new Usuario { Nombre = "Lady", Email = "lady@test", PasswordHash = "x", UsuarioWO = "LADY CARDONA", NombresCompletosWO = Lady },
            new Usuario { Nombre = "Yazmin", Email = "yazmin@test", PasswordHash = "x", UsuarioWO = "YAZMIN RENDON", NombresCompletosWO = $"{Yazmin}; {Laura}" }
        };
        db.AddRange(usuarios);
        await db.SaveChangesAsync();

        db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = usuarios[0].Id, RolId = rolLogistica.Id });
        foreach (var vendedora in usuarios.Skip(1))
            db.UsuarioRoles.Add(new UsuarioRol { UsuarioId = vendedora.Id, RolId = rolEmisor.Id });
        await db.SaveChangesAsync();
    }

    private async Task<DocumentoVinculado> CrearHojaAsync(string elaboradoPor, string? solicitanteWO = null)
    {
        InstanciaDocumento? instancia = null;
        if (solicitanteWO is not null)
        {
            var tipo = new TipoDocumento { Nombre = "Orden de Compra", PrefijoWorldOffice = "OC" };
            var flujo = new DefinicionFlujo { Nombre = "Flujo", TipoDocumento = tipo };
            instancia = new InstanciaDocumento { TipoDocumento = tipo, DefinicionFlujo = flujo, NumeroReferencia = "19999", SolicitanteWO = solicitanteWO };
            db.Add(instancia);
        }

        var hoja = new DocumentoVinculado
        {
            PrefijoOrigen = "COT",
            NumeroReferencia = Guid.NewGuid().ToString("N")[..6],
            ElaboradoPor = elaboradoPor,
            InstanciaDocumento = instancia
        };
        db.Add(hoja);
        await db.SaveChangesAsync();
        return hoja;
    }

    [Fact]
    public async Task Atrasada_HojaDeLaura_LeLlegaALogisticaLauraYYazmin_YNoALasDemas()
    {
        await SembrarUsuariosAsync();
        var hoja = await CrearHojaAsync(Laura);

        await CrearServicio().NotificarHojaAlistamientoAtrasadaAsync(hoja.Id);

        Assert.Equal(["laura@test", "logistica@test", "yazmin@test"], enviadosA.Order());
    }

    [Fact]
    public async Task Atrasada_HojaDeYazmin_LeLlegaALogisticaYazminYLaura()
    {
        await SembrarUsuariosAsync();
        var hoja = await CrearHojaAsync(Yazmin);

        await CrearServicio().NotificarHojaAlistamientoAtrasadaAsync(hoja.Id);

        Assert.Equal(["laura@test", "logistica@test", "yazmin@test"], enviadosA.Order());
    }

    [Fact]
    public async Task Atrasada_HojaDeLady_LeLlegaSoloALogisticaYLady()
    {
        await SembrarUsuariosAsync();
        var hoja = await CrearHojaAsync(Lady);

        await CrearServicio().NotificarHojaAlistamientoAtrasadaAsync(hoja.Id);

        Assert.Equal(["lady@test", "logistica@test"], enviadosA.Order());
    }

    [Fact]
    public async Task Atrasada_HojaDeLeidy_LeLlegaSoloALogisticaYCatalina()
    {
        await SembrarUsuariosAsync();
        var hoja = await CrearHojaAsync(Leidy);

        await CrearServicio().NotificarHojaAlistamientoAtrasadaAsync(hoja.Id);

        Assert.Equal(["catalina@test", "logistica@test"], enviadosA.Order());
    }

    [Fact]
    public async Task Atrasada_ElaboradoPorSinUsuario_UsaQuienMandoLaOcAAprobar()
    {
        await SembrarUsuariosAsync();
        var hoja = await CrearHojaAsync("JORGE ENRIQUE NIÑO", solicitanteWO: "YAZMIN RENDON");

        await CrearServicio().NotificarHojaAlistamientoAtrasadaAsync(hoja.Id);

        Assert.Equal(["logistica@test", "yazmin@test"], enviadosA.Order());
    }

    [Fact]
    public async Task Atrasada_SinOcYSinUsuario_SoloLogistica()
    {
        await SembrarUsuariosAsync();
        var hoja = await CrearHojaAsync("ALGUIEN NUEVO");

        await CrearServicio().NotificarHojaAlistamientoAtrasadaAsync(hoja.Id);

        Assert.Equal(["logistica@test"], enviadosA);
    }

    [Fact]
    public async Task CompletaYRemisionada_HojaDeLady_LeLlegaALogisticaYLady_NoAQuienMandoLaOc()
    {
        await SembrarUsuariosAsync();
        var hoja = await CrearHojaAsync(Lady, solicitanteWO: "YAZMIN RENDON");

        await CrearServicio().NotificarHojaCompletaYRemisionadaAsync(hoja.Id, [hoja.InstanciaDocumentoId!.Value]);

        Assert.Equal(["lady@test", "logistica@test"], enviadosA.Order());
    }

    [Theory]
    [InlineData("LAURA SOFIA HERNANDEZ CARO; LEIDY CATALINA JIMENEZ CARO", "  leidy catalina jimenez caro ", true)]
    [InlineData("LAURA SOFIA HERNANDEZ CARO", "LAURA", false)]
    [InlineData(null, "LAURA SOFIA HERNANDEZ CARO", false)]
    [InlineData("LAURA SOFIA HERNANDEZ CARO", null, false)]
    public void NombresCompletosWO_CoincidenciaExactaPorNombre_SinImportarMayusculasNiEspacios(string? valor, string? nombre, bool esperado) =>
        Assert.Equal(esperado, NombresCompletosWO.Contiene(valor, nombre));
}
