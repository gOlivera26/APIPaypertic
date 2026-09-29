using Microsoft.EntityFrameworkCore;
using PagoTicAPI.Domain.Models;
using PagoTicAPI.Domain.Context.Configuration;

namespace PagoTicAPI.Domain.Context;

public partial class gtwContext
{
    public virtual DbSet<PayPerTicPaymentWebhookInbox> PayPerTicPaymentWebhookInbox { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureAutomaticDebits(modelBuilder);

        modelBuilder.Entity<PayPerTicPaymentWebhookInbox>(entity =>
        {
            entity.HasKey(x => x.Id).HasName("PK_PAGO_PPT_NOTIF");
            entity.ToTable("T_PAGO_PPT_NOTIFICACIONES");
            entity.HasIndex(x => new { x.JurisdictionId, x.DeduplicationKey })
                .IsUnique()
                .HasDatabaseName("UK_PAGO_PPT_NOTIF_DEDUP");
            entity.HasIndex(x => new { x.JurisdictionId, x.ProviderPaymentId })
                .HasDatabaseName("IDX_PAGO_PPT_NOTIF_PAGO");

            entity.Property(x => x.Id).HasColumnName("ID_NOTIFICACION_PAGO_PPT").ValueGeneratedNever();
            entity.Property(x => x.JurisdictionId).HasColumnName("ID_JURISDICCION");
            entity.Property(x => x.PaymentId).HasColumnName("PAG_ID");
            entity.Property(x => x.ProviderPaymentId).HasColumnName("ID_PAGO_PPT").HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.DeduplicationKey).HasColumnName("CLAVE_DEDUPLICACION").HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.PayloadHash).HasColumnName("HASH_CONTENIDO").HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.Result)
                .HasConversion(value => InternalStateConversions.ToDatabase(value), value => InternalStateConversions.ToPaymentInboxResult(value))
                .HasColumnName("RESULTADO").HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.Attempts).HasColumnName("CANTIDAD_INTENTOS");
            entity.Property(x => x.FailureCode).HasColumnName("CODIGO_FALLO").HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.ReceivedAt).HasColumnName("FECHA_RECEPCION");
            entity.Property(x => x.LastAttemptAt).HasColumnName("FECHA_ULTIMO_INTENTO");
            entity.Property(x => x.ProcessedAt).HasColumnName("FECHA_PROCESAMIENTO");

            entity.HasOne<GtwPago>()
                .WithMany()
                .HasForeignKey(x => x.PaymentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_PAGO_PPT_NOTIF_PAGO");
        });
    }
}
