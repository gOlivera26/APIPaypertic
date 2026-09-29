namespace PagoTicAPI.Application.Services.Implementations;

public sealed class OperationalAuditService : IOperationalAuditService
{
    private const int MaximumPageSize = 100;
    private readonly gtwContext _gatewayContext;
    private readonly TimeProvider _timeProvider;

    public OperationalAuditService(
        gtwContext gatewayContext,
        TimeProvider timeProvider)
    {
        _gatewayContext = gatewayContext;
        _timeProvider = timeProvider;
    }

    public async Task<OperationResponse<PagedAuditResult<AutomaticDebitAuditItem>>> GetAutomaticDebitsAsync(
        AutomaticDebitAuditFilter filter,
        CancellationToken cancellationToken)
    {
        var validation = Validate(filter);
        if (validation is not null)
            return OperationResponse<PagedAuditResult<AutomaticDebitAuditItem>>.BadRequestResponse(validation);

        var (fromUtc, toUtc) = DateRange(filter);
        var query = ApplyAutomaticDebitFilters(
            _gatewayContext.AutomaticDebitOperations.AsNoTracking(), filter, fromUtc, toUtc);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.RequestedAt)
            .ThenByDescending(x => x.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new DebitProjection(
                x.Id, x.AdhesionId, x.JurisdictionId, x.TaxpayerAccountId, x.ObligationId,
                x.ProviderTransactionId, x.ExternalTransactionId, x.Amount, x.DueDate,
                x.ProviderState, x.ProcessingState, x.RequestedAt, x.ProcessedAt,
                x.ApprovedAt, x.RejectedAt, x.RejectionReason))
            .ToListAsync(cancellationToken);

