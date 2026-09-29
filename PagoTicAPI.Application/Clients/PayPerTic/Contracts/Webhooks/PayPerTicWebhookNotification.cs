using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Webhooks;

/// <summary>
/// Notificación webhook que PayPerTIC envía vía POST a la notification_url
/// cada vez que el estado de un pago cambia.
/// El campo clave para correlacionar con el pago local es <see cref="ExternalTransactionId"/>.
/// </summary>
public class PayPerTicWebhookNotification
{
    /// <summary>ID del pago en PayPerTIC (UUID). Equivale a GtwPago.PagIdExterno.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Importe total del pago.</summary>
    [JsonPropertyName("final_amount")]
    public decimal? FinalAmount { get; set; }

    /// <summary>
    /// Estado actual del pago en PayPerTIC.
    /// Valores posibles: pending, issued, in_process, approved, rejected, cancelled, refunded.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Detalle adicional del estado (ej. motivo de rechazo).</summary>
    [JsonPropertyName("status_detail")]
    public string? StatusDetail { get; set; }

    /// <summary>Tipo de pago: debit, online, transfer, debin, coupon.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// ID del pago en el sistema propio.
    /// Se usa para identificar el GtwPago correspondiente (PagIdExterno o referencia externa).
    /// </summary>
    [JsonPropertyName("external_transaction_id")]
    public string? ExternalTransactionId { get; set; }

    /// <summary>Código ISO 4217 de moneda. Ej: "ARS".</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("request_date")]
    public string? RequestDate { get; set; }

    [JsonPropertyName("rejected_date")]
    public string? RejectedDate { get; set; }

    [JsonPropertyName("due_date")]
    public string? DueDate { get; set; }

    [JsonPropertyName("collector")]
    public PayPerTicWebhookCollector? Collector { get; set; }

    [JsonPropertyName("payer")]
    public PayPerTicWebhookPayer? Payer { get; set; }

    /// <summary>Datos adicionales adjuntados al crear el pago (metadata).</summary>
    [JsonPropertyName("metadata")]
    public object? Metadata { get; set; }

    /// <summary>Detalle de los medios de pago usados.</summary>
    [JsonPropertyName("payment_methods")]
    public List<PayPerTicWebhookPaymentMethod>? PaymentMethods { get; set; }
}

/// <summary>Información del recaudador en el webhook.</summary>
public class PayPerTicWebhookCollector
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>Información del pagador en el webhook.</summary>
public class PayPerTicWebhookPayer
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>Referencia del pagador en el sistema propio (ej. DNI).</summary>
    [JsonPropertyName("external_reference")]
    public string? ExternalReference { get; set; }
}

/// <summary>Detalle del medio de pago usado, incluido en el webhook.</summary>
public class PayPerTicWebhookPaymentMethod
{
    /// <summary>Código del medio de pago. Ej: 5=Mastercard, 9=Visa, 98=Pagofácil.</summary>
    [JsonPropertyName("media_payment_id")]
    public int? MediaPaymentId { get; set; }

    /// <summary>Descripción del medio. Ej: "VISA CREDIT", "MASTERCARD DEBIT".</summary>
    [JsonPropertyName("media_payment_detail")]
    public string? MediaPaymentDetail { get; set; }

    /// <summary>Últimos 4 dígitos de la tarjeta (si aplica).</summary>
    [JsonPropertyName("last_four_digits")]
    public string? LastFourDigits { get; set; }

    /// <summary>Primeros 6 dígitos de la tarjeta (BIN).</summary>
    [JsonPropertyName("first_six_digits")]
    public string? FirstSixDigits { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }
}
