namespace PagoTicAPI.Application.ResponseDto.AutomaticDebits;

public sealed record AutomaticDebitAdhesionDto(
    long Id,
    long JurisdictionId,
    string TaxpayerAccountId,
    string PersonId,
    string? ProviderAdhesionId,
    string? FormUrl,
    string ProviderState,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CancelledAt);

public sealed record AutomaticDebitOperationDto(
    long Id,
    long JurisdictionId,
    string TaxpayerAccountId,
    string ObligationId,
    string? ProviderTransactionId,
    string ExternalTransactionId,
    decimal Amount,
    DateTime? DueDate,
    string ProviderState,
    string ProcessingState,
    DateTimeOffset RequestedAt);

public static class AutomaticDebitApiDateTime
{
    private static readonly TimeZoneInfo ArgentinaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    public static DateTimeOffset FromUtc(DateTime value)
    {
        var utcValue = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utcValue), ArgentinaTimeZone);
    }

    public static DateTimeOffset? FromUtc(DateTime? value) =>
        value.HasValue ? FromUtc(value.Value) : null;
}
