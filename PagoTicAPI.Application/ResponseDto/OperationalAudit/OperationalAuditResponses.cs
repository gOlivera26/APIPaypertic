namespace PagoTicAPI.Application.ResponseDto.OperationalAudit;

public sealed record PagedAuditResult<T>(
    int Page,
    int PageSize,
    int TotalRows,
    int TotalPages,
    IReadOnlyList<T> Items);

public sealed record AutomaticDebitAuditItem(
    long Id,
    long AdhesionId,
    long JurisdictionId,
    string TaxpayerAccountId,
    string ObligationId,
    string? ProviderTransactionId,
    string ExternalTransactionId,
    decimal Amount,
    DateTime? DueDate,
    string ProviderState,
    string ProcessingState,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    string? RejectionReason);

public sealed record CheckoutAuditItem(
    int Id,
    long JurisdictionId,
    string? ProviderPaymentId,
    string State,
    string? StateDetail,
    string Currency,
    decimal PaidAmount,
    decimal CancelledAmount,
    string Origin,
    string PaymentMode,
    DateTimeOffset CreatedAt,
    int DetailCount);

public sealed record CheckoutAuditDetail(
    CheckoutAuditItem Checkout,
    IReadOnlyList<CheckoutAuditLine> Details);

public sealed record CheckoutAuditLine(
    int Id,
    string TaxpayerAccountId,
    string ObligationId,
    string Concept,
    string AssetKey,
    int Year,
    int Installment,
    decimal InvoicedCapital,
    decimal Interest,
    decimal UpdatedDebt,
    string? InstallmentType,
    string? TaxType);

public sealed record OperationalAuditEvent(
    string Source,
    string Type,
    string? State,
    string? Result,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ProcessedAt);

public sealed record OperationalStateCount(string State, int Count);

public sealed record AutomaticDebitOperationalSummary(
    int Total,
    decimal TotalAmount,
    int PendingProcessing,
    IReadOnlyList<OperationalStateCount> ByProviderState);

public sealed record CheckoutOperationalSummary(
    int Total,
    decimal TotalPaidAmount,
    decimal TotalCancelledAmount,
    IReadOnlyList<OperationalStateCount> ByState);

public sealed record InboxOperationalSummary(
    int AutomaticDebitTotal,
    IReadOnlyList<OperationalStateCount> AutomaticDebitByResult,
    int CheckoutTotal,
    IReadOnlyList<OperationalStateCount> CheckoutByResult);

public sealed record OperationalAuditSummary(
    long? JurisdictionId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    DateTimeOffset GeneratedAt,
    AutomaticDebitOperationalSummary AutomaticDebits,
    CheckoutOperationalSummary Checkouts,
    InboxOperationalSummary Inboxes);
