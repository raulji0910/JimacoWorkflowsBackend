using Jimaco.Aprobaciones.Modelo.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jimaco.Aprobaciones.Modelo;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // SQL Server (`datetime2`) no guarda la zona horaria — al releer, EF Core marca cualquier
    // DateTime como Kind=Unspecified, aunque se haya guardado en UTC. System.Text.Json entonces
    // serializa esa fecha SIN el sufijo "Z", y el navegador la interpreta como hora LOCAL en vez
    // de UTC — para un usuario en Colombia (UTC-5), eso hace que todo se vea 5 horas adelantado.
    // Estos conversores fuerzan Kind=Utc al releer, para las columnas que de verdad representan
    // "en qué momento pasó esto" (no para fechas de calendario puras como FechaDocumento, que no
    // deben convertirse de zona horaria — esas siguen sin este conversor a propósito).
    private static readonly ValueConverter<DateTime, DateTime> ConversorFechaUtc = new(
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> ConversorFechaUtcNullable = new(
        v => v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<UsuarioRol> UsuarioRoles => Set<UsuarioRol>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<TipoDocumento> TiposDocumento => Set<TipoDocumento>();
    public DbSet<CampoTipoDocumento> CamposTipoDocumento => Set<CampoTipoDocumento>();
    public DbSet<DefinicionFlujo> DefinicionesFlujo => Set<DefinicionFlujo>();
    public DbSet<PasoFlujo> PasosFlujo => Set<PasoFlujo>();
    public DbSet<PasoFlujoRol> PasoFlujoRoles => Set<PasoFlujoRol>();
    public DbSet<PasoFlujoDestinoDevolucion> PasoFlujoDestinosDevolucion => Set<PasoFlujoDestinoDevolucion>();
    public DbSet<InstanciaDocumento> InstanciasDocumento => Set<InstanciaDocumento>();
    public DbSet<Adjunto> Adjuntos => Set<Adjunto>();
    public DbSet<HistorialAccion> HistorialAcciones => Set<HistorialAccion>();
    public DbSet<RenglonInstanciaDocumento> RenglonesInstanciaDocumento => Set<RenglonInstanciaDocumento>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();
    public DbSet<DocumentoVinculado> DocumentosVinculados => Set<DocumentoVinculado>();
    public DbSet<RenglonDocumentoVinculado> RenglonesDocumentoVinculado => Set<RenglonDocumentoVinculado>();
    public DbSet<RemisionRenglon> RemisionesRenglon => Set<RemisionRenglon>();
    public DbSet<HistorialDisponible> HistorialDisponible => Set<HistorialDisponible>();
    public DbSet<BorradorPagoProveedor> BorradoresPagoProveedor => Set<BorradorPagoProveedor>();
    public DbSet<RelacionPago> RelacionesPago => Set<RelacionPago>();
    public DbSet<LotePago> LotesPago => Set<LotePago>();
    public DbSet<SoporteLotePago> SoportesLotePago => Set<SoporteLotePago>();
    public DbSet<FacturaProveedor> FacturasProveedor => Set<FacturaProveedor>();
    public DbSet<FacturaProveedorRenglon> FacturaProveedorRenglones => Set<FacturaProveedorRenglon>();
    public DbSet<OrdenCompraWorldOffice> OrdenesCompraWorldOffice => Set<OrdenCompraWorldOffice>();
    public DbSet<OrdenCompraWorldOfficeRenglon> OrdenCompraWorldOfficeRenglones => Set<OrdenCompraWorldOfficeRenglon>();
    public DbSet<FacturaCompraWorldOffice> FacturasCompraWorldOffice => Set<FacturaCompraWorldOffice>();
    public DbSet<ObservacionOrdenCompra> ObservacionesOrdenCompra => Set<ObservacionOrdenCompra>();
    public DbSet<ObservacionFacturaProveedor> ObservacionesFacturaProveedor => Set<ObservacionFacturaProveedor>();
    public DbSet<SincronizacionConciliacion> SincronizacionConciliacion => Set<SincronizacionConciliacion>();
    public DbSet<TipoGiro> TiposGiro => Set<TipoGiro>();
    public DbSet<ConfiguracionCuentasPorPagar> ConfiguracionesCuentasPorPagar => Set<ConfiguracionCuentasPorPagar>();
    public DbSet<TerceroWO> TercerosWO => Set<TerceroWO>();
    public DbSet<ComprobanteEgreso> ComprobantesEgreso => Set<ComprobanteEgreso>();
    public DbSet<RenglonComprobanteEgreso> RenglonesComprobanteEgreso => Set<RenglonComprobanteEgreso>();
    public DbSet<PartidaCuentaPorPagar> PartidasCuentaPorPagar => Set<PartidaCuentaPorPagar>();
    public DbSet<AnticipoProveedor> AnticiposProveedor => Set<AnticipoProveedor>();
    public DbSet<SincronizacionCuentasPorPagar> SincronizacionesCuentasPorPagar => Set<SincronizacionCuentasPorPagar>();
    public DbSet<FacturaPagoProveedor> FacturasPagoProveedor => Set<FacturaPagoProveedor>();
    public DbSet<AnticipoPagoProveedor> AnticiposPagoProveedor => Set<AnticipoPagoProveedor>();
    public DbSet<CuentaBancoWO> CuentasBancoWO => Set<CuentaBancoWO>();
    public DbSet<PagoProveedor> PagosProveedor => Set<PagoProveedor>();
    // Catálogo de WO para crear documentos desde el portal (2026-11-25) — ver Entidades/CatalogoWO.cs.
    public DbSet<ProductoWO> ProductosWO => Set<ProductoWO>();
    public DbSet<BodegaWO> BodegasWO => Set<BodegaWO>();
    public DbSet<ExistenciaWO> ExistenciasWO => Set<ExistenciaWO>();
    public DbSet<DireccionTerceroWO> DireccionesTerceroWO => Set<DireccionTerceroWO>();
    public DbSet<FormaPagoWO> FormasPagoWO => Set<FormaPagoWO>();
    public DbSet<CampoPersonalizadoWO> CamposPersonalizadosWO => Set<CampoPersonalizadoWO>();
    public DbSet<SincronizacionCatalogoWO> SincronizacionCatalogoWO => Set<SincronizacionCatalogoWO>();
    public DbSet<Cotizacion> Cotizaciones => Set<Cotizacion>();
    public DbSet<CotizacionRenglon> CotizacionRenglones => Set<CotizacionRenglon>();

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
            e.Property(u => u.FechaCreacion).HasConversion(ConversorFechaUtc);
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

            e.Property(p => p.PermiteDevolverAlEmisor).HasDefaultValue(true);

            e.HasIndex(p => new { p.DefinicionFlujoId, p.Orden }).IsUnique();

            // Restrict — mismo motivo que PasoFlujoDestinoDevolucion (relación reflexiva).
            e.HasOne(p => p.PasoRedirigirAPagoContado)
                .WithMany()
                .HasForeignKey(p => p.PasoRedirigirAPagoContadoId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DefinicionFlujo>(e =>
        {
            e.Property(f => f.ValorMinimo).HasPrecision(18, 2);
            e.Property(f => f.ValorMaximo).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PasoFlujoDestinoDevolucion>(e =>
        {
            e.HasKey(d => new { d.PasoFlujoOrigenId, d.PasoFlujoDestinoId });

            // Restrict de los dos lados — Cascade en una relación reflexiva (mismo PasoFlujo del
            // lado origen y destino) tira "multiple cascade paths" en SQL Server; borrar un paso
            // que aparece como origen o destino de una devolución se maneja a mano en el servicio.
            e.HasOne(d => d.PasoFlujoOrigen)
                .WithMany(p => p.DestinosDevolucion)
                .HasForeignKey(d => d.PasoFlujoOrigenId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(d => d.PasoFlujoDestino)
                .WithMany()
                .HasForeignKey(d => d.PasoFlujoDestinoId)
                .OnDelete(DeleteBehavior.Restrict);
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
            e.Property(i => i.FechaCreacion).HasConversion(ConversorFechaUtc);
            e.Property(i => i.FechaEscrituraWO).HasConversion(ConversorFechaUtcNullable);
            e.HasIndex(i => new { i.Empresa, i.PrefijoOrigen, i.IdAsientoContableOrigen });

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

            // Restrict — Adjunto ya cascadea DESDE InstanciaDocumento (ver Entity<Adjunto> abajo);
            // esta es la relación inversa (cuál de esos adjuntos es el soporte), Cascade acá
            // generaría "multiple cascade paths" en SQL Server.
            e.HasOne(i => i.SoportePagoAdjunto)
                .WithMany()
                .HasForeignKey(i => i.SoportePagoAdjuntoId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(i => i.NumeroReferencia);
            e.HasIndex(i => i.Estado);
        });

        modelBuilder.Entity<Adjunto>(e =>
        {
            e.Property(a => a.FechaCarga).HasConversion(ConversorFechaUtc);

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
            e.Property(h => h.Fecha).HasConversion(ConversorFechaUtc);

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

        modelBuilder.Entity<DocumentoVinculado>(e =>
        {
            e.Property(d => d.FechaCreacion).HasConversion(ConversorFechaUtc);
            e.Property(d => d.UltimaAlertaEnviada).HasConversion(ConversorFechaUtcNullable);
            e.Property(d => d.FechaCompletada).HasConversion(ConversorFechaUtcNullable);
            // Hoja = (Empresa, Prefijo, Número): la COT 894 de Ferrealiados no es la de Jimaco.
            e.HasIndex(d => new { d.Empresa, d.PrefijoOrigen, d.NumeroReferencia });

            e.HasOne(d => d.InstanciaDocumento)
                .WithMany(i => i.DocumentosVinculados)
                .HasForeignKey(d => d.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RenglonDocumentoVinculado>(e =>
        {
            e.Property(r => r.Cantidad).HasPrecision(18, 3);
            e.Property(r => r.ValorUnitario).HasPrecision(18, 2);
            e.Property(r => r.PorcentajeIva).HasPrecision(5, 4);
            e.Property(r => r.Total).HasPrecision(18, 2);
            e.Property(r => r.CantidadDisponible).HasPrecision(18, 3);

            e.HasOne(r => r.DocumentoVinculado)
                .WithMany(d => d.Renglones)
                .HasForeignKey(r => r.DocumentoVinculadoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BorradorPagoProveedor>(e =>
        {
            e.Property(b => b.FechaActualizacion).HasConversion(ConversorFechaUtc);
            e.HasIndex(b => new { b.UsuarioId, b.Identificacion }).IsUnique();
        });

        modelBuilder.Entity<RelacionPago>(e =>
        {
            e.Property(r => r.Fecha).HasConversion(ConversorFechaUtc);
            e.HasIndex(r => r.Numero).IsUnique();
        });

        modelBuilder.Entity<LotePago>(e =>
        {
            e.Property(l => l.Fecha).HasConversion(ConversorFechaUtc);
            e.Property(l => l.Total).HasPrecision(18, 2);
            e.HasIndex(l => l.Numero).IsUnique();
            e.HasMany(l => l.Soportes).WithOne(s => s.LotePago).HasForeignKey(s => s.LotePagoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TipoGiro>(e =>
        {
            e.HasData(TiposGiroSemilla.Todos);
        });

        modelBuilder.Entity<ConfiguracionCuentasPorPagar>(e =>
        {
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.FechaActualizacion).HasConversion(ConversorFechaUtcNullable);
            e.HasOne(c => c.ActualizadoPorUsuario).WithMany().HasForeignKey(c => c.ActualizadoPorUsuarioId)
                .OnDelete(DeleteBehavior.NoAction);
            // Arranca apagada y con el prefijo de prueba (ver la entidad).
            e.HasData(new ConfiguracionCuentasPorPagar
            {
                Id = ConfiguracionCuentasPorPagar.IdUnico,
                PrefijoCE = ConfiguracionCuentasPorPagar.PrefijoCEPorDefecto,
                CrearCEEnWorldOffice = false
            });
        });

        modelBuilder.Entity<TerceroWO>(e =>
        {
            e.HasIndex(t => t.Identificacion);
            e.HasIndex(t => t.IdTerceroWO).IsUnique();
        });

        modelBuilder.Entity<HistorialDisponible>(e =>
        {
            e.Property(h => h.Fecha).HasConversion(ConversorFechaUtc);
            e.Property(h => h.CantidadAnterior).HasPrecision(18, 3);
            e.Property(h => h.CantidadNueva).HasPrecision(18, 3);
            e.Property(h => h.CantidadPorRestaurar).HasPrecision(18, 3);
            e.HasIndex(h => new { h.Empresa, h.PrefijoOrigen, h.NumeroReferencia, h.Codigo });
        });

        modelBuilder.Entity<RemisionRenglon>(e =>
        {
            e.Property(r => r.Fecha).HasConversion(ConversorFechaUtc);
            e.Property(r => r.CantidadRemisionada).HasPrecision(18, 3);

            e.HasOne(r => r.RenglonDocumentoVinculado)
                .WithMany(r => r.DespachosRemision)
                .HasForeignKey(r => r.RenglonDocumentoVinculadoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ComprobanteEgreso>(e =>
        {
            e.Property(c => c.Valor).HasPrecision(18, 2);

            e.HasOne(c => c.InstanciaDocumento)
                .WithOne(i => i.ComprobanteEgreso)
                .HasForeignKey<ComprobanteEgreso>(c => c.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RenglonComprobanteEgreso>(e =>
        {
            e.Property(r => r.Debito).HasPrecision(18, 2);
            e.Property(r => r.Credito).HasPrecision(18, 2);

            e.HasOne(r => r.ComprobanteEgreso)
                .WithMany(c => c.Renglones)
                .HasForeignKey(r => r.ComprobanteEgresoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---- Cuentas por pagar (2026-11-05) — Fecha/Vencimiento son fechas de calendario de WO
        // (sin hora, sin zona), igual que DocumentoVinculado.Fecha: sin conversión UTC. ----
        modelBuilder.Entity<PartidaCuentaPorPagar>(e =>
        {
            e.Property(p => p.Pendiente).HasPrecision(18, 2);
            e.HasIndex(p => p.Identificacion);
            e.HasIndex(p => p.IdDocumentoDetalleWO);
        });

        modelBuilder.Entity<AnticipoProveedor>(e =>
        {
            e.Property(a => a.Saldo).HasPrecision(18, 2);
            e.HasIndex(a => a.Identificacion);
            e.HasIndex(a => a.IdDocumentoDetalleWO);
        });

        modelBuilder.Entity<CuentaBancoWO>(e =>
        {
            e.HasIndex(b => b.IdCuentaContableWO).IsUnique();
        });

        modelBuilder.Entity<AnticipoPagoProveedor>(e =>
        {
            e.Property(a => a.Valor).HasPrecision(18, 2);
            e.Property(a => a.SaldoAnterior).HasPrecision(18, 2);
            e.HasIndex(a => a.IdDocumentoDetalleWO);

            e.HasOne(a => a.InstanciaDocumento)
                .WithMany(i => i.AnticiposPagoProveedor)
                .HasForeignKey(a => a.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PagoProveedor>(e =>
        {
            e.Property(p => p.NumeroCE).HasPrecision(20, 0);
            e.Property(p => p.FechaCreacionCE).HasConversion(ConversorFechaUtcNullable);
            e.HasIndex(p => p.InstanciaDocumentoId).IsUnique();
            e.HasIndex(p => p.PendienteCreacionCE);

            e.HasOne(p => p.InstanciaDocumento)
                .WithOne(i => i.PagoProveedor)
                .HasForeignKey<PagoProveedor>(p => p.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SincronizacionCuentasPorPagar>(e =>
        {
            e.Property(s => s.FechaCorte).HasConversion(ConversorFechaUtc);
        });

        modelBuilder.Entity<FacturaPagoProveedor>(e =>
        {
            e.Property(f => f.Valor).HasPrecision(18, 2);
            e.HasIndex(f => f.IdDocumentoDetalleWO);

            e.HasOne(f => f.InstanciaDocumento)
                .WithMany(i => i.FacturasPagoProveedor)
                .HasForeignKey(f => f.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Notificacion>(e =>
        {
            e.Property(n => n.FechaCreacion).HasConversion(ConversorFechaUtc);
            e.Property(n => n.FechaEnvio).HasConversion(ConversorFechaUtcNullable);

            e.HasOne(n => n.InstanciaDocumento)
                .WithMany()
                .HasForeignKey(n => n.InstanciaDocumentoId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(n => n.Usuario)
                .WithMany()
                .HasForeignKey(n => n.UsuarioId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ---- Conciliación de facturas de proveedor vs OC de WO (2026-11-24) ----

        modelBuilder.Entity<FacturaProveedor>(e =>
        {
            e.HasIndex(f => f.Cufe).IsUnique();
            e.HasIndex(f => f.FechaEmision);
            e.HasIndex(f => f.NitEmisor);
            e.Property(f => f.Origen).HasDefaultValue(OrigenFactura.Correo);
            e.Property(f => f.FechaRegistro).HasConversion(ConversorFechaUtc);
            e.Property(f => f.CorreoFecha).HasConversion(ConversorFechaUtcNullable);
        });

        modelBuilder.Entity<FacturaProveedorRenglon>(e =>
        {
            e.HasOne(r => r.FacturaProveedor)
                .WithMany(f => f.Renglones)
                .HasForeignKey(r => r.FacturaProveedorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrdenCompraWorldOffice>(e =>
        {
            e.HasIndex(o => o.IdAsientoContable).IsUnique();
            e.HasIndex(o => o.Fecha);
            e.HasIndex(o => o.NitProveedor);
            e.HasIndex(o => o.Numero);
            e.Property(o => o.FechaSincronizacion).HasConversion(ConversorFechaUtc);
        });

        modelBuilder.Entity<OrdenCompraWorldOfficeRenglon>(e =>
        {
            e.HasOne(r => r.OrdenCompraWorldOffice)
                .WithMany(o => o.Renglones)
                .HasForeignKey(r => r.OrdenCompraWorldOfficeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FacturaCompraWorldOffice>(e =>
        {
            e.HasIndex(f => f.IdAsientoContable).IsUnique();
            e.HasIndex(f => f.Fecha);
            e.HasIndex(f => f.NitProveedor);
            e.Property(f => f.FechaSincronizacion).HasConversion(ConversorFechaUtc);
        });

        modelBuilder.Entity<ObservacionOrdenCompra>(e =>
        {
            e.HasIndex(o => o.IdAsientoContable);
            e.Property(o => o.Fecha).HasConversion(ConversorFechaUtc);
        });

        modelBuilder.Entity<ObservacionFacturaProveedor>(e =>
        {
            e.HasIndex(o => o.FacturaProveedorId);
            e.Property(o => o.Fecha).HasConversion(ConversorFechaUtc);
            e.HasOne(o => o.FacturaProveedor)
                .WithMany()
                .HasForeignKey(o => o.FacturaProveedorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SincronizacionConciliacion>(e =>
        {
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.UltimaSincronizacionOrdenes).HasConversion(ConversorFechaUtcNullable);
        });

        // ---- Catálogo de WO (2026-11-25): espejo de lectura, las llaves son los Id de WO ----
        modelBuilder.Entity<ProductoWO>(e =>
        {
            e.HasKey(p => p.IdInventarioWO);
            e.HasIndex(p => p.Codigo);
            e.Property(p => p.FechaActualizacion).HasConversion(ConversorFechaUtc);
        });
        modelBuilder.Entity<BodegaWO>(e => e.HasKey(b => b.IdBodegaWO));
        modelBuilder.Entity<ExistenciaWO>(e => e.HasKey(x => new { x.IdInventarioWO, x.IdBodegaWO }));
        modelBuilder.Entity<DireccionTerceroWO>(e =>
        {
            e.HasKey(d => d.IdDireccionWO);
            e.HasIndex(d => d.IdTerceroWO);
        });
        modelBuilder.Entity<FormaPagoWO>(e => e.HasKey(f => f.IdFormaPagoWO));
        modelBuilder.Entity<CampoPersonalizadoWO>(e => e.HasIndex(c => new { c.TipoDocumento, c.Numero }).IsUnique());
        modelBuilder.Entity<SincronizacionCatalogoWO>(e =>
        {
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.FechaProductos).HasConversion(ConversorFechaUtcNullable);
            e.Property(s => s.FechaExistencias).HasConversion(ConversorFechaUtcNullable);
            e.Property(s => s.FechaCatalogos).HasConversion(ConversorFechaUtcNullable);
        });

        modelBuilder.Entity<Cotizacion>(e =>
        {
            e.HasIndex(c => new { c.UsuarioId, c.Estado });
            e.HasIndex(c => c.Estado);
            e.HasOne(c => c.Usuario).WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(c => c.Renglones).WithOne(r => r.Cotizacion).HasForeignKey(r => r.CotizacionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.FechaCreacion).HasConversion(ConversorFechaUtc);
            e.Property(c => c.FechaActualizacion).HasConversion(ConversorFechaUtc);
            e.Property(c => c.FechaEnvio).HasConversion(ConversorFechaUtcNullable);
            e.Property(c => c.FechaCreacionWO).HasConversion(ConversorFechaUtcNullable);
        });
    }
}
