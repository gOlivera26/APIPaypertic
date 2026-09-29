namespace PagoTicAPI.Application.Webhooks.AutomaticDebits;

public sealed class AutomaticDebitWebhookProcessor : IAutomaticDebitWebhookProcessor
{
    private readonly gtwContext _context;
    private readonly IJurisdictionPayPerTicConfigurationProvider _configurationProvider;
    private readonly IAutomaticDebitPayPerTicClient _client;
    private readonly IAutomaticDebitWebhookIdGenerator _idGenerator;
    private readonly AutomaticDebitFeatureOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutomaticDebitWebhookProcessor> _logger;

    public AutomaticDebitWebhookProcessor(
        gtwContext context,
        IJurisdictionPayPerTicConfigurationProvider configurationProvider,
        IAutomaticDebitPayPerTicClient client,
        IAutomaticDebitWebhookIdGenerator idGenerator,
        IOptions<AutomaticDebitFeatureOptions> options,
        TimeProvider timeProvider,
        ILogger<AutomaticDebitWebhookProcessor> logger)
    {
        _context = context;
        _configurationProvider = configurationProvider;
        _client = client;
        _idGenerator = idGenerator;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AutomaticDebitWebhookProcessingResult> ProcessAsync(
        long jurisdictionId,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return AutomaticDebitWebhookProcessingResult.Rejected(404, "feature_disabled");
        }

        if (jurisdictionId <= 0)
        {
            return AutomaticDebitWebhookProcessingResult.Rejected(400, "invalid_jurisdiction");
        }

        var envelope = AutomaticDebitWebhookEnvelope.Create(rawBody);
        if (envelope.Hint is null)
        {
            _logger.LogWarning(
                "Webhook de débito automático rechazado por formato inválido. JurisdictionId={JurisdictionId} PayloadHash={PayloadHash}",
                jurisdictionId,
                envelope.PayloadHash);
            return AutomaticDebitWebhookProcessingResult.Rejected(400, "malformed_hint");
        }

        var operation = envelope.Hint.ObjectType == AutomaticDebitWebhookObjectTypes.Payment
            ? await _context.AutomaticDebitOperations.FirstOrDefaultAsync(x =>
                x.JurisdictionId == jurisdictionId &&
                x.ProviderTransactionId == envelope.Hint.ProviderId,
                cancellationToken)
            : null;
        var adhesion = envelope.Hint.ObjectType == AutomaticDebitWebhookObjectTypes.Subscription
            ? await _context.AutomaticDebitAdhesions.FirstOrDefaultAsync(x =>
                x.JurisdictionId == jurisdictionId &&
                x.ProviderAdhesionId == envelope.Hint.ProviderId,
                cancellationToken)
            : null;
        if (operation is null && adhesion is null)
        {
            _logger.LogWarning(
                "El webhook de débito automático referencia un objeto local desconocido. JurisdictionId={JurisdictionId} ProviderObjectId={ProviderObjectId} ObjectType={ObjectType}",
                jurisdictionId,
                envelope.Hint.ProviderId,
                envelope.Hint.ObjectType);
            return AutomaticDebitWebhookProcessingResult.Rejected(
                404,
                envelope.Hint.ObjectType == AutomaticDebitWebhookObjectTypes.Payment
                    ? "operation_not_found"
                    : "adhesion_not_found");
        }

        JurisdictionPayPerTicConfiguration configuration;
        try
        {
            configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        }
        catch (PayPerTicConfigurationNotFoundException)
        {
            return AutomaticDebitWebhookProcessingResult.Rejected(404, "jurisdiction_not_configured");
        }

        AutomaticDebitProviderReadBack readBack;
        try
        {
            readBack = envelope.Hint.ObjectType == AutomaticDebitWebhookObjectTypes.Payment
                ? await _client.GetPaymentReadBackAsync(jurisdictionId, envelope.Hint.ProviderId, cancellationToken)
                : await _client.GetAdhesionReadBackAsync(jurisdictionId, envelope.Hint.ProviderId, cancellationToken);
        }
        catch (AutomaticDebitProviderException exception)
        {
            _logger.LogWarning(
                "Falló la consulta autoritativa del webhook de débito automático para la jurisdicción {JurisdictionId}, con estado del proveedor {StatusCode}.",
                jurisdictionId,
                exception.StatusCode);
            return AutomaticDebitWebhookProcessingResult.Rejected(503, "read_back_unavailable");
        }

        if (!MatchesProviderEnvelope(configuration, envelope.Hint, readBack))
        {
            _logger.LogWarning(
                "Webhook de débito automático rechazado por discrepancia con el estado autoritativo. JurisdictionId={JurisdictionId} ProviderObjectId={ProviderObjectId} ObjectType={ObjectType}",
                jurisdictionId, envelope.Hint.ProviderId, envelope.Hint.ObjectType);
            return AutomaticDebitWebhookProcessingResult.Rejected(401, "authoritative_mismatch");
        }

        try
        {
            return envelope.Hint.ObjectType == AutomaticDebitWebhookObjectTypes.Payment
                ? await ProcessPaymentAsync(jurisdictionId, envelope, readBack, operation!, cancellationToken)
                : await ProcessSubscriptionAsync(jurisdictionId, envelope, readBack, adhesion!, cancellationToken);
        }
        catch (AutomaticDebitProviderException)
        {
            return AutomaticDebitWebhookProcessingResult.Rejected(422, "unsupported_authoritative_state");
        }
    }