        var result = Page(filter, total, rows.Select(MapDebit).ToList());
        return OperationResponse<PagedAuditResult<AutomaticDebitAuditItem>>.SuccessResponse(result, total);
    }

    public async Task<OperationResponse<AutomaticDebitAuditItem>> GetAutomaticDebitAsync(long id, CancellationToken cancellationToken)
    {
        var row = await _gatewayContext.AutomaticDebitOperations.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new DebitProjection(
                x.Id, x.AdhesionId, x.JurisdictionId, x.TaxpayerAccountId, x.ObligationId,
                x.ProviderTransactionId, x.ExternalTransactionId, x.Amount, x.DueDate,
                x.ProviderState, x.ProcessingState, x.RequestedAt, x.ProcessedAt,
                x.ApprovedAt, x.RejectedAt, x.RejectionReason))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? OperationResponse<AutomaticDebitAuditItem>.NotFoundResponse()
            : OperationResponse<AutomaticDebitAuditItem>.SuccessResponse(MapDebit(row));
    }

    public async Task<OperationResponse<PagedAuditResult<OperationalAuditEvent>>> GetAutomaticDebitHistoryAsync(
        long id, AuditHistoryFilter filter, CancellationToken cancellationToken)
    {
        var validation = Validate(filter);
        if (validation is not null)
            return OperationResponse<PagedAuditResult<OperationalAuditEvent>>.BadRequestResponse(validation);
        var operation = await _gatewayContext.AutomaticDebitOperations.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.JurisdictionId, x.ProviderTransactionId, x.ProviderState, x.RequestedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (operation is null) return OperationResponse<PagedAuditResult<OperationalAuditEvent>>.NotFoundResponse();

        var (fromUtc, toUtc) = DateRange(filter);
        var take = checked(filter.Page * filter.PageSize);
        var eventQuery = ApplyAutomaticDebitEventFilters(
            _gatewayContext.AutomaticDebitEvents.AsNoTracking(), id, fromUtc, toUtc);
        var eventCount = await eventQuery.CountAsync(cancellationToken);
        var events = await eventQuery
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Take(take)
            .Select(x => new HistoryProjection("EVENT", x.EventType, x.ProviderState, null, x.OccurredAt, (DateTime?)null))
            .ToListAsync(cancellationToken);

        var inboxCount = 0;
        if (!string.IsNullOrWhiteSpace(operation.ProviderTransactionId))
        {
            var providerId = operation.ProviderTransactionId;
            var inboxQuery = ApplyAutomaticDebitInboxFilters(
                _gatewayContext.AutomaticDebitWebhookInbox.AsNoTracking(),
                operation.JurisdictionId, providerId, fromUtc, toUtc);
            inboxCount = await inboxQuery.CountAsync(cancellationToken);
            var inboxRows = await inboxQuery
                .OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id)
                .Take(take)
                .ToListAsync(cancellationToken);
            var inbox = inboxRows.Select(x => new HistoryProjection(
                "WEBHOOK", x.ObjectType ?? "notification", null, ToApiValue(x.Result), x.ReceivedAt, x.ProcessedAt));
            events.AddRange(inbox);
        }

        var includeCreated = (!fromUtc.HasValue || operation.RequestedAt >= fromUtc.Value) &&
                             (!toUtc.HasValue || operation.RequestedAt <= toUtc.Value);
        if (includeCreated)
            events.Add(new HistoryProjection("LOCAL", "DEBIT_CREATED", operation.ProviderState, null, operation.RequestedAt, null));
        var ordered = events
            .OrderByDescending(x => x.OccurredAt)
            .ToList();
        var total = eventCount + inboxCount + (includeCreated ? 1 : 0);
        var result = Page(filter, total, ordered.Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize).Select(MapHistory).ToList());
        return OperationResponse<PagedAuditResult<OperationalAuditEvent>>.SuccessResponse(result, total);
    }

    public async Task<OperationResponse<PagedAuditResult<CheckoutAuditItem>>> GetCheckoutsAsync(
        CheckoutAuditFilter filter,
        CancellationToken cancellationToken)
    {
        var validation = Validate(filter);
        if (validation is not null)
            return OperationResponse<PagedAuditResult<CheckoutAuditItem>>.BadRequestResponse(validation);

        var (fromUtc, toUtc) = DateRange(filter);
        var query = ApplyCheckoutFilters(_gatewayContext.GtwPagos.AsNoTracking(), filter, fromUtc, toUtc);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.PagFecha)
            .ThenByDescending(x => x.PagId)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new CheckoutProjection(
                x.PagId, x.PagIdJurisdiccion, x.PagIdExterno, x.PagEstado,
                x.PagEstadoDetalle, x.PagMoneda, x.PagImporteAbonado, x.PagImporteCancelado,
                x.PagOrigen, x.PagModoPago, x.PagFecha, x.GtwPagoDetalles.Count))
            .ToListAsync(cancellationToken);

        var result = Page(filter, total, rows.Select(MapCheckout).ToList());
        return OperationResponse<PagedAuditResult<CheckoutAuditItem>>.SuccessResponse(result, total);
    }

    public async Task<OperationResponse<CheckoutAuditDetail>> GetCheckoutAsync(int id, CancellationToken cancellationToken)
    {
        var row = await _gatewayContext.GtwPagos.AsNoTracking()
            .Where(x => x.PagId == id)
            .Select(x => new CheckoutProjection(
                x.PagId, x.PagIdJurisdiccion, x.PagIdExterno, x.PagEstado,
                x.PagEstadoDetalle, x.PagMoneda, x.PagImporteAbonado, x.PagImporteCancelado,
                x.PagOrigen, x.PagModoPago, x.PagFecha, x.GtwPagoDetalles.Count))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return OperationResponse<CheckoutAuditDetail>.NotFoundResponse();

        var details = await _gatewayContext.GtwPagoDetalles.AsNoTracking()
            .Where(x => x.PgdPagId == id)
            .OrderBy(x => x.PgdId)
            .Select(x => new CheckoutAuditLine(
                x.PgdId, x.PgdIdTributoContribuyente, x.PgdIdObligacion, x.PgdConcepto,
                x.PgdClaveBien, x.PgdAnoCuota, x.PgdNroCuota, x.PgdCapitalFacturado,
                x.PgdIntereses, x.PgdDeudaActualizada, x.PgdTipoCuota, x.PgdTipoTributo))
            .ToListAsync(cancellationToken);
        return OperationResponse<CheckoutAuditDetail>.SuccessResponse(new CheckoutAuditDetail(MapCheckout(row), details));
    }

    public async Task<OperationResponse<PagedAuditResult<OperationalAuditEvent>>> GetCheckoutHistoryAsync(
        int id, AuditHistoryFilter filter, CancellationToken cancellationToken)
    {
        var validation = Validate(filter);
        if (validation is not null)
            return OperationResponse<PagedAuditResult<OperationalAuditEvent>>.BadRequestResponse(validation);
        var checkout = await _gatewayContext.GtwPagos.AsNoTracking()
            .Where(x => x.PagId == id)
            .Select(x => new { x.PagId, x.PagEstado, x.PagFecha })
            .SingleOrDefaultAsync(cancellationToken);
        if (checkout is null) return OperationResponse<PagedAuditResult<OperationalAuditEvent>>.NotFoundResponse();

        var (fromUtc, toUtc) = DateRange(filter);
        var take = checked(filter.Page * filter.PageSize);
        var inboxQuery = ApplyPaymentInboxFilters(
            _gatewayContext.PayPerTicPaymentWebhookInbox.AsNoTracking(), id, fromUtc, toUtc);
        var inboxCount = await inboxQuery.CountAsync(cancellationToken);
        var inboxRows = await inboxQuery
            .OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        var rows = inboxRows.Select(x => new HistoryProjection(
            "WEBHOOK", "PAYMENT_NOTIFICATION", null, ToApiValue(x.Result), x.ReceivedAt, x.ProcessedAt)).ToList();
        var includeCreated = (!fromUtc.HasValue || checkout.PagFecha >= fromUtc.Value) &&
                             (!toUtc.HasValue || checkout.PagFecha <= toUtc.Value);
        if (includeCreated)
            rows.Add(new HistoryProjection("LOCAL", "CHECKOUT_CREATED", checkout.PagEstado, null, checkout.PagFecha, null));
        var ordered = rows
            .OrderByDescending(x => x.OccurredAt).ToList();
        var total = inboxCount + (includeCreated ? 1 : 0);
        var result = Page(filter, total, ordered.Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize).Select(MapHistory).ToList());
        return OperationResponse<PagedAuditResult<OperationalAuditEvent>>.SuccessResponse(result, total);
    }

    public async Task<OperationResponse<OperationalAuditSummary>> GetSummaryAsync(
        OperationalSummaryFilter filter,
        CancellationToken cancellationToken)
    {
        var validation = ValidateDateRange(filter.From, filter.To);
        if (validation is not null)
            return OperationResponse<OperationalAuditSummary>.BadRequestResponse(validation);

        var fromUtc = ToUtc(filter.From);
        var toUtc = ToUtc(filter.To);

        var debitQuery = ApplyAutomaticDebitSummaryFilters(
            _gatewayContext.AutomaticDebitOperations.AsNoTracking(), filter, fromUtc, toUtc);
        var debitTotal = await debitQuery.CountAsync(cancellationToken);
        var debitAmount = await debitQuery.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var debitPending = await debitQuery.CountAsync(x => x.ProcessingState == AutomaticDebitProcessingStates.Pending, cancellationToken);
        var debitStateRows = await debitQuery.GroupBy(x => x.ProviderState)
            .Select(x => new { State = x.Key, Count = x.Count() })
            .OrderBy(x => x.State)
            .ToListAsync(cancellationToken);
        var debitStates = debitStateRows.Select(x => new OperationalStateCount(x.State, x.Count)).ToList();

        var checkoutQuery = ApplyCheckoutSummaryFilters(
            _gatewayContext.GtwPagos.AsNoTracking(), filter, fromUtc, toUtc);
        var checkoutTotal = await checkoutQuery.CountAsync(cancellationToken);
        var checkoutPaid = await checkoutQuery.SumAsync(x => (decimal?)x.PagImporteAbonado, cancellationToken) ?? 0m;
        var checkoutCancelled = await checkoutQuery.SumAsync(x => (decimal?)x.PagImporteCancelado, cancellationToken) ?? 0m;
        var checkoutStateRows = await checkoutQuery.GroupBy(x => x.PagEstado)
            .Select(x => new { State = x.Key, Count = x.Count() })
            .OrderBy(x => x.State)
            .ToListAsync(cancellationToken);
        var checkoutStates = checkoutStateRows.Select(x => new OperationalStateCount(x.State, x.Count)).ToList();

        var debitInboxQuery = ApplyAutomaticDebitInboxSummaryFilters(
            _gatewayContext.AutomaticDebitWebhookInbox.AsNoTracking(), filter, fromUtc, toUtc);
        var debitInboxTotal = await debitInboxQuery.CountAsync(cancellationToken);
        var debitInboxResultRows = await debitInboxQuery.GroupBy(x => x.Result)
            .Select(x => new { State = x.Key, Count = x.Count() })
            .OrderBy(x => x.State)
            .ToListAsync(cancellationToken);
        var debitInboxResults = debitInboxResultRows.Select(x => new OperationalStateCount(ToApiValue(x.State), x.Count)).ToList();

        var checkoutInboxQuery = ApplyPaymentInboxSummaryFilters(
            _gatewayContext.PayPerTicPaymentWebhookInbox.AsNoTracking(), filter, fromUtc, toUtc);
        var checkoutInboxTotal = await checkoutInboxQuery.CountAsync(cancellationToken);
        var checkoutInboxResultRows = await checkoutInboxQuery.GroupBy(x => x.Result)
            .Select(x => new { State = x.Key, Count = x.Count() })
            .OrderBy(x => x.State)
            .ToListAsync(cancellationToken);
        var checkoutInboxResults = checkoutInboxResultRows.Select(x => new OperationalStateCount(ToApiValue(x.State), x.Count)).ToList();

        var summary = new OperationalAuditSummary(
            filter.JurisdictionId,
            FromUtcForResponse(fromUtc),
            FromUtcForResponse(toUtc),
            TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), ArgentinaTimeZone),
            new AutomaticDebitOperationalSummary(debitTotal, debitAmount, debitPending, debitStates),
            new CheckoutOperationalSummary(checkoutTotal, checkoutPaid, checkoutCancelled, checkoutStates),
            new InboxOperationalSummary(debitInboxTotal, debitInboxResults, checkoutInboxTotal, checkoutInboxResults));
        return OperationResponse<OperationalAuditSummary>.SuccessResponse(summary);
    }

    private static string? Validate(PagedAuditFilter filter)
    {
        if (filter.Page < 1) return "page must be greater than or equal to 1.";
        if (filter.PageSize < 1 || filter.PageSize > MaximumPageSize) return "pageSize must be between 1 and 100.";
        if (filter.Page > int.MaxValue / filter.PageSize) return "page is too large.";
        return ValidateDateRange(filter.From, filter.To);
    }

    private static string? ValidateDateRange(DateTime? from, DateTime? to) =>
        from.HasValue && to.HasValue && from > to ? "from must be earlier than or equal to to." : null;

    private static (DateTime? FromUtc, DateTime? ToUtc) DateRange(PagedAuditFilter filter) =>
        (ToUtc(filter.From), ToUtc(filter.To));

    private static DateTime? ToUtc(DateTime? value)
    {
        if (!value.HasValue) return null;
        var local = DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static readonly TimeZoneInfo ArgentinaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    private static DateTimeOffset? FromUtcForResponse(DateTime? value) =>
        value.HasValue ? AutomaticDebitApiDateTime.FromUtc(value.Value) : null;

    private static IQueryable<AutomaticDebitOperation> ApplyAutomaticDebitFilters(
        IQueryable<AutomaticDebitOperation> query,
        AutomaticDebitAuditFilter filter,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        if (filter.JurisdictionId.HasValue) query = query.Where(x => x.JurisdictionId == filter.JurisdictionId.Value);
        if (filter.AdhesionId.HasValue) query = query.Where(x => x.AdhesionId == filter.AdhesionId.Value);
        if (!string.IsNullOrWhiteSpace(filter.TaxpayerAccountId)) query = query.Where(x => x.TaxpayerAccountId == filter.TaxpayerAccountId.Trim());
        if (!string.IsNullOrWhiteSpace(filter.ObligationId)) query = query.Where(x => x.ObligationId == filter.ObligationId.Trim());
        if (!string.IsNullOrWhiteSpace(filter.ProviderTransactionId)) query = query.Where(x => x.ProviderTransactionId == filter.ProviderTransactionId.Trim());
        if (!string.IsNullOrWhiteSpace(filter.ExternalTransactionId)) query = query.Where(x => x.ExternalTransactionId == filter.ExternalTransactionId.Trim());
        if (!string.IsNullOrWhiteSpace(filter.ProviderState)) query = query.Where(x => x.ProviderState == filter.ProviderState.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(filter.ProcessingState))
        {
            if (Enum.TryParse<AutomaticDebitProcessingStates>(filter.ProcessingState.Trim(), true, out var state))
                query = query.Where(x => x.ProcessingState == state);
            else
                query = query.Where(_ => false);
        }
        if (fromUtc.HasValue) query = query.Where(x => x.RequestedAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.RequestedAt <= toUtc.Value);
        return query;
    }

    private static IQueryable<GtwPago> ApplyCheckoutFilters(
        IQueryable<GtwPago> query,
        CheckoutAuditFilter filter,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        if (filter.JurisdictionId.HasValue) query = query.Where(x => x.PagIdJurisdiccion == filter.JurisdictionId.Value);
        if (!string.IsNullOrWhiteSpace(filter.ProviderPaymentId)) query = query.Where(x => x.PagIdExterno == filter.ProviderPaymentId.Trim());
        if (!string.IsNullOrWhiteSpace(filter.State)) query = query.Where(x => x.PagEstado == filter.State.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Origin)) query = query.Where(x => x.PagOrigen == filter.Origin.Trim());
        if (!string.IsNullOrWhiteSpace(filter.TaxpayerAccountId)) query = query.Where(x => x.GtwPagoDetalles.Any(d => d.PgdIdTributoContribuyente == filter.TaxpayerAccountId.Trim()));
        if (!string.IsNullOrWhiteSpace(filter.ObligationId)) query = query.Where(x => x.GtwPagoDetalles.Any(d => d.PgdIdObligacion == filter.ObligationId.Trim()));
        if (fromUtc.HasValue) query = query.Where(x => x.PagFecha >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.PagFecha <= toUtc.Value);
        return query;
    }

    private static IQueryable<AutomaticDebitOperation> ApplyAutomaticDebitSummaryFilters(
        IQueryable<AutomaticDebitOperation> query,
        OperationalSummaryFilter filter,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        if (filter.JurisdictionId.HasValue) query = query.Where(x => x.JurisdictionId == filter.JurisdictionId.Value);
        if (fromUtc.HasValue) query = query.Where(x => x.RequestedAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.RequestedAt <= toUtc.Value);
        return query;
    }

    private static IQueryable<AutomaticDebitEvent> ApplyAutomaticDebitEventFilters(
        IQueryable<AutomaticDebitEvent> query, long operationId, DateTime? fromUtc, DateTime? toUtc)
    {
        query = query.Where(x => x.OperationId == operationId);
        if (fromUtc.HasValue) query = query.Where(x => x.OccurredAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.OccurredAt <= toUtc.Value);
        return query;
    }

    private static IQueryable<AutomaticDebitWebhookInbox> ApplyAutomaticDebitInboxFilters(
        IQueryable<AutomaticDebitWebhookInbox> query,
        long jurisdictionId,
        string providerObjectId,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        query = query.Where(x => x.JurisdictionId == jurisdictionId && x.ProviderObjectId == providerObjectId);
        if (fromUtc.HasValue) query = query.Where(x => x.ReceivedAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.ReceivedAt <= toUtc.Value);
        return query;
    }

    private static IQueryable<PayPerTicPaymentWebhookInbox> ApplyPaymentInboxFilters(
        IQueryable<PayPerTicPaymentWebhookInbox> query,
        int paymentId,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        query = query.Where(x => x.PaymentId == paymentId);
        if (fromUtc.HasValue) query = query.Where(x => x.ReceivedAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.ReceivedAt <= toUtc.Value);
        return query;
    }

    private static IQueryable<GtwPago> ApplyCheckoutSummaryFilters(
        IQueryable<GtwPago> query, OperationalSummaryFilter filter, DateTime? fromUtc, DateTime? toUtc)
    {
        if (filter.JurisdictionId.HasValue) query = query.Where(x => x.PagIdJurisdiccion == filter.JurisdictionId.Value);
        if (fromUtc.HasValue) query = query.Where(x => x.PagFecha >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.PagFecha <= toUtc.Value);
        return query;
    }

    private static IQueryable<AutomaticDebitWebhookInbox> ApplyAutomaticDebitInboxSummaryFilters(
        IQueryable<AutomaticDebitWebhookInbox> query,
        OperationalSummaryFilter filter,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        if (filter.JurisdictionId.HasValue) query = query.Where(x => x.JurisdictionId == filter.JurisdictionId.Value);
        if (fromUtc.HasValue) query = query.Where(x => x.ReceivedAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.ReceivedAt <= toUtc.Value);
        return query;
    }

    private static IQueryable<PayPerTicPaymentWebhookInbox> ApplyPaymentInboxSummaryFilters(
        IQueryable<PayPerTicPaymentWebhookInbox> query,
        OperationalSummaryFilter filter,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        if (filter.JurisdictionId.HasValue) query = query.Where(x => x.JurisdictionId == filter.JurisdictionId.Value);
        if (fromUtc.HasValue) query = query.Where(x => x.ReceivedAt >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.ReceivedAt <= toUtc.Value);
        return query;
    }

    private static string ToApiValue<TEnum>(TEnum value) where TEnum : struct, Enum =>
        value.ToString().ToUpperInvariant();

    private static PagedAuditResult<T> Page<T>(PagedAuditFilter filter, int total, IReadOnlyList<T> items) =>
        new(filter.Page, filter.PageSize, total, (int)Math.Ceiling(total / (double)filter.PageSize), items);

    private static AutomaticDebitAuditItem MapDebit(DebitProjection x) => new(
        x.Id, x.AdhesionId, x.JurisdictionId, x.TaxpayerAccountId, x.ObligationId,
        x.ProviderTransactionId, x.ExternalTransactionId, x.Amount, x.DueDate,
        x.ProviderState, ToApiValue(x.ProcessingState), AutomaticDebitApiDateTime.FromUtc(x.RequestedAt),
        AutomaticDebitApiDateTime.FromUtc(x.ProcessedAt), AutomaticDebitApiDateTime.FromUtc(x.ApprovedAt),
        AutomaticDebitApiDateTime.FromUtc(x.RejectedAt), x.RejectionReason);

    private static CheckoutAuditItem MapCheckout(CheckoutProjection x) => new(
        x.Id, x.JurisdictionId, x.ProviderPaymentId, x.State, x.StateDetail,
        x.Currency, x.PaidAmount, x.CancelledAmount, x.Origin, x.PaymentMode,
        AutomaticDebitApiDateTime.FromUtc(x.CreatedAt), x.DetailCount);

    private static OperationalAuditEvent MapHistory(HistoryProjection x) => new(
        x.Source, x.Type, x.State, x.Result, AutomaticDebitApiDateTime.FromUtc(x.OccurredAt),
        AutomaticDebitApiDateTime.FromUtc(x.ProcessedAt));

    private sealed record DebitProjection(long Id, long AdhesionId, long JurisdictionId,
        string TaxpayerAccountId, string ObligationId, string? ProviderTransactionId,
        string ExternalTransactionId, decimal Amount, DateTime? DueDate, string ProviderState,
        AutomaticDebitProcessingStates ProcessingState, DateTime RequestedAt, DateTime? ProcessedAt,
        DateTime? ApprovedAt, DateTime? RejectedAt, string? RejectionReason);

    private sealed record CheckoutProjection(int Id, long JurisdictionId, string? ProviderPaymentId,
        string State, string? StateDetail, string Currency, decimal PaidAmount, decimal CancelledAmount,
        string Origin, string PaymentMode, DateTime CreatedAt, int DetailCount);

    private sealed record HistoryProjection(string Source, string Type, string? State, string? Result,
        DateTime OccurredAt, DateTime? ProcessedAt);
}

