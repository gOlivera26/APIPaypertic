namespace PagoTicAPI.Domain.Models.AutomaticDebits;

public class AutomaticDebitOperation
{
    public long Id { get; set; }
    public long AdhesionId { get; set; }
    public long JurisdictionId { get; set; }
    public string TaxpayerAccountId { get; set; } = string.Empty;
    public string ObligationId { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string ExternalTransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime? DueDate { get; set; }
    public string ProviderState { get; set; } = AutomaticDebitProviderStates.Issued;
    public AutomaticDebitProcessingStates ProcessingState { get; set; } = AutomaticDebitProcessingStates.Pending;
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }

    public AutomaticDebitAdhesion? Adhesion { get; set; }
}
