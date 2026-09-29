namespace PagoTicAPI.Domain.Models.AutomaticDebits;

public class AutomaticDebitAdhesion
{
    public long Id { get; set; }
    public long JurisdictionId { get; set; }
    public string TaxpayerAccountId { get; set; } = string.Empty;
    public string PersonId { get; set; } = string.Empty;
    public string? ProviderAdhesionId { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public string? FormUrl { get; set; }
    public string ProviderState { get; set; } = AutomaticDebitProviderStates.Pending;
    public AutomaticDebitProcessingStates ProcessingState { get; set; } = AutomaticDebitProcessingStates.Pending;
    public string? PaymentMethod { get; set; }
    public string? LastDigits { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? DeactivatedBy { get; set; }
    public DateTime? DeactivatedAt { get; set; }
}
