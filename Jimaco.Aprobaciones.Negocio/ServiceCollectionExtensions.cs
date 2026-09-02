using Jimaco.Aprobaciones.Negocio.Interfaces;
using Jimaco.Aprobaciones.Negocio.Servicios;
using Microsoft.Extensions.DependencyInjection;

namespace Jimaco.Aprobaciones.Negocio;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNegocio(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAlmacenamientoArchivos, AlmacenamientoArchivosDisco>();
        services.AddScoped<IJwtGenerador, JwtGenerador>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRolService, RolService>();
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<ITipoDocumentoService, TipoDocumentoService>();
        services.AddScoped<IDefinicionFlujoService, DefinicionFlujoService>();
        services.AddScoped<IInstanciaDocumentoService, InstanciaDocumentoService>();
        return services;
    }
}
