namespace PagoTicAPI.Domain.Models;

public sealed class PayPerTicPaymentWebhookInbox
{
    public long Id { get; set; }
    public long JurisdictionId { get; set; }
    public int PaymentId { get; set; }
    public string ProviderPaymentId { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public PayPerTicPaymentWebhookInboxResults Result { get; set; } = PayPerTicPaymentWebhookInboxResults.Received;
    public int Attempts { get; set; }
    public string? FailureCode { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public enum PayPerTicPaymentWebhookInboxResults
{
    Received,
    Processed,
    Rejected,
    Failed
}
