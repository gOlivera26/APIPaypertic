namespace PagoTicAPI.Application.RequestDto.OperationalAudit;

public abstract class PagedAuditFilter
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public long? JurisdictionId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}

public sealed class AutomaticDebitAuditFilter : PagedAuditFilter
{
    public long? AdhesionId { get; init; }
    public string? TaxpayerAccountId { get; init; }
    public string? ObligationId { get; init; }
    public string? ProviderTransactionId { get; init; }
    public string? ExternalTransactionId { get; init; }
    public string? ProviderState { get; init; }
    public string? ProcessingState { get; init; }
}

public sealed class CheckoutAuditFilter : PagedAuditFilter
{
    public string? TaxpayerAccountId { get; init; }
    public string? ObligationId { get; init; }
    public string? ProviderPaymentId { get; init; }
    public string? State { get; init; }
    public string? Origin { get; init; }
}

public sealed class AuditHistoryFilter : PagedAuditFilter;

public sealed class OperationalSummaryFilter
{
    public long? JurisdictionId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}
