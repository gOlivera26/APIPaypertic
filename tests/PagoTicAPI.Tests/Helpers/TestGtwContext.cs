namespace PagoTicAPI.Tests.Helpers;

/// <summary>
/// Versión de gtwContext compatible con el proveedor InMemory de EF Core.
/// Omite configuraciones Oracle-específicas (schemas, collations, tipos de columna)
/// que no son soportadas por InMemory y que no son necesarias para las pruebas unitarias.
/// </summary>
public class TestGtwContext : gtwContext
{
    public TestGtwContext(DbContextOptions<gtwContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // GtwPago
        modelBuilder.Entity<GtwPago>(entity =>
        {
            entity.HasKey(e => e.PagId);
            entity.Property(e => e.PagId).ValueGeneratedOnAdd();

            entity.HasMany(e => e.GtwPagoDetalles)
                  .WithOne(e => e.PgdPag)
                  .HasForeignKey(e => e.PgdPagId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // GtwPagoDetalle
        modelBuilder.Entity<GtwPagoDetalle>(entity =>
        {
            entity.HasKey(e => e.PgdId);
            entity.Property(e => e.PgdId).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<PayPerTicPaymentWebhookInbox>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.JurisdictionId, e.DeduplicationKey }).IsUnique();
        });
    }
}
