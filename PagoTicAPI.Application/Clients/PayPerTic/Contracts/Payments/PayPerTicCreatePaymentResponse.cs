using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Respuesta de PayPerTIC al crear un pago.
/// Los campos más importantes son <see cref="Id"/> y <see cref="FormUrl"/>.
/// </summary>
public class PayPerTicCreatePaymentResponse
{
    /// <summary>ID del pago en PayPerTIC (UUID). Se almacena en GtwPago.PagIdExterno.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// URL del formulario de pago hosted de PayPerTIC.
    /// El pagador debe ser redirigido a esta URL para completar el pago.
    /// Se almacena en GtwPago.PagFormUrl.
    /// </summary>
    [JsonPropertyName("form_url")]
    public string FormUrl { get; set; } = string.Empty;

    /// <summary>Importe total del pago.</summary>
    [JsonPropertyName("final_amount")]
    public decimal FinalAmount { get; set; }

    /// <summary>Estado inicial del pago. Normalmente "pending".</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>El external_transaction_id que se envió en el request.</summary>
    [JsonPropertyName("external_transaction_id")]
    public string ExternalTransactionId { get; set; } = string.Empty;

    [JsonPropertyName("request_date")]
    public string? RequestDate { get; set; }

    [JsonPropertyName("due_date")]
    public string? DueDate { get; set; }

    [JsonPropertyName("last_update_date")]
    public string? LastUpdateDate { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("validation")]
    public bool? Validation { get; set; }

    [JsonPropertyName("review")]
    public bool? Review { get; set; }
}
