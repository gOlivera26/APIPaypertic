using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PagoTicAPI.Domain.Context;
using PagoTicAPI.Domain.Context.Configuration;
using PagoTicAPI.Domain.Models.AutomaticDebits;
using PagoTicAPI.Domain.Models;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public sealed class AutomaticDebitDatabaseNamingTests
{
    [Fact]
    public void Model_UsesSpanishOracleIdentifiersAlignedWithRequirement()
    {
        using var context = CreateContext();
        var model = context.Model;

        AssertEntity<PayPerTicConfiguration>(model, "T_CREDENCIALES_PAYPERTIC", new Dictionary<string, string>
        {
            [nameof(PayPerTicConfiguration.Id)] = "ID_CREDENCIAL_PAYPERTIC",
            [nameof(PayPerTicConfiguration.AuthUrl)] = "URL_AUTENTICACION",
            [nameof(PayPerTicConfiguration.ApiUrl)] = "URL_API",
            [nameof(PayPerTicConfiguration.Username)] = "USUARIO",
            [nameof(PayPerTicConfiguration.Password)] = "CLAVE",
            [nameof(PayPerTicConfiguration.ClientId)] = "ID_CLIENTE",
            [nameof(PayPerTicConfiguration.ClientSecret)] = "SECRETO_CLIENTE",
            [nameof(PayPerTicConfiguration.CollectorId)] = "ID_RECAUDADOR",
            [nameof(PayPerTicConfiguration.NotificationUrl)] = "URL_NOTIFICACION",
            [nameof(PayPerTicConfiguration.ReturnUrl)] = "URL_RETORNO",
            [nameof(PayPerTicConfiguration.BackUrl)] = "URL_REGRESO"
        });

        AssertEntity<AutomaticDebitAdhesion>(model, "T_DEBITOS_AUTOMATICOS", new Dictionary<string, string>
        {
            [nameof(AutomaticDebitAdhesion.Id)] = "ID_DEBITO_AUTOMATICO",
            [nameof(AutomaticDebitAdhesion.ProviderAdhesionId)] = "ID_SUSCRIPCION_PPT",
            [nameof(AutomaticDebitAdhesion.ExternalReference)] = "REFERENCIA_EXTERNA",
            [nameof(AutomaticDebitAdhesion.FormUrl)] = "URL_FORMULARIO",
            [nameof(AutomaticDebitAdhesion.DeactivatedBy)] = "USR_BAJA",
            [nameof(AutomaticDebitAdhesion.DeactivatedAt)] = "FEC_BAJA"
        });

        AssertEntity<AutomaticDebitOperation>(model, "T_DEBITOS_AUTOMATICOS_DET", new Dictionary<string, string>
        {
            [nameof(AutomaticDebitOperation.Id)] = "ID_DEBITO_AUTOMATICO_DET",
            [nameof(AutomaticDebitOperation.AdhesionId)] = "ID_DEBITO_AUTOMATICO",
            [nameof(AutomaticDebitOperation.ExternalTransactionId)] = "ID_TRANSACCION_EXTERNA",
            [nameof(AutomaticDebitOperation.ApprovedAt)] = "FECHA_APROBACION",
            [nameof(AutomaticDebitOperation.RejectedAt)] = "FECHA_RECHAZO"
        });

        AssertEntity<AutomaticDebitEvent>(model, "T_DEB_AUT_EVENTOS", new Dictionary<string, string>
        {
            [nameof(AutomaticDebitEvent.Id)] = "ID_EVENTO_DEB_AUT",
            [nameof(AutomaticDebitEvent.DeduplicationKey)] = "CLAVE_DEDUPLICACION",
            [nameof(AutomaticDebitEvent.ProviderObjectId)] = "ID_OBJETO_PPT",
            [nameof(AutomaticDebitEvent.EventType)] = "TIPO_EVENTO"
        });

        AssertEntity<AutomaticDebitWebhookInbox>(model, "T_DEB_AUT_NOTIFICACIONES", new Dictionary<string, string>
        {
            [nameof(AutomaticDebitWebhookInbox.Id)] = "ID_NOTIFICACION_DEB_AUT",
            [nameof(AutomaticDebitWebhookInbox.ObjectType)] = "TIPO_OBJETO",
            [nameof(AutomaticDebitWebhookInbox.PayloadHash)] = "HASH_CONTENIDO",
            [nameof(AutomaticDebitWebhookInbox.RawPayload)] = "CONTENIDO_ORIGINAL",
            [nameof(AutomaticDebitWebhookInbox.SanitizedMetadata)] = "METADATOS_SANITIZADOS"
        });
    }

    [Fact]
    public void DatabaseIdentifiers_DoNotExceedOracleThirtyCharacterLimit()
    {
        using var context = CreateContext();
        var identifiers = GetAutomaticDebitEntityTypes(context.Model)
            .SelectMany(entity => GetIdentifiers(entity))
            .Concat(AutomaticDebitDatabaseNames.Sequences.All);

        identifiers.Should().OnlyContain(identifier => identifier.Length <= 30);
    }

    [Fact]
    public void InternalStates_ArePersistedWithTheExistingUppercaseOracleValues()
    {
        using var context = CreateContext();

        AssertConversion<AutomaticDebitOperation, AutomaticDebitProcessingStates>(
            context.Model, nameof(AutomaticDebitOperation.ProcessingState),
            AutomaticDebitProcessingStates.Pending, "PENDING");
        AssertConversion<AutomaticDebitWebhookInbox, AutomaticDebitInboxResults>(
            context.Model, nameof(AutomaticDebitWebhookInbox.Result),
            AutomaticDebitInboxResults.Processed, "PROCESSED");
        AssertConversion<PayPerTicPaymentWebhookInbox, PayPerTicPaymentWebhookInboxResults>(
            context.Model, nameof(PayPerTicPaymentWebhookInbox.Result),
            PayPerTicPaymentWebhookInboxResults.Failed, "FAILED");
    }

    [Fact]
    public void FoundationScript_UsesTheSameSpanishTablesAndSequences()
    {
        var sql = File.ReadAllText(FindRepositoryFile("database", "automatic-debits", "001_foundation.sql"));
        using var context = CreateContext();

        foreach (var table in AutomaticDebitDatabaseNames.Tables.All)
        {
            sql.Should().Contain($"GATEWAY.{table}");
        }

        foreach (var sequence in AutomaticDebitDatabaseNames.Sequences.All)
        {
            sql.Should().Contain($"GATEWAY.{sequence}");
        }

        foreach (var identifier in GetAutomaticDebitEntityTypes(context.Model).SelectMany(GetIdentifiers).Distinct())
        {
            sql.Should().Contain(identifier);
        }

        sql.Should().NotContain("T_KEYS_PAYPERTIC");
        sql.Should().NotContain("GTW_PPT_");
        sql.Should().NotContain("EXTERNAL_REFERENCE");
        sql.Should().NotContain("FORM_URL");
        sql.Should().NotContain("WEBHOOK");
    }

    [Fact]
    public void DeploymentScripts_DoNotReferenceRetiredDatabaseIdentifiers()
    {
        var scriptDirectory = Path.GetDirectoryName(
            FindRepositoryFile("database", "automatic-debits", "001_foundation.sql"))!;
        var sql = string.Join(Environment.NewLine, Directory.GetFiles(scriptDirectory, "*.sql").Select(File.ReadAllText));
        var retiredIdentifiers = new[]
        {
            "T_KEYS_PAYPERTIC", "GTW_PPT_", "ID_KEY_PAYPERTIC", "AUTH_URL", "API_URL",
            "USERNAME", "PASSWORD", "CLIENT_ID", "CLIENT_SECRET", "COLLECTOR_ID",
            "NOTIFICATION_URL", "RETURN_URL", "BACK_URL", "EXTERNAL_REFERENCE", "FORM_URL",
            "EXTERNAL_TRANSACTION_ID", "DEDUPLICATION_KEY", "PROVIDER_OBJECT_ID", "EVENT_TYPE",
            "AUTHORITATIVE_HASH", "AUTHORITATIVE_UPDATED_AT", "OCCURRED_AT", "OBJECT_TYPE",
            "PAYLOAD_HASH", "RAW_PAYLOAD", "SANITIZED_METADATA", "RECEIVED_AT", "PROCESSED_AT"
        };

        foreach (var retiredIdentifier in retiredIdentifiers)
        {
            sql.Should().NotContain(retiredIdentifier);
        }
    }

    private static gtwContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<gtwContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new gtwContext(options);
    }

    private static void AssertEntity<TEntity>(IModel model, string tableName, IReadOnlyDictionary<string, string> columns)
        where TEntity : class
    {
        var entity = model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"No se encontró el mapeo de {typeof(TEntity).Name}.");
        entity.GetTableName().Should().Be(tableName);
        entity.GetSchema().Should().Be("GATEWAY");

        var storeObject = StoreObjectIdentifier.Table(tableName, "GATEWAY");
        foreach (var (propertyName, columnName) in columns)
        {
            entity.FindProperty(propertyName)!.GetColumnName(storeObject).Should().Be(columnName);
        }
    }

    private static void AssertConversion<TEntity, TEnum>(
        IModel model, string propertyName, TEnum modelValue, string providerValue)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var property = model.FindEntityType(typeof(TEntity))!.FindProperty(propertyName)!;
        var converter = property.GetValueConverter()!;

        converter.ConvertToProvider(modelValue).Should().Be(providerValue);
        converter.ConvertFromProvider(providerValue).Should().Be(modelValue);
    }

    private static IEnumerable<IReadOnlyEntityType> GetAutomaticDebitEntityTypes(IModel model)
    {
        var types = new[]
        {
            typeof(PayPerTicConfiguration),
            typeof(AutomaticDebitAdhesion),
            typeof(AutomaticDebitOperation),
            typeof(AutomaticDebitEvent),
            typeof(AutomaticDebitWebhookInbox)
        };

        return types.Select(type => model.FindEntityType(type)
            ?? throw new InvalidOperationException($"No se encontró el mapeo de {type.Name}."));
    }

    private static IEnumerable<string> GetIdentifiers(IReadOnlyEntityType entity)
    {
        if (entity.GetTableName() is { } tableName)
        {
            yield return tableName;
        }

        if (entity.FindPrimaryKey()?.GetName() is { } primaryKey)
        {
            yield return primaryKey;
        }

        foreach (var index in entity.GetIndexes())
        {
            if (index.GetDatabaseName() is { } indexName)
            {
                yield return indexName;
            }
        }

        foreach (var foreignKey in entity.GetForeignKeys())
        {
            if (foreignKey.GetConstraintName() is { } constraintName)
            {
                yield return constraintName;
            }
        }

        foreach (var property in entity.GetProperties())
        {
            yield return property.GetColumnName();
        }
    }

    private static string FindRepositoryFile(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"No se encontró {Path.Combine(pathSegments)} desde {AppContext.BaseDirectory}.");
    }
}

