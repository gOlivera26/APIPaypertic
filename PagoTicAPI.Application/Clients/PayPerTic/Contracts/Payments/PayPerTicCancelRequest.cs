using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Request para cancelar un pago en PayPerTIC.
/// Endpoint: POST https://api.paypertic.com/pagos/cancelar/{payment_id}
/// Solo aplica a pagos en estado "pending" o "issued".
/// </summary>
public class PayPerTicCancelRequest
{
    /// <summary>Motivo de la cancelación. Campo obligatorio.</summary>
    [JsonPropertyName("status_detail")]
    public string StatusDetail { get; set; } = string.Empty;
}
