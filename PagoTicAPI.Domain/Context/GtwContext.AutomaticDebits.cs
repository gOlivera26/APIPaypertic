using Microsoft.EntityFrameworkCore;
using PagoTicAPI.Domain.Models.AutomaticDebits;
using PagoTicAPI.Domain.Context.Configuration;

namespace PagoTicAPI.Domain.Context;

public partial class gtwContext
{
    public virtual DbSet<PayPerTicConfiguration> PayPerTicConfigurations => Set<PayPerTicConfiguration>();
    public virtual DbSet<AutomaticDebitAdhesion> AutomaticDebitAdhesions => Set<AutomaticDebitAdhesion>();
    public virtual DbSet<AutomaticDebitOperation> AutomaticDebitOperations => Set<AutomaticDebitOperation>();
    public virtual DbSet<AutomaticDebitEvent> AutomaticDebitEvents => Set<AutomaticDebitEvent>();
    public virtual DbSet<AutomaticDebitWebhookInbox> AutomaticDebitWebhookInbox => Set<AutomaticDebitWebhookInbox>();

    private static void ConfigureAutomaticDebits(ModelBuilder modelBuilder)
    {
        ConfigurePayPerTicConfiguration(modelBuilder);
        ConfigureAdhesion(modelBuilder);
        ConfigureOperation(modelBuilder);
        ConfigureEvent(modelBuilder);
        ConfigureWebhookInbox(modelBuilder);
    }

    private static void ConfigurePayPerTicConfiguration(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PayPerTicConfiguration>();
        entity.ToTable(AutomaticDebitDatabaseNames.Tables.Credentials);
        entity.HasKey(x => x.Id).HasName("PK_CREDENCIALES_PPT");
        entity.HasIndex(x => new { x.JurisdictionId, x.IsActive }).HasDatabaseName("IDX_CRED_PPT_JUR_ACT");
        entity.Property(x => x.Id).HasColumnName("ID_CREDENCIAL_PAYPERTIC").ValueGeneratedNever();
        entity.Property(x => x.JurisdictionId).HasColumnName("ID_JURISDICCION");
        entity.Property(x => x.AuthUrl).HasColumnName("URL_AUTENTICACION").HasMaxLength(500).IsUnicode(false);
        entity.Property(x => x.ApiUrl).HasColumnName("URL_API").HasMaxLength(500).IsUnicode(false);
        entity.Property(x => x.Username).HasColumnName("USUARIO").HasMaxLength(250).IsUnicode(false);
        entity.Property(x => x.Password).HasColumnName("CLAVE").HasMaxLength(1000).IsUnicode(false);
        entity.Property(x => x.ClientId).HasColumnName("ID_CLIENTE").HasMaxLength(250).IsUnicode(false);
        entity.Property(x => x.ClientSecret).HasColumnName("SECRETO_CLIENTE").HasMaxLength(1000).IsUnicode(false);
        entity.Property(x => x.CollectorId).HasColumnName("ID_RECAUDADOR").HasMaxLength(250).IsUnicode(false);
        entity.Property(x => x.NotificationUrl).HasColumnName("URL_NOTIFICACION").HasMaxLength(500).IsUnicode(false);
        entity.Property(x => x.ReturnUrl).HasColumnName("URL_RETORNO").HasMaxLength(500).IsUnicode(false);
        entity.Property(x => x.BackUrl).HasColumnName("URL_REGRESO").HasMaxLength(500).IsUnicode(false);
        entity.Property(x => x.IsActive).HasColumnName("ACTIVO").HasMaxLength(1)
            .HasConversion(value => value ? "S" : "N", value => value == "S");
        entity.Property(x => x.CreatedBy).HasColumnName("USR_ING").HasMaxLength(50).IsUnicode(false);
        entity.Property(x => x.CreatedAt).HasColumnName("FEC_ING");
        entity.Property(x => x.ModifiedBy).HasColumnName("USR_MOD").HasMaxLength(50).IsUnicode(false);
        entity.Property(x => x.ModifiedAt).HasColumnName("FEC_MOD");
    }

