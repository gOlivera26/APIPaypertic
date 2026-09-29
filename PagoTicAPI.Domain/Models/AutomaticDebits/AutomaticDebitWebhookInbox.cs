namespace PagoTicAPI.Domain.Models.AutomaticDebits;

public class AutomaticDebitWebhookInbox
{
    public long Id { get; set; }
    public long JurisdictionId { get; set; }
    public string DeduplicationKey { get; set; } = string.Empty;
    public string? ProviderObjectId { get; set; }
    public string? ObjectType { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public string? RawPayload { get; set; }
    public string? AuthoritativeHash { get; set; }
    public DateTime? AuthoritativeUpdatedAt { get; set; }
    public string? SanitizedMetadata { get; set; }
    public AutomaticDebitInboxResults Result { get; set; } = AutomaticDebitInboxResults.Received;
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
