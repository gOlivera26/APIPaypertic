namespace PagoTicAPI.Application.Clients.AutomaticDebits;

public sealed record AutomaticDebitProviderReadBack(string ObjectType, string Id, string Status, string CollectorId, string ExternalReference, DateTimeOffset? LastUpdateDate)
{
    public string? PaymentType { get; init; }
    public string? PaymentCurrencyId { get; init; }
    public decimal? DetailAmount { get; init; }
    public string? DetailExternalReference { get; init; }
    public string? DetailConceptId { get; init; }
    public string? DetailConceptDescription { get; init; }
}
