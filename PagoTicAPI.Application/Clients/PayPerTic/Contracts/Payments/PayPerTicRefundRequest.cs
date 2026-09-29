using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Request para realizar una devolución (refund) en PayPerTIC.
/// Endpoint: POST https://api.paypertic.com/pagos/devolucion/{payment_id}
/// </summary>
public class PayPerTicRefundRequest
{
    /// <summary>
    /// Tipo de devolución. "online" = devolución automática al medio de pago original.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "online";

    [JsonPropertyName("status_detail")]
    public string? StatusDetail { get; set; }

    /// <summary>Referencia externa de la entidad para esta devolución.</summary>
    [JsonPropertyName("external_reference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("metadata")]
    public object? Metadata { get; set; }
}
