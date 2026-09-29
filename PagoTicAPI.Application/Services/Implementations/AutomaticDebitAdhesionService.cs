namespace PagoTicAPI.Application.Services.Implementations;

public sealed class AutomaticDebitAdhesionService : IAutomaticDebitAdhesionService
{
    private readonly gtwContext _context;
    private readonly IJurisdictionPayPerTicConfigurationProvider _configurationProvider;
    private readonly ITributaryAccountReader _accountReader;
    private readonly IAutomaticDebitPayPerTicClient _client;
    private readonly IAutomaticDebitIdGenerator _idGenerator;
    private readonly IAutomaticDebitAdhesionCreationLock _creationLock;
    private readonly AutomaticDebitFeatureOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutomaticDebitAdhesionService> _logger;

    public AutomaticDebitAdhesionService(
        gtwContext context,
        IJurisdictionPayPerTicConfigurationProvider configurationProvider,
        ITributaryAccountReader accountReader,
        IAutomaticDebitPayPerTicClient client,
        IAutomaticDebitIdGenerator idGenerator,
        IAutomaticDebitAdhesionCreationLock creationLock,
        IOptions<AutomaticDebitFeatureOptions> options,
        TimeProvider timeProvider,
        ILogger<AutomaticDebitAdhesionService> logger)
    {
        _context = context;
        _configurationProvider = configurationProvider;
        _accountReader = accountReader;
        _client = client;
        _idGenerator = idGenerator;
        _creationLock = creationLock;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<OperationResponse<AutomaticDebitAdhesionDto>> CreateAsync(
        CreateAutomaticDebitAdhesionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Disabled();
        }

        if (request.IdJurisdiccion <= 0 || string.IsNullOrWhiteSpace(request.IdTributoContribuyente))
        {
            return OperationResponse<AutomaticDebitAdhesionDto>.BadRequestResponse(
                "Jurisdiction and taxpayer account are required.");
        }

        if (!await HasConfigurationAsync(request.IdJurisdiccion, cancellationToken))
        {
            return OperationResponse<AutomaticDebitAdhesionDto>.NotFoundResponse();
        }

        await using var accountLock = await _creationLock.AcquireAsync(
            request.IdJurisdiccion,
            request.IdTributoContribuyente,
            cancellationToken);

        if (await CurrentAdhesionQuery(request.IdJurisdiccion, request.IdTributoContribuyente)
                .CountAsync(cancellationToken) > 0)
        {
            return CurrentAdhesionConflict();
        }

        var account = await _accountReader.GetByAccountAsync(
            request.IdJurisdiccion,
            request.IdTributoContribuyente,
            cancellationToken);
        if (account is null || account.JurisdictionId != request.IdJurisdiccion ||
            !string.Equals(account.TaxpayerAccountId, request.IdTributoContribuyente, StringComparison.Ordinal))
        {
            return OperationResponse<AutomaticDebitAdhesionDto>.NotFoundResponse();
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var adhesion = new AutomaticDebitAdhesion
        {
            Id = await _idGenerator.NextAdhesionIdAsync(cancellationToken),
            JurisdictionId = account.JurisdictionId,
            TaxpayerAccountId = account.TaxpayerAccountId,
            PersonId = account.PersonId,
            ExternalReference = account.TaxpayerAccountId,
            ProviderState = AutomaticDebitProviderStates.Pending,
            ProcessingState = AutomaticDebitProcessingStates.Pending,
            RequestedAt = now,
            CreatedBy = "AUTOMATIC_DEBIT_API",
            CreatedAt = now
        };

        _context.AutomaticDebitAdhesions.Add(adhesion);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            if (await CurrentAdhesionQuery(
                        request.IdJurisdiccion,
                        request.IdTributoContribuyente)
                    .CountAsync(cancellationToken) > 0)
            {
                return CurrentAdhesionConflict();
            }

            throw;
        }

        var providerCreated = false;
        try
        {
            var provider = await _client.CreateAdhesionAsync(
                request.IdJurisdiccion,
                ToProviderRequest(account),
                cancellationToken);
            providerCreated = true;
            var providerState = AutomaticDebitProviderStateNormalizer.NormalizeAdhesionState(provider.Status);
            adhesion.ProviderAdhesionId = provider.Id;
            adhesion.FormUrl = provider.FormUrl;
            adhesion.ProviderState = providerState;
            adhesion.ActivatedAt = adhesion.ProviderState == AutomaticDebitProviderStates.Active ? now : null;
            adhesion.ModifiedBy = "AUTOMATIC_DEBIT_API";
            adhesion.ModifiedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Adhesión al débito automático creada. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} ProviderAdhesionId={ProviderAdhesionId} ProviderState={ProviderState}",
                adhesion.JurisdictionId, adhesion.Id, adhesion.ProviderAdhesionId, adhesion.ProviderState);
            return OperationResponse<AutomaticDebitAdhesionDto>.SuccessResponse(ToDto(adhesion));
        }
        catch (AutomaticDebitProviderException exception)
            when (!providerCreated && exception.IsDefinitiveRejection)
        {
            _context.AutomaticDebitAdhesions.Remove(adhesion);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "Falló la creación de la adhesión al débito automático. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId}",
                request.IdJurisdiccion, adhesion.Id);
            return OperationResponse<AutomaticDebitAdhesionDto>.CustomErrorResponse(
                502,
                "The automatic-debit provider could not create the adhesion.");
        }
        catch (AutomaticDebitProviderException exception) when (!providerCreated)
        {
            _context.Entry(adhesion).State = EntityState.Unchanged;
            _logger.LogWarning(
                "El resultado de la creación de la adhesión en PayPerTIC es incierto. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} ProviderStatusCode={ProviderStatusCode}",
                request.IdJurisdiccion, adhesion.Id, exception.StatusCode);
            return OperationResponse<AutomaticDebitAdhesionDto>.CustomErrorResponse(
                502,
                "The provider response requires automatic-debit adhesion reconciliation.");
        }
        catch (Exception exception) when (
            exception is DbUpdateException ||
            exception is AutomaticDebitProviderException && providerCreated)
        {
            _context.Entry(adhesion).State = EntityState.Unchanged;
            _logger.LogError(
                "PayPerTIC creó la adhesión, pero la finalización local requiere reconciliación. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} ProviderAdhesionId={ProviderAdhesionId}",
                request.IdJurisdiccion, adhesion.Id, adhesion.ProviderAdhesionId);
            return OperationResponse<AutomaticDebitAdhesionDto>.CustomErrorResponse(
                502,
                "The provider response requires automatic-debit adhesion reconciliation.");
        }
    }

    public async Task<OperationResponse<AutomaticDebitAdhesionDto>> GetAsync(
        long jurisdictionId,
        string taxpayerAccountId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Disabled();
        }

        var adhesion = await _context.AutomaticDebitAdhesions
            .Where(x => x.JurisdictionId == jurisdictionId && x.TaxpayerAccountId == taxpayerAccountId)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (adhesion is null)
        {
            return OperationResponse<AutomaticDebitAdhesionDto>.NotFoundResponse();
        }

        if (!string.IsNullOrWhiteSpace(adhesion.ProviderAdhesionId))
        {
            try
            {
                var provider = await _client.GetAdhesionAsync(
                    jurisdictionId,
                    adhesion.ProviderAdhesionId,
                    cancellationToken);
                var normalizedState = AutomaticDebitProviderStateNormalizer.NormalizeAdhesionState(provider.Status);
                if (AutomaticDebitProviderStateTransitionPolicy.CanApplyAdhesion(
                        adhesion.ProviderState,
                        normalizedState))
                {
                    var previousState = adhesion.ProviderState;
                    adhesion.ProviderState = normalizedState;
                    adhesion.FormUrl = provider.FormUrl ?? adhesion.FormUrl;
                    var now = _timeProvider.GetUtcNow().UtcDateTime;
                    adhesion.ActivatedAt ??= normalizedState == AutomaticDebitProviderStates.Active ? now : null;
                    adhesion.CancelledAt ??= normalizedState == AutomaticDebitProviderStates.Cancelled ? now : null;
                    adhesion.DeactivatedAt ??= normalizedState == AutomaticDebitProviderStates.Cancelled ? now : null;
                    adhesion.DeactivatedBy ??= normalizedState == AutomaticDebitProviderStates.Cancelled
                        ? "AUTOMATIC_DEBIT_API"
                        : null;
                    adhesion.ModifiedBy = "AUTOMATIC_DEBIT_API";
                    adhesion.ModifiedAt = now;
                    await _context.SaveChangesAsync(cancellationToken);
                    if (!string.Equals(previousState, normalizedState, StringComparison.Ordinal))
                    {
                        _logger.LogInformation(
                            "El estado de la adhesión cambió luego de consultar al proveedor. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} ProviderAdhesionId={ProviderAdhesionId} PreviousState={PreviousState} ProviderState={ProviderState}",
                            jurisdictionId, adhesion.Id, adhesion.ProviderAdhesionId, previousState, normalizedState);
                    }
                }
            }
            catch (AutomaticDebitProviderException exception)
            {
                _logger.LogWarning(
                    "Falló la consulta autoritativa de la adhesión. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} ProviderAdhesionId={ProviderAdhesionId} ProviderStatusCode={ProviderStatusCode} ProviderState={ProviderState}",
                    jurisdictionId, adhesion.Id, adhesion.ProviderAdhesionId, exception.StatusCode, adhesion.ProviderState);
                return OperationResponse<AutomaticDebitAdhesionDto>.CustomErrorResponse(
                    502,
                    "The automatic-debit provider could not retrieve the adhesion.");
            }
        }

        return OperationResponse<AutomaticDebitAdhesionDto>.SuccessResponse(ToDto(adhesion));
    }

    public async Task<OperationResponse<AutomaticDebitAdhesionDto>> CancelAsync(
        long jurisdictionId,
        string taxpayerAccountId,
        CancelAutomaticDebitAdhesionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Disabled();
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return OperationResponse<AutomaticDebitAdhesionDto>.BadRequestResponse(
                "A cancellation reason is required.");
        }

        if (!await HasConfigurationAsync(jurisdictionId, cancellationToken))
        {
            return OperationResponse<AutomaticDebitAdhesionDto>.NotFoundResponse();
        }

        var adhesion = await CurrentAdhesionQuery(jurisdictionId, taxpayerAccountId)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (adhesion is null || string.IsNullOrWhiteSpace(adhesion.ProviderAdhesionId))
        {
            return OperationResponse<AutomaticDebitAdhesionDto>.NotFoundResponse();
        }

        try
        {
            var provider = await _client.CancelAdhesionAsync(
                jurisdictionId,
                adhesion.ProviderAdhesionId,
                request.Reason,
                cancellationToken);
            var state = AutomaticDebitProviderStateNormalizer.NormalizeAdhesionState(provider.Status);
            if (state != AutomaticDebitProviderStates.Cancelled)
            {
                throw new AutomaticDebitProviderException("PayPerTIC did not confirm adhesion cancellation.");
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            adhesion.ProviderState = AutomaticDebitProviderStates.Cancelled;
            adhesion.CancelledAt = now;
            adhesion.CancellationReason = request.Reason;
            adhesion.ModifiedBy = "AUTOMATIC_DEBIT_API";
            adhesion.ModifiedAt = now;
            adhesion.DeactivatedBy = "AUTOMATIC_DEBIT_API";
            adhesion.DeactivatedAt = now;
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Adhesión al débito automático cancelada. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} ProviderAdhesionId={ProviderAdhesionId} ProviderState={ProviderState}",
                jurisdictionId, adhesion.Id, adhesion.ProviderAdhesionId, adhesion.ProviderState);
            return OperationResponse<AutomaticDebitAdhesionDto>.SuccessResponse(ToDto(adhesion));
        }
        catch (AutomaticDebitProviderException exception)
        {
            _logger.LogWarning(
                "Falló la cancelación de la adhesión al débito automático. JurisdictionId={JurisdictionId} AdhesionId={AdhesionId} ProviderAdhesionId={ProviderAdhesionId} ProviderStatusCode={ProviderStatusCode}",
                jurisdictionId, adhesion.Id, adhesion.ProviderAdhesionId, exception.StatusCode);
            return OperationResponse<AutomaticDebitAdhesionDto>.CustomErrorResponse(
                502,
                "The automatic-debit provider could not cancel the adhesion.");
        }
    }

    private IQueryable<AutomaticDebitAdhesion> CurrentAdhesionQuery(long jurisdictionId, string taxpayerAccountId) =>
        _context.AutomaticDebitAdhesions.Where(x =>
            x.JurisdictionId == jurisdictionId &&
            x.TaxpayerAccountId == taxpayerAccountId &&
            (x.ProviderState == AutomaticDebitProviderStates.Pending ||
             x.ProviderState == AutomaticDebitProviderStates.Active));

    private async Task<bool> HasConfigurationAsync(long jurisdictionId, CancellationToken cancellationToken)
    {
        try
        {
            await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
            return true;
        }
        catch (PayPerTicConfigurationNotFoundException)
        {
            return false;
        }
    }

    private static AutomaticDebitProviderCreateRequest ToProviderRequest(TributaryAccountReadModel account) => new(
        account.TaxpayerAccountId,
        account.ConceptId,
        account.ConceptDescription,
        new AutomaticDebitProviderPayer(
            account.PayerName,
            account.PayerEmail,
            account.PersonId,
            account.IdentificationType,
            account.IdentificationNumber,
            account.IdentificationCountry));

    private static AutomaticDebitAdhesionDto ToDto(AutomaticDebitAdhesion adhesion) => new(
        adhesion.Id,
        adhesion.JurisdictionId,
        adhesion.TaxpayerAccountId,
        adhesion.PersonId,
        adhesion.ProviderAdhesionId,
        adhesion.FormUrl,
        adhesion.ProviderState,
        AutomaticDebitApiDateTime.FromUtc(adhesion.RequestedAt),
        AutomaticDebitApiDateTime.FromUtc(adhesion.ActivatedAt),
        AutomaticDebitApiDateTime.FromUtc(adhesion.CancelledAt));

    private static OperationResponse<AutomaticDebitAdhesionDto> Disabled() =>
        OperationResponse<AutomaticDebitAdhesionDto>.CustomErrorResponse(
            404,
            "Automatic debit is not enabled.");

    private static OperationResponse<AutomaticDebitAdhesionDto> CurrentAdhesionConflict() =>
        OperationResponse<AutomaticDebitAdhesionDto>.CustomErrorResponse(
            409,
            "A current automatic-debit adhesion already exists for this account.");
}

