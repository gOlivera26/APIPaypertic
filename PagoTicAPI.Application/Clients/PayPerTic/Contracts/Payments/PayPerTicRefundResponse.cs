using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Respuesta de PayPerTIC al solicitar una devolución.
/// </summary>
public class PayPerTicRefundResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Estado de la devolución: "approved" o "rejected".</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Tipo de devolución: "manual", "online" o "by_media_payment".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("status_detail")]
    public string? StatusDetail { get; set; }

    /// <summary>ID del pago original al que aplica la devolución.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("external_reference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}