    private async Task<AutomaticDebitWebhookProcessingResult> ProcessPaymentAsync(
        long jurisdictionId,
        AutomaticDebitWebhookEnvelope envelope,
        AutomaticDebitProviderReadBack readBack,
        AutomaticDebitOperation operation,
        CancellationToken cancellationToken)
    {
        var state = NormalizePaymentState(readBack.Status);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!string.Equals(operation.ExternalTransactionId, readBack.ExternalReference, StringComparison.Ordinal) ||
                !string.Equals(operation.ProviderTransactionId, readBack.Id, StringComparison.Ordinal))
                return AutomaticDebitWebhookProcessingResult.Rejected(401, "authoritative_mismatch");

            try
            {
                return await PersistAcceptedAsync(
                    jurisdictionId, envelope, readBack, operation.AdhesionId, operation.Id,
                    () =>
                    {
                        if (!AutomaticDebitProviderStateTransitionPolicy.CanApplyPayment(operation.ProviderState, state))
                            return;
                        operation.ProviderState = state;
                        operation.ProcessingState = AutomaticDebitProcessingStates.Pending;
                        operation.ModifiedBy = "AUTOMATIC_DEBIT_WEBHOOK";
                        operation.ModifiedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    }, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                operation = await _context.AutomaticDebitOperations.FirstOrDefaultAsync(
                    x => x.Id == operation.Id && x.JurisdictionId == jurisdictionId, cancellationToken)
                    ?? throw new DbUpdateConcurrencyException("The automatic-debit operation disappeared during webhook recovery.");
            }
            catch (DbUpdateException exception) when (
                attempt < 2 && (AutomaticDebitEventDeduplication.IsExpectedCollision(exception) ||
                                AutomaticDebitEventDeduplication.IsInboxCollision(exception)))
            {
                operation = await _context.AutomaticDebitOperations.FirstOrDefaultAsync(
                    x => x.Id == operation.Id && x.JurisdictionId == jurisdictionId, cancellationToken)
                    ?? throw new DbUpdateConcurrencyException("The automatic-debit operation disappeared during webhook recovery.");
            }
        }

