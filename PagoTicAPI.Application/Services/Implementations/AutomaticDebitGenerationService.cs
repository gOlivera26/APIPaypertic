namespace PagoTicAPI.Application.Services.Implementations;

public sealed class AutomaticDebitGenerationService : IAutomaticDebitGenerationService
{
    private const string ProcessUser = "AUTOMATIC_DEBIT_API";
    private readonly gtwContext _context;
    private readonly IAutomaticDebitObligationReader _obligationReader;
    private readonly IAutomaticDebitPayPerTicClient _client;
    private readonly IAutomaticDebitIdGenerator _idGenerator;
    private readonly IAutomaticDebitWebhookIdGenerator _webhookIdGenerator;
    private readonly IAutomaticDebitOperationCreationLock _creationLock;
    private readonly AutomaticDebitFeatureOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutomaticDebitGenerationService> _logger;

    public AutomaticDebitGenerationService(
        gtwContext context,
        IAutomaticDebitObligationReader obligationReader,
        IAutomaticDebitPayPerTicClient client,
        IAutomaticDebitIdGenerator idGenerator,
        IAutomaticDebitWebhookIdGenerator webhookIdGenerator,
        IAutomaticDebitOperationCreationLock creationLock,
        IOptions<AutomaticDebitFeatureOptions> options,
        TimeProvider timeProvider,
        ILogger<AutomaticDebitGenerationService> logger)
    {
        _context = context;
        _obligationReader = obligationReader;
        _client = client;
        _idGenerator = idGenerator;
        _webhookIdGenerator = webhookIdGenerator;
        _creationLock = creationLock;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<OperationResponse<AutomaticDebitOperationDto>> CreateAsync(
        CreateAutomaticDebitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Error(404, "Automatic debit is not enabled.");
        if (request.IdJurisdiccion <= 0 || string.IsNullOrWhiteSpace(request.IdObligacion))
            return OperationResponse<AutomaticDebitOperationDto>.BadRequestResponse("Jurisdiction and obligation are required.");

        await using var operationLock = await _creationLock.AcquireAsync(
            request.IdJurisdiccion, request.IdObligacion, cancellationToken);

        var existing = await OpenOperationQuery(request.IdJurisdiccion, request.IdObligacion)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "La emisión del débito automático reutilizó una operación existente. JurisdictionId={JurisdictionId} OperationId={OperationId} ObligationId={ObligationId} ProviderTransactionId={ProviderTransactionId} ExternalTransactionId={ExternalTransactionId} ProviderState={ProviderState}",
                existing.JurisdictionId, existing.Id, existing.ObligationId, existing.ProviderTransactionId,
                existing.ExternalTransactionId, existing.ProviderState);
            return OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(ToDto(existing));
        }

        var obligation = await _obligationReader.FindAsync(
            request.IdJurisdiccion, request.IdObligacion, cancellationToken);
        if (obligation is null || obligation.JurisdictionId != request.IdJurisdiccion ||
            !string.Equals(obligation.ObligationId, request.IdObligacion, StringComparison.Ordinal))
            return OperationResponse<AutomaticDebitOperationDto>.NotFoundResponse();

        var eligibility = AutomaticDebitObligationPolicy.Evaluate(obligation);
        if (!eligibility.IsEligible)
            return Error(422, $"Obligation is not eligible: {eligibility.RejectionReason}.");

