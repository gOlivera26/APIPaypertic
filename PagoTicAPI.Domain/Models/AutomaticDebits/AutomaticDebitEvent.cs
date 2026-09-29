namespace PagoTicAPI.Domain.Models.AutomaticDebits;

public class AutomaticDebitEvent
{
    public long Id { get; set; }
    public long JurisdictionId { get; set; }
    public long? AdhesionId { get; set; }
    public long? OperationId { get; set; }
    public string DeduplicationKey { get; set; } = string.Empty;
    public string ProviderObjectId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ProviderState { get; set; } = string.Empty;
    public string AuthoritativeHash { get; set; } = string.Empty;
    public DateTime? AuthoritativeUpdatedAt { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