        throw new DbUpdateConcurrencyException("Automatic-debit webhook recovery exceeded its retry limit.");
    }

    private async Task<AutomaticDebitWebhookProcessingResult> ProcessSubscriptionAsync(
        long jurisdictionId,
        AutomaticDebitWebhookEnvelope envelope,
        AutomaticDebitProviderReadBack readBack,
        AutomaticDebitAdhesion adhesion,
        CancellationToken cancellationToken)
    {
        var state = NormalizeAdhesionState(readBack.Status);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (!string.Equals(adhesion.ExternalReference, readBack.ExternalReference, StringComparison.Ordinal) ||
                !string.Equals(adhesion.ProviderAdhesionId, readBack.Id, StringComparison.Ordinal))
                return AutomaticDebitWebhookProcessingResult.Rejected(401, "authoritative_mismatch");

            try
            {
                return await PersistAcceptedAsync(
                    jurisdictionId, envelope, readBack, adhesion.Id, null,
                    () =>
                {
                if (!AutomaticDebitProviderStateTransitionPolicy.CanApplyAdhesion(
                        adhesion.ProviderState,
                        state))
                {
                    return;
                }

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                adhesion.ProviderState = state;
                adhesion.ProcessingState = AutomaticDebitProcessingStates.Pending;
                adhesion.ActivatedAt ??= state == AutomaticDebitProviderStates.Active ? now : null;
                adhesion.CancelledAt ??= state == AutomaticDebitProviderStates.Cancelled ? now : null;
                adhesion.DeactivatedAt ??= state == AutomaticDebitProviderStates.Cancelled ? now : null;
                adhesion.DeactivatedBy ??= state == AutomaticDebitProviderStates.Cancelled
                    ? "AUTOMATIC_DEBIT_WEBHOOK"
                    : null;
                adhesion.ModifiedBy = "AUTOMATIC_DEBIT_WEBHOOK";
                adhesion.ModifiedAt = now;
                    }, cancellationToken);
            }
            catch (DbUpdateException exception) when (
                attempt < 2 && (AutomaticDebitEventDeduplication.IsExpectedCollision(exception) ||
                                AutomaticDebitEventDeduplication.IsInboxCollision(exception)))
            {
                adhesion = await _context.AutomaticDebitAdhesions.FirstOrDefaultAsync(
                    x => x.Id == adhesion.Id && x.JurisdictionId == jurisdictionId, cancellationToken)
                    ?? throw new DbUpdateConcurrencyException("The automatic-debit adhesion disappeared during webhook recovery.");
            }
        }

        throw new DbUpdateConcurrencyException("Automatic-debit webhook recovery exceeded its retry limit.");
    }

    private async Task<AutomaticDebitWebhookProcessingResult> PersistAcceptedAsync(
        long jurisdictionId,
        AutomaticDebitWebhookEnvelope envelope,
        AutomaticDebitProviderReadBack readBack,
        long? adhesionId,
        long? operationId,
        Action transition,
        CancellationToken cancellationToken)
    {
        var authoritativeHash = AutomaticDebitAuthoritativeFingerprint.Create(readBack);
        var deduplicationKey = authoritativeHash;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expectedEvent = new AutomaticDebitEvent
        {
            JurisdictionId = jurisdictionId,
            AdhesionId = adhesionId,
            OperationId = operationId,
            DeduplicationKey = deduplicationKey,
            ProviderObjectId = readBack.Id,
            EventType = readBack.ObjectType,
            ProviderState = readBack.Status.Trim().ToUpperInvariant(),
            AuthoritativeHash = authoritativeHash,
            AuthoritativeUpdatedAt = readBack.LastUpdateDate?.UtcDateTime,
            OccurredAt = readBack.LastUpdateDate?.UtcDateTime ?? now,
            CreatedAt = now
        };
        if (await InboxExistsMatchingAsync(jurisdictionId, deduplicationKey, readBack, cancellationToken))
        {
            if (!await AutomaticDebitEventDeduplication.ExistsMatchingAsync(
                    _context, expectedEvent, cancellationToken))
                throw new InvalidOperationException("The automatic-debit inbox exists without its authoritative event.");
            _logger.LogDebug(
                "Webhook duplicado de débito automático ignorado. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} OperationId={OperationId} ProviderObjectId={ProviderObjectId} ProviderState={ProviderState}",
                jurisdictionId, adhesionId, operationId, readBack.Id, readBack.Status);
            return AutomaticDebitWebhookProcessingResult.Accepted(duplicate: true);
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            }

            transition();
            _context.AutomaticDebitWebhookInbox.Add(new AutomaticDebitWebhookInbox
            {
                Id = await _idGenerator.NextInboxIdAsync(cancellationToken),
                JurisdictionId = jurisdictionId,
                DeduplicationKey = deduplicationKey,
                ProviderObjectId = readBack.Id,
                ObjectType = readBack.ObjectType,
                PayloadHash = envelope.PayloadHash,
                RawPayload = Encoding.UTF8.GetString(envelope.RawBytes),
                AuthoritativeHash = authoritativeHash,
                AuthoritativeUpdatedAt = readBack.LastUpdateDate?.UtcDateTime,
                SanitizedMetadata = JsonSerializer.Serialize(new
                {
                    type = readBack.ObjectType,
                    notification_type = envelope.Hint!.NotificationType,
                    provider_id = readBack.Id
                }),
                Result = AutomaticDebitInboxResults.Processed,
                ReceivedAt = now,
                ProcessedAt = now
            });
            if (!await AutomaticDebitEventDeduplication.ExistsMatchingAsync(
                    _context, expectedEvent, cancellationToken))
            {
                expectedEvent.Id = await _idGenerator.NextEventIdAsync(cancellationToken);
                _context.AutomaticDebitEvents.Add(expectedEvent);
            }
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Webhook de débito automático procesado. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} OperationId={OperationId} ProviderObjectId={ProviderObjectId} ExternalTransactionId={ExternalTransactionId} ObjectType={ObjectType} ProviderState={ProviderState}",
                jurisdictionId, adhesionId, operationId, readBack.Id, readBack.ExternalReference,
                readBack.ObjectType, readBack.Status);

            return AutomaticDebitWebhookProcessingResult.Accepted();
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _context.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<bool> InboxExistsMatchingAsync(
        long jurisdictionId,
        string deduplicationKey,
        AutomaticDebitProviderReadBack readBack,
        CancellationToken cancellationToken)
    {
        var existing = await _context.AutomaticDebitWebhookInbox.AsNoTracking().FirstOrDefaultAsync(
            x => x.JurisdictionId == jurisdictionId && x.DeduplicationKey == deduplicationKey,
            cancellationToken);
        if (existing is null)
            return false;

        if (!string.Equals(existing.ProviderObjectId, readBack.Id, StringComparison.Ordinal) ||
            !string.Equals(existing.ObjectType, readBack.ObjectType, StringComparison.Ordinal) ||
            !string.Equals(existing.AuthoritativeHash, deduplicationKey, StringComparison.Ordinal) ||
            existing.AuthoritativeUpdatedAt != readBack.LastUpdateDate?.UtcDateTime)
            throw new InvalidOperationException("The existing automatic-debit inbox does not match the authoritative read-back.");

        return true;
    }

    private static bool MatchesProviderEnvelope(
        JurisdictionPayPerTicConfiguration configuration,
        AutomaticDebitWebhookHint hint,
        AutomaticDebitProviderReadBack readBack) =>
        string.Equals(hint.ObjectType, readBack.ObjectType, StringComparison.Ordinal) &&
        string.Equals(hint.ProviderId, readBack.Id, StringComparison.Ordinal) &&
        string.Equals(configuration.CollectorId, readBack.CollectorId, StringComparison.Ordinal);

    private static string NormalizePaymentState(string providerState) =>
        providerState.Trim().ToUpperInvariant() switch
        {
            AutomaticDebitProviderStates.Issued => AutomaticDebitProviderStates.Issued,
            AutomaticDebitProviderStates.InProcess => AutomaticDebitProviderStates.InProcess,
            AutomaticDebitProviderStates.Approved => AutomaticDebitProviderStates.Approved,
            AutomaticDebitProviderStates.Rejected => AutomaticDebitProviderStates.Rejected,
            AutomaticDebitProviderStates.Cancelled => AutomaticDebitProviderStates.Cancelled,
            AutomaticDebitProviderStates.Overdue => AutomaticDebitProviderStates.Overdue,
            _ => throw new AutomaticDebitProviderException("PayPerTIC returned an unsupported payment state.")
        };

    private static string NormalizeAdhesionState(string providerState) =>
        providerState.Trim().ToUpperInvariant() switch
        {
            AutomaticDebitProviderStates.Pending => AutomaticDebitProviderStates.Pending,
            AutomaticDebitProviderStates.Active => AutomaticDebitProviderStates.Active,
            AutomaticDebitProviderStates.Cancelled => AutomaticDebitProviderStates.Cancelled,
            _ => throw new AutomaticDebitProviderException("PayPerTIC returned an unsupported adhesion state.")
        };
}

