using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Jimaco.Aprobaciones.Negocio.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Jimaco.Aprobaciones.Negocio.Servicios;

public class JwtGenerador(IConfiguration configuration) : IJwtGenerador
{
    public string GenerarToken(Usuario usuario, IReadOnlyList<string> roles)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException("Falta configurar Jwt:Secret.");
        var issuer = configuration["Jwt:Issuer"] ?? "Jimaco.Aprobaciones";
        var audience = configuration["Jwt:Audience"] ?? "Jimaco.Aprobaciones";
        var expiracionMinutos = int.TryParse(configuration["Jwt:ExpiracionMinutos"], out var m) ? m : 480;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Nombre),
            new(ClaimTypes.Email, usuario.Email)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiracionMinutos),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
