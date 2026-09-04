using Jimaco.Aprobaciones.Modelo.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Aprobaciones.Modelo;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<UsuarioRol> UsuarioRoles => Set<UsuarioRol>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<TipoDocumento> TiposDocumento => Set<TipoDocumento>();
    public DbSet<CampoTipoDocumento> CamposTipoDocumento => Set<CampoTipoDocumento>();
    public DbSet<DefinicionFlujo> DefinicionesFlujo => Set<DefinicionFlujo>();
    public DbSet<PasoFlujo> PasosFlujo => Set<PasoFlujo>();
    public DbSet<PasoFlujoRol> PasoFlujoRoles => Set<PasoFlujoRol>();
    public DbSet<InstanciaDocumento> InstanciasDocumento => Set<InstanciaDocumento>();
    public DbSet<Adjunto> Adjuntos => Set<Adjunto>();
    public DbSet<HistorialAccion> HistorialAcciones => Set<HistorialAccion>();
    public DbSet<RenglonInstanciaDocumento> RenglonesInstanciaDocumento => Set<RenglonInstanciaDocumento>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();

    /// <summary>Id fijo del rol sembrado en la migración inicial — útil para lógica de arranque (ver SeedAdminAsync en Program.cs).</summary>
    public const int RolAdminId = 1;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Rol>(e =>
        {
            e.HasIndex(r => r.Nombre).IsUnique();
            e.HasData(new Rol
            {
                Id = RolAdminId,
                Nombre = "Admin",
                Descripcion = "Administra usuarios, roles, tipos de documento y flujos.",
                Activo = true
            });
        });

        modelBuilder.Entity<UsuarioRol>(e =>
        {
            e.HasKey(ur => new { ur.UsuarioId, ur.RolId });

            e.HasOne(ur => ur.Usuario)
                .WithMany(u => u.UsuarioRoles)
                .HasForeignKey(ur => ur.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ur => ur.Rol)
                .WithMany(r => r.UsuarioRoles)
                .HasForeignKey(ur => ur.RolId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<TipoDocumento>(e =>
        {
            e.HasIndex(t => t.Nombre).IsUnique();
        });

        modelBuilder.Entity<CampoTipoDocumento>(e =>
        {
            e.HasOne(c => c.TipoDocumento)
                .WithMany(t => t.Campos)
                .HasForeignKey(c => c.TipoDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(c => new { c.TipoDocumentoId, c.Nombre }).IsUnique();
        });

        modelBuilder.Entity<DefinicionFlujo>(e =>
        {
            e.HasOne(f => f.TipoDocumento)
                .WithMany(t => t.DefinicionesFlujo)
                .HasForeignKey(f => f.TipoDocumentoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PasoFlujo>(e =>
        {
            e.HasOne(p => p.DefinicionFlujo)
                .WithMany(f => f.Pasos)
                .HasForeignKey(p => p.DefinicionFlujoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(p => p.PasoDestinoDevolucion)
                .WithMany()
                .HasForeignKey(p => p.PasoDestinoDevolucionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(p => new { p.DefinicionFlujoId, p.Orden }).IsUnique();
        });

        modelBuilder.Entity<PasoFlujoRol>(e =>
        {
            e.HasKey(pr => new { pr.PasoFlujoId, pr.RolId });

            e.HasOne(pr => pr.PasoFlujo)
                .WithMany(p => p.PasoFlujoRoles)
                .HasForeignKey(pr => pr.PasoFlujoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(pr => pr.Rol)
                .WithMany(r => r.PasoFlujoRoles)
                .HasForeignKey(pr => pr.RolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InstanciaDocumento>(e =>
        {
            e.Property(i => i.Valor).HasPrecision(18, 2);

            e.HasOne(i => i.TipoDocumento)
                .WithMany()
                .HasForeignKey(i => i.TipoDocumentoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(i => i.DefinicionFlujo)
                .WithMany()
                .HasForeignKey(i => i.DefinicionFlujoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(i => i.PasoActual)
                .WithMany()
                .HasForeignKey(i => i.PasoActualId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(i => i.CreadoPorUsuario)
                .WithMany()
                .HasForeignKey(i => i.CreadoPorUsuarioId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(i => i.NumeroReferencia);
            e.HasIndex(i => i.Estado);
        });

        modelBuilder.Entity<Adjunto>(e =>
        {
            e.HasOne(a => a.InstanciaDocumento)
                .WithMany(i => i.Adjuntos)
                .HasForeignKey(a => a.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.SubidoPorUsuario)
                .WithMany()
                .HasForeignKey(a => a.SubidoPorUsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HistorialAccion>(e =>
        {
            e.HasOne(h => h.InstanciaDocumento)
                .WithMany(i => i.Historial)
                .HasForeignKey(h => h.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(h => h.PasoFlujo)
                .WithMany()
                .HasForeignKey(h => h.PasoFlujoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(h => h.Usuario)
                .WithMany()
                .HasForeignKey(h => h.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RenglonInstanciaDocumento>(e =>
        {
            e.Property(r => r.Cantidad).HasPrecision(18, 3);
            e.Property(r => r.ValorUnitario).HasPrecision(18, 2);
            e.Property(r => r.PorcentajeIva).HasPrecision(5, 4);
            e.Property(r => r.Total).HasPrecision(18, 2);

            e.HasOne(r => r.InstanciaDocumento)
                .WithMany(i => i.Renglones)
                .HasForeignKey(r => r.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Notificacion>(e =>
        {
            e.HasOne(n => n.InstanciaDocumento)
                .WithMany()
                .HasForeignKey(n => n.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(n => n.Usuario)
                .WithMany()
                .HasForeignKey(n => n.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