        var adhesion = await _context.AutomaticDebitAdhesions
            .Where(x => x.JurisdictionId == request.IdJurisdiccion &&
                        x.TaxpayerAccountId == obligation.TaxpayerAccountId &&
                        x.ProviderState == AutomaticDebitProviderStates.Active &&
                        x.DeactivatedAt == null)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (adhesion is null || string.IsNullOrWhiteSpace(adhesion.ProviderAdhesionId))
            return Error(409, "An ACTIVE automatic-debit adhesion is required for this account.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var operationId = await _idGenerator.NextOperationIdAsync(cancellationToken);
        var operation = new AutomaticDebitOperation
        {
            Id = operationId,
            AdhesionId = adhesion.Id,
            JurisdictionId = obligation.JurisdictionId,
            TaxpayerAccountId = obligation.TaxpayerAccountId,
            ObligationId = obligation.ObligationId,
            ExternalTransactionId = $"PPT-DA-{obligation.JurisdictionId}-{obligation.ObligationId}-{operationId}",
            Amount = obligation.Amount,
            DueDate = obligation.DueDate,
            ProviderState = AutomaticDebitProviderStates.Pending,
            ProcessingState = AutomaticDebitProcessingStates.Pending,
            RequestedAt = now,
            CreatedBy = ProcessUser,
            CreatedAt = now
        };
        _context.AutomaticDebitOperations.Add(operation);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            var competing = await OpenOperationQuery(request.IdJurisdiccion, request.IdObligacion)
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
            if (competing is not null)
                return OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(ToDto(competing));
            throw;
        }