    private static void ConfigureAdhesion(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AutomaticDebitAdhesion>();
        entity.ToTable(AutomaticDebitDatabaseNames.Tables.Adhesions);
        entity.HasKey(x => x.Id).HasName("PK_DEBITOS_AUT");
        entity.HasIndex(x => new { x.JurisdictionId, x.TaxpayerAccountId }).HasDatabaseName("IDX_DEB_AUT_JUR_CTA");
        entity.HasIndex(x => new { x.JurisdictionId, x.ProviderAdhesionId }).IsUnique().HasDatabaseName("UK_DEB_AUT_SUSC");
        entity.Property(x => x.Id).HasColumnName("ID_DEBITO_AUTOMATICO").ValueGeneratedNever();
        entity.Property(x => x.JurisdictionId).HasColumnName("ID_JURISDICCION");
        entity.Property(x => x.TaxpayerAccountId).HasColumnName("ID_TRIBUTO_CONTRIBUYENTE").HasMaxLength(50).IsUnicode(false);
        entity.Property(x => x.PersonId).HasColumnName("ID_PERSONA").HasMaxLength(10).IsUnicode(false);
        entity.Property(x => x.ProviderAdhesionId).HasColumnName("ID_SUSCRIPCION_PPT").HasMaxLength(100).IsUnicode(false);
        entity.Property(x => x.ExternalReference).HasColumnName("REFERENCIA_EXTERNA").HasMaxLength(150).IsUnicode(false);
        entity.Property(x => x.FormUrl).HasColumnName("URL_FORMULARIO").HasMaxLength(2000).IsUnicode(false);
        entity.Property(x => x.ProviderState).HasColumnName("ESTADO_PPT").HasMaxLength(30).IsUnicode(false);
        entity.Property(x => x.ProcessingState)
            .HasConversion(value => InternalStateConversions.ToDatabase(value), value => InternalStateConversions.ToProcessingState(value))
            .HasColumnName("ESTADO_PROCESAMIENTO").HasMaxLength(30).IsUnicode(false);
        entity.Property(x => x.PaymentMethod).HasColumnName("MEDIO_PAGO").HasMaxLength(100).IsUnicode(false);
        entity.Property(x => x.LastDigits).HasColumnName("ULTIMOS_DIGITOS").HasMaxLength(10).IsUnicode(false);
        entity.Property(x => x.RequestedAt).HasColumnName("FECHA_SOLICITUD");
        entity.Property(x => x.ActivatedAt).HasColumnName("FECHA_ACTIVACION");
        entity.Property(x => x.CancelledAt).HasColumnName("FECHA_CANCELACION");
        entity.Property(x => x.CancellationReason).HasColumnName("MOTIVO_CANCELACION").HasMaxLength(1000).IsUnicode(false);
        MapAudit(entity);
        entity.Property(x => x.DeactivatedBy).HasColumnName("USR_BAJA").HasMaxLength(50).IsUnicode(false);
        entity.Property(x => x.DeactivatedAt).HasColumnName("FEC_BAJA");
    }

    private static void ConfigureOperation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AutomaticDebitOperation>();
        entity.ToTable(AutomaticDebitDatabaseNames.Tables.Operations);
        entity.HasKey(x => x.Id).HasName("PK_DEBITOS_AUT_DET");
        entity.HasIndex(x => x.AdhesionId).HasDatabaseName("IDX_DEB_AUT_DET_CAB");
        entity.HasIndex(x => new { x.JurisdictionId, x.ExternalTransactionId }).IsUnique().HasDatabaseName("UK_DEB_AUT_DET_EXT");
        entity.HasIndex(x => new { x.JurisdictionId, x.ProviderTransactionId }).IsUnique().HasDatabaseName("UK_DEB_AUT_DET_PPT");
        entity.Property(x => x.Id).HasColumnName("ID_DEBITO_AUTOMATICO_DET").ValueGeneratedNever();
        entity.Property(x => x.AdhesionId).HasColumnName("ID_DEBITO_AUTOMATICO");
        entity.Property(x => x.JurisdictionId).HasColumnName("ID_JURISDICCION");
        entity.Property(x => x.TaxpayerAccountId).HasColumnName("ID_TRIBUTO_CONTRIBUYENTE").HasMaxLength(50).IsUnicode(false);
        entity.Property(x => x.ObligationId).HasColumnName("ID_OBLIGACION").HasMaxLength(50).IsUnicode(false);
        entity.Property(x => x.ProviderTransactionId).HasColumnName("ID_TRANSACCION_PPT").HasMaxLength(100).IsUnicode(false).IsConcurrencyToken();
        entity.Property(x => x.ExternalTransactionId).HasColumnName("ID_TRANSACCION_EXTERNA").HasMaxLength(150).IsUnicode(false);
        entity.Property(x => x.Amount).HasColumnName("IMPORTE").HasPrecision(19, 5);
        entity.Property(x => x.DueDate).HasColumnName("FECHA_VENCIMIENTO");
        entity.Property(x => x.ProviderState).HasColumnName("ESTADO_PPT").HasMaxLength(30).IsUnicode(false).IsConcurrencyToken();
        entity.Property(x => x.ProcessingState)
            .HasConversion(value => InternalStateConversions.ToDatabase(value), value => InternalStateConversions.ToProcessingState(value))
            .HasColumnName("ESTADO_PROCESAMIENTO").HasMaxLength(30).IsUnicode(false);
        entity.Property(x => x.RequestedAt).HasColumnName("FECHA_SOLICITUD");
        entity.Property(x => x.ProcessedAt).HasColumnName("FECHA_PROCESAMIENTO");
        entity.Property(x => x.ApprovedAt).HasColumnName("FECHA_APROBACION");
        entity.Property(x => x.RejectedAt).HasColumnName("FECHA_RECHAZO");
        entity.Property(x => x.RejectionReason).HasColumnName("MOTIVO_RECHAZO").HasMaxLength(1000).IsUnicode(false);
        MapAudit(entity);
        entity.HasOne(x => x.Adhesion).WithMany().HasForeignKey(x => x.AdhesionId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_DEB_AUT_DET_CAB");
    }

