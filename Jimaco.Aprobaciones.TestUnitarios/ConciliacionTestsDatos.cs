using Jimaco.Aprobaciones.Modelo;
using Jimaco.Aprobaciones.Modelo.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.TestUnitarios;

/// <summary>Base en memoria con dos usuarios que tienen el rol Conciliador (y uno sin permiso).</summary>
internal static class ConciliacionTestsDatos
{
    public const int Ana = 1;
    public const int Luis = 2;
    public const int SinPermiso = 3;

    public static AppDbContext CrearContexto()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var conciliador = new Rol { Id = 900, Nombre = "Conciliador", Activo = true, PuedeVerConciliacion = true };
        var otro = new Rol { Id = 901, Nombre = "Logística", Activo = true };
        db.Roles.AddRange(conciliador, otro);
        db.Usuarios.AddRange(
            new Usuario { Id = Ana, Nombre = "Ana", Email = "ana@x.com", PasswordHash = "x" },
            new Usuario { Id = Luis, Nombre = "Luis", Email = "luis@x.com", PasswordHash = "x" },
            new Usuario { Id = SinPermiso, Nombre = "Pedro", Email = "pedro@x.com", PasswordHash = "x" });
        db.UsuarioRoles.AddRange(
            new UsuarioRol { UsuarioId = Ana, RolId = 900 },
            new UsuarioRol { UsuarioId = Luis, RolId = 900 },
            new UsuarioRol { UsuarioId = SinPermiso, RolId = 901 });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }
}