        try
        {
            var provider = await _client.CreateDebitAsync(
                request.IdJurisdiccion,
                adhesion.ProviderAdhesionId,
                new AutomaticDebitProviderPaymentRequest(
                    operation.ExternalTransactionId,
                    ToProviderDueDate(obligation.DueDate),
                    obligation.ObligationId,
                    obligation.TaxTypeId,
                    obligation.Concept,
                    obligation.Amount),
                cancellationToken);
            operation.ProviderTransactionId = provider.Id;
            operation.ProviderState = NormalizeCreatedState(provider.Status);
            operation.ModifiedBy = ProcessUser;
            operation.ModifiedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Débito automático emitido. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} OperationId={OperationId} ObligationId={ObligationId} ProviderTransactionId={ProviderTransactionId} ExternalTransactionId={ExternalTransactionId} ProviderState={ProviderState}",
                operation.JurisdictionId, operation.AdhesionId, operation.Id, operation.ObligationId,
                operation.ProviderTransactionId, operation.ExternalTransactionId, operation.ProviderState);
            return OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(ToDto(operation));
        }
        catch (AutomaticDebitProviderException exception) when (exception.IsDefinitiveRejection)
        {
            operation.ProviderState = AutomaticDebitProviderStates.Rejected;
            operation.ProcessingState = AutomaticDebitProcessingStates.Failed;
            operation.RejectedAt = now;
            operation.RejectionReason = "Provider rejected debit issuance.";
            operation.ModifiedBy = ProcessUser;
            operation.ModifiedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "La emisión del débito automático fue rechazada de forma definitiva. JurisdictionId={JurisdictionId} OperationId={OperationId} ObligationId={ObligationId} ExternalTransactionId={ExternalTransactionId} ProviderStatusCode={ProviderStatusCode} ProviderState={ProviderState}",
                operation.JurisdictionId, operation.Id, operation.ObligationId, operation.ExternalTransactionId,
                exception.StatusCode, operation.ProviderState);
            return Error(422, "The automatic-debit provider rejected the issuance request.");
        }
        catch (AutomaticDebitProviderException)
        {
            _context.Entry(operation).State = EntityState.Unchanged;
            _logger.LogWarning(
                "La emisión del débito automático requiere reconciliación. JurisdictionId={JurisdictionId} OperationId={OperationId} AdhesionId={AdhesionId} ObligationId={ObligationId} ExternalTransactionId={ExternalTransactionId} ProviderState={ProviderState}",
                operation.JurisdictionId, operation.Id, operation.AdhesionId, operation.ObligationId,
                operation.ExternalTransactionId, operation.ProviderState);
            return Error(502, "The provider outcome is uncertain and requires reconciliation.");
        }
    }

    public async Task<OperationResponse<AutomaticDebitOperationDto>> ReconcileAsync(
        long operationId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Error(404, "Automatic debit is not enabled.");
        if (operationId <= 0)
            return OperationResponse<AutomaticDebitOperationDto>.BadRequestResponse("Operation is required.");

        var operation = await _context.AutomaticDebitOperations
            .FirstOrDefaultAsync(x => x.Id == operationId, cancellationToken);
        if (operation is null)
            return OperationResponse<AutomaticDebitOperationDto>.NotFoundResponse();
        if (!string.IsNullOrWhiteSpace(operation.ProviderTransactionId))
            return await GetAsync(operationId, cancellationToken);
        if (operation.ProviderState != AutomaticDebitProviderStates.Pending)
            return OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(ToDto(operation));
        await using var operationLock = await _creationLock.AcquireAsync(
            operation.JurisdictionId, operation.ObligationId, cancellationToken);

        await _context.Entry(operation).ReloadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(operation.ProviderTransactionId))
            return await GetAsync(operationId, cancellationToken);
        if (operation.ProviderState != AutomaticDebitProviderStates.Pending)
            return OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(ToDto(operation));

        var adhesion = await _context.AutomaticDebitAdhesions.FirstOrDefaultAsync(
            x => x.Id == operation.AdhesionId &&
                 x.JurisdictionId == operation.JurisdictionId &&
                 x.TaxpayerAccountId == operation.TaxpayerAccountId,
            cancellationToken);
        if (adhesion is null || string.IsNullOrWhiteSpace(adhesion.ProviderAdhesionId))
            return Error(502, "The provider outcome is uncertain and requires reconciliation.");

        try
        {
            var adhesionReadBack = await _client.GetAdhesionReadBackAsync(
                operation.JurisdictionId, adhesion.ProviderAdhesionId, cancellationToken);
            if (adhesionReadBack is null ||
                !string.Equals(adhesionReadBack.ObjectType, AutomaticDebitWebhookObjectTypes.Subscription, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(adhesionReadBack.Id, adhesion.ProviderAdhesionId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(adhesionReadBack.CollectorId) ||
                !string.Equals(adhesionReadBack.ExternalReference, adhesion.ExternalReference, StringComparison.Ordinal))
                return Error(502, "The provider outcome is uncertain and requires reconciliation.");

            var match = await _client.FindDebitByExternalIdAsync(
                operation.JurisdictionId, operation.ExternalTransactionId, operation.RequestedAt,
                adhesionReadBack.CollectorId, cancellationToken);
            if (match is null)
                return Error(502, "The provider outcome is uncertain and requires reconciliation.");
            if (!string.Equals(match.ExternalTransactionId, operation.ExternalTransactionId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(match.Id) ||
                !string.Equals(match.CollectorId, adhesionReadBack.CollectorId, StringComparison.Ordinal))
                return Error(502, "The provider operation does not match the local automatic debit.");

            var readBack = await _client.GetPaymentReadBackAsync(operation.JurisdictionId, match.Id, cancellationToken);
            if (!string.Equals(readBack.ObjectType, AutomaticDebitWebhookObjectTypes.Payment, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(readBack.Id, match.Id, StringComparison.Ordinal) ||
                !string.Equals(readBack.ExternalReference, operation.ExternalTransactionId, StringComparison.Ordinal) ||
                !string.Equals(readBack.CollectorId, adhesionReadBack.CollectorId, StringComparison.Ordinal) ||
                !string.Equals(readBack.PaymentType, "debit", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(readBack.PaymentCurrencyId, "ARS", StringComparison.Ordinal) ||
                readBack.DetailAmount != operation.Amount ||
                !string.Equals(readBack.DetailExternalReference, operation.ObligationId, StringComparison.Ordinal))
                return Error(502, "The provider operation does not match the local automatic debit.");
            var state = NormalizeReadBackState(readBack.Status);
            if (!await PersistReadBackAsync(operation, readBack, state, allowUnboundProviderId: true, cancellationToken))
                return Error(502, "The provider operation does not match the local automatic debit.");
            _logger.LogInformation(
                "Reconciliación del débito automático completada. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} OperationId={OperationId} ObligationId={ObligationId} ProviderTransactionId={ProviderTransactionId} ExternalTransactionId={ExternalTransactionId} ProviderState={ProviderState}",
                operation.JurisdictionId, operation.AdhesionId, operation.Id, operation.ObligationId,
                operation.ProviderTransactionId, operation.ExternalTransactionId, operation.ProviderState);
            return OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(ToDto(operation));
        }
        catch (AutomaticDebitProviderException)
        {
            _context.Entry(operation).State = EntityState.Unchanged;
            _logger.LogWarning(
                "La reconciliación del débito automático continúa incierta. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} OperationId={OperationId} ObligationId={ObligationId} ExternalTransactionId={ExternalTransactionId} ProviderState={ProviderState}",
                operation.JurisdictionId, operation.AdhesionId, operation.Id, operation.ObligationId,
                operation.ExternalTransactionId, operation.ProviderState);
            return Error(502, "The provider outcome is uncertain and requires reconciliation.");
        }
    }

    public async Task<OperationResponse<AutomaticDebitOperationDto>> GetAsync(
        long operationId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return Error(404, "Automatic debit is not enabled.");
        if (operationId <= 0)
            return OperationResponse<AutomaticDebitOperationDto>.BadRequestResponse("Operation is required.");

        var operation = await _context.AutomaticDebitOperations
            .FirstOrDefaultAsync(x => x.Id == operationId, cancellationToken);
        if (operation is null)
            return OperationResponse<AutomaticDebitOperationDto>.NotFoundResponse();
        if (string.IsNullOrWhiteSpace(operation.ProviderTransactionId))
            return Error(409, "The operation requires issuance reconciliation before it can be queried.");

        AutomaticDebitProviderReadBack readBack;
        try
        {
            readBack = await _client.GetPaymentReadBackAsync(
                operation.JurisdictionId,
                operation.ProviderTransactionId,
                cancellationToken);
        }
        catch (AutomaticDebitProviderException exception)
        {
            _logger.LogWarning(
                "Falló la consulta autoritativa del débito automático. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} OperationId={OperationId} ObligationId={ObligationId} ProviderTransactionId={ProviderTransactionId} ExternalTransactionId={ExternalTransactionId} ProviderStatusCode={ProviderStatusCode} ProviderState={ProviderState}",
                operation.JurisdictionId, operation.AdhesionId, operation.Id, operation.ObligationId,
                operation.ProviderTransactionId, operation.ExternalTransactionId, exception.StatusCode, operation.ProviderState);
            return Error(502, "The automatic-debit provider could not retrieve the operation.");
        }

        if (!string.Equals(readBack.ObjectType, AutomaticDebitWebhookObjectTypes.Payment, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(readBack.Id, operation.ProviderTransactionId, StringComparison.Ordinal) ||
            !string.Equals(readBack.ExternalReference, operation.ExternalTransactionId, StringComparison.Ordinal))
            return Error(502, "The provider operation does not match the local automatic debit.");

        string state;
        try
        {
            state = NormalizeReadBackState(readBack.Status);
        }
        catch (AutomaticDebitProviderException)
        {
            return Error(502, "The provider returned an unsupported automatic-debit state.");
        }

        var previousState = operation.ProviderState;
        if (!await PersistReadBackAsync(operation, readBack, state, allowUnboundProviderId: false, cancellationToken))
            return Error(502, "The provider operation does not match the local automatic debit.");
        if (!string.Equals(previousState, operation.ProviderState, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "El estado del débito automático cambió luego de consultar al proveedor. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} OperationId={OperationId} ObligationId={ObligationId} ProviderTransactionId={ProviderTransactionId} ExternalTransactionId={ExternalTransactionId} PreviousState={PreviousState} ProviderState={ProviderState}",
                operation.JurisdictionId, operation.AdhesionId, operation.Id, operation.ObligationId,
                operation.ProviderTransactionId, operation.ExternalTransactionId, previousState, operation.ProviderState);
        }
        return OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(ToDto(operation));
    }

    private async Task<bool> PersistReadBackAsync(
        AutomaticDebitOperation operation,
        AutomaticDebitProviderReadBack readBack,
        string state,
        bool allowUnboundProviderId,
        CancellationToken cancellationToken)
    {
        var fingerprint = AutomaticDebitAuthoritativeFingerprint.Create(readBack);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (operation.JurisdictionId <= 0 ||
                !string.Equals(operation.ExternalTransactionId, readBack.ExternalReference, StringComparison.Ordinal) ||
                (!string.Equals(operation.ProviderTransactionId, readBack.Id, StringComparison.Ordinal) &&
                 !(allowUnboundProviderId && string.IsNullOrWhiteSpace(operation.ProviderTransactionId))))
                return false;

            if (allowUnboundProviderId && string.IsNullOrWhiteSpace(operation.ProviderTransactionId) &&
                operation.ProviderState != AutomaticDebitProviderStates.Pending)
                return false;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (allowUnboundProviderId && string.IsNullOrWhiteSpace(operation.ProviderTransactionId))
                operation.ProviderTransactionId = readBack.Id;
            if (AutomaticDebitProviderStateTransitionPolicy.CanApplyPayment(operation.ProviderState, state))
            {
                operation.ProviderState = state;
                operation.ProcessingState = AutomaticDebitProcessingStates.Pending;
                operation.ApprovedAt ??= state == AutomaticDebitProviderStates.Approved ? now : null;
                operation.RejectedAt ??= state == AutomaticDebitProviderStates.Rejected ? now : null;
                operation.ModifiedBy = ProcessUser;
                operation.ModifiedAt = now;
            }

            var expectedEvent = new AutomaticDebitEvent
            {
                JurisdictionId = operation.JurisdictionId,
                AdhesionId = operation.AdhesionId,
                OperationId = operation.Id,
                DeduplicationKey = fingerprint,
                ProviderObjectId = readBack.Id,
                EventType = readBack.ObjectType,
                ProviderState = state,
                AuthoritativeHash = fingerprint,
                AuthoritativeUpdatedAt = readBack.LastUpdateDate?.UtcDateTime,
                OccurredAt = readBack.LastUpdateDate?.UtcDateTime ?? now,
                CreatedAt = now
            };
            if (!await AutomaticDebitEventDeduplication.ExistsMatchingAsync(
                    _context, expectedEvent, cancellationToken))
            {
                expectedEvent.Id = await _webhookIdGenerator.NextEventIdAsync(cancellationToken);
                _context.AutomaticDebitEvents.Add(expectedEvent);
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                if (_context.Entry(expectedEvent).State == EntityState.Added)
                    _context.Entry(expectedEvent).State = EntityState.Detached;
                await _context.Entry(operation).ReloadAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (
                attempt < 2 && AutomaticDebitEventDeduplication.IsExpectedCollision(exception))
            {
                if (_context.Entry(expectedEvent).State == EntityState.Added)
                    _context.Entry(expectedEvent).State = EntityState.Detached;
                await _context.Entry(operation).ReloadAsync(cancellationToken);
            }
        }

        throw new DbUpdateConcurrencyException("Automatic-debit read-back persistence exceeded its retry limit.");
    }

    public async Task<OperationResponse<AutomaticDebitOperationDto>> CancelAsync(
        long operationId,
        CancelAutomaticDebitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return OperationResponse<AutomaticDebitOperationDto>.BadRequestResponse(
                "A cancellation reason is required.");

        if (!_options.Enabled)
            return Error(404, "Automatic debit is not enabled.");

        var existing = operationId > 0
            ? await _context.AutomaticDebitOperations
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == operationId, cancellationToken)
            : null;
        if (existing?.ProviderState == AutomaticDebitProviderStates.Cancelled)
            return OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(ToDto(existing));

        var current = await GetAsync(operationId, cancellationToken);
        if (current.Code != 200 || current.Data is null)
            return current;
        if (current.Data.ProviderState != AutomaticDebitProviderStates.Issued)
            return Error(409, "PayPerTIC only allows cancellation while the payment is ISSUED.");

        try
        {
            await _client.CancelPaymentAsync(
                current.Data.JurisdictionId,
                current.Data.ProviderTransactionId!,
                request.Reason.Trim(),
                cancellationToken);
        }
        catch (AutomaticDebitProviderException)
        {
            _logger.LogWarning(
                "Falló la cancelación del débito automático. JurisdictionId={JurisdictionId} OperationId={OperationId} ObligationId={ObligationId} ProviderTransactionId={ProviderTransactionId} ExternalTransactionId={ExternalTransactionId} ProviderState={ProviderState}",
                current.Data.JurisdictionId, current.Data.Id, current.Data.ObligationId,
                current.Data.ProviderTransactionId, current.Data.ExternalTransactionId, current.Data.ProviderState);
            return Error(502, "The automatic-debit provider could not cancel the operation.");
        }

        var result = await GetAsync(operationId, cancellationToken);
        if (result.Code == 200 && result.Data is not null)
        {
            _logger.LogInformation(
                "Cancelación del débito automático confirmada. JurisdictionId={JurisdictionId} OperationId={OperationId} ObligationId={ObligationId} ProviderTransactionId={ProviderTransactionId} ExternalTransactionId={ExternalTransactionId} ProviderState={ProviderState}",
                result.Data.JurisdictionId, result.Data.Id, result.Data.ObligationId,
                result.Data.ProviderTransactionId, result.Data.ExternalTransactionId, result.Data.ProviderState);
        }
        return result;
    }

    private IQueryable<AutomaticDebitOperation> OpenOperationQuery(long jurisdictionId, string obligationId) =>
        _context.AutomaticDebitOperations.Where(x =>
            x.JurisdictionId == jurisdictionId && x.ObligationId == obligationId);

    private static string NormalizeCreatedState(string status) => status.Trim().ToUpperInvariant() switch
    {
        AutomaticDebitProviderStates.Issued => AutomaticDebitProviderStates.Issued,
        AutomaticDebitProviderStates.InProcess => AutomaticDebitProviderStates.InProcess,
        AutomaticDebitProviderStates.Approved => AutomaticDebitProviderStates.Approved,
        _ => throw new AutomaticDebitProviderException("PayPerTIC returned an unsupported debit state.")
    };

    private static string NormalizeReadBackState(string status) => status.Trim().ToUpperInvariant() switch
    {
        AutomaticDebitProviderStates.Issued => AutomaticDebitProviderStates.Issued,
        AutomaticDebitProviderStates.InProcess => AutomaticDebitProviderStates.InProcess,
        AutomaticDebitProviderStates.Approved => AutomaticDebitProviderStates.Approved,
        AutomaticDebitProviderStates.Rejected => AutomaticDebitProviderStates.Rejected,
        AutomaticDebitProviderStates.Cancelled => AutomaticDebitProviderStates.Cancelled,
        AutomaticDebitProviderStates.Overdue => AutomaticDebitProviderStates.Overdue,
        _ => throw new AutomaticDebitProviderException("PayPerTIC returned an unsupported debit state.")
    };

    private static DateTimeOffset ToProviderDueDate(DateTime dueDate) =>
        new(DateTime.SpecifyKind(dueDate.Date, DateTimeKind.Unspecified), TimeSpan.FromHours(-3));

    private static AutomaticDebitOperationDto ToDto(AutomaticDebitOperation operation) => new(
        operation.Id, operation.JurisdictionId, operation.TaxpayerAccountId, operation.ObligationId,
        operation.ProviderTransactionId, operation.ExternalTransactionId, operation.Amount, operation.DueDate,
        operation.ProviderState, ToApiValue(operation.ProcessingState),
        AutomaticDebitApiDateTime.FromUtc(operation.RequestedAt));

    private static string ToApiValue(AutomaticDebitProcessingStates value) =>
        value.ToString().ToUpperInvariant();

    private static OperationResponse<AutomaticDebitOperationDto> Error(int code, string message) =>
        OperationResponse<AutomaticDebitOperationDto>.CustomErrorResponse(code, message);
}