    private static void ConfigureEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AutomaticDebitEvent>();
        entity.ToTable(AutomaticDebitDatabaseNames.Tables.Events);
        entity.HasKey(x => x.Id).HasName("PK_DEB_AUT_EVENTOS");
        entity.HasIndex(x => new { x.JurisdictionId, x.DeduplicationKey }).IsUnique().HasDatabaseName("UK_DEB_AUT_EVENTO_DEDUP");
        entity.Property(x => x.Id).HasColumnName("ID_EVENTO_DEB_AUT").ValueGeneratedNever();
        entity.Property(x => x.JurisdictionId).HasColumnName("ID_JURISDICCION");
        entity.Property(x => x.AdhesionId).HasColumnName("ID_DEBITO_AUTOMATICO");
        entity.Property(x => x.OperationId).HasColumnName("ID_DEBITO_AUTOMATICO_DET");
        entity.Property(x => x.DeduplicationKey).HasColumnName("CLAVE_DEDUPLICACION").HasMaxLength(64).IsUnicode(false);
        entity.Property(x => x.ProviderObjectId).HasColumnName("ID_OBJETO_PPT").HasMaxLength(150).IsUnicode(false);
        entity.Property(x => x.EventType).HasColumnName("TIPO_EVENTO").HasMaxLength(100).IsUnicode(false);
        entity.Property(x => x.ProviderState).HasColumnName("ESTADO_PPT").HasMaxLength(30).IsUnicode(false);
        entity.Property(x => x.AuthoritativeHash).HasColumnName("HASH_AUTORITATIVO").HasMaxLength(64).IsUnicode(false);
        entity.Property(x => x.AuthoritativeUpdatedAt).HasColumnName("FECHA_ACTUALIZACION_PPT");
        entity.Property(x => x.OccurredAt).HasColumnName("FECHA_EVENTO");
        entity.Property(x => x.CreatedAt).HasColumnName("FEC_ING");
    }

    private static void ConfigureWebhookInbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AutomaticDebitWebhookInbox>();
        entity.ToTable(AutomaticDebitDatabaseNames.Tables.Notifications);
        entity.HasKey(x => x.Id).HasName("PK_DEB_AUT_NOTIF");
        entity.HasIndex(x => new { x.JurisdictionId, x.DeduplicationKey }).IsUnique().HasDatabaseName("UK_DEB_AUT_NOTIF_DEDUP");
        entity.Property(x => x.Id).HasColumnName("ID_NOTIFICACION_DEB_AUT").ValueGeneratedNever();
        entity.Property(x => x.JurisdictionId).HasColumnName("ID_JURISDICCION");
        entity.Property(x => x.DeduplicationKey).HasColumnName("CLAVE_DEDUPLICACION").HasMaxLength(150).IsUnicode(false);
        entity.Property(x => x.ProviderObjectId).HasColumnName("ID_OBJETO_PPT").HasMaxLength(150).IsUnicode(false);
        entity.Property(x => x.ObjectType).HasColumnName("TIPO_OBJETO").HasMaxLength(30).IsUnicode(false);
        entity.Property(x => x.PayloadHash).HasColumnName("HASH_CONTENIDO").HasMaxLength(64).IsUnicode(false);
        entity.Property(x => x.RawPayload).HasColumnName("CONTENIDO_ORIGINAL").HasColumnType("CLOB");
        entity.Property(x => x.AuthoritativeHash).HasColumnName("HASH_AUTORITATIVO").HasMaxLength(64).IsUnicode(false);
        entity.Property(x => x.AuthoritativeUpdatedAt).HasColumnName("FECHA_ACTUALIZACION_PPT");
        entity.Property(x => x.SanitizedMetadata).HasColumnName("METADATOS_SANITIZADOS").HasMaxLength(2000).IsUnicode(false);
        entity.Property(x => x.Result)
            .HasConversion(value => InternalStateConversions.ToDatabase(value), value => InternalStateConversions.ToAutomaticDebitInboxResult(value))
            .HasColumnName("RESULTADO").HasMaxLength(30).IsUnicode(false);
        entity.Property(x => x.ReceivedAt).HasColumnName("FECHA_RECEPCION");
        entity.Property(x => x.ProcessedAt).HasColumnName("FECHA_PROCESAMIENTO");
    }

    private static void MapAudit<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity)
        where T : class
    {
        entity.Property<string>("CreatedBy").HasColumnName("USR_ING").HasMaxLength(50).IsUnicode(false);
        entity.Property<DateTime>("CreatedAt").HasColumnName("FEC_ING");
        entity.Property<string?>("ModifiedBy").HasColumnName("USR_MOD").HasMaxLength(50).IsUnicode(false);
        entity.Property<DateTime?>("ModifiedAt").HasColumnName("FEC_MOD");
    }
}

