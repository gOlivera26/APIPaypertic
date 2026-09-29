using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Request que se envía a PayPerTIC para crear un nuevo pago.
/// Endpoint: POST https://api.paypertic.com/pagos
/// </summary>
public class PayPerTicCreatePaymentRequest
{
    /// <summary>
    /// ID único del pago en el sistema propio.
    /// Debe ser único por cada intento de pago.
    /// </summary>
    [JsonPropertyName("external_transaction_id")]
    public string ExternalTransactionId { get; set; } = string.Empty;

    /// <summary>Código ISO 4217 de moneda. Ej: "ARS".</summary>
    [JsonPropertyName("currency_id")]
    public string CurrencyId { get; set; } = "ARS";

    /// <summary>Líneas de detalle del pago. Al menos una es obligatoria.</summary>
    [JsonPropertyName("details")]
    public List<PayPerTicPaymentDetail> Details { get; set; } = new();

    /// <summary>Información del pagador.</summary>
    [JsonPropertyName("payer")]
    public PayPerTicPayer Payer { get; set; } = new();

    /// <summary>Fecha de vencimiento. Formato: yyyy-MM-dd'T'HH:mm:ssZ</summary>
    [JsonPropertyName("due_date")]
    public string? DueDate { get; set; }

    /// <summary>Fecha límite final de pago.</summary>
    [JsonPropertyName("last_due_date")]
    public string? LastDueDate { get; set; }

    /// <summary>URL donde PayPerTIC enviará las notificaciones de estado (webhook).</summary>
    [JsonPropertyName("notification_url")]
    public string? NotificationUrl { get; set; }

    /// <summary>URL de retorno luego de completar el pago.</summary>
    [JsonPropertyName("return_url")]
    public string? ReturnUrl { get; set; }

    /// <summary>URL de retorno cuando el pagador hace clic en "volver".</summary>
    [JsonPropertyName("back_url")]
    public string? BackUrl { get; set; }

    /// <summary>
    /// Tipo de pago. Valores posibles: "debit", "online", "transfer", "debin", "coupon".
    /// Si no se envía, PayPerTIC muestra todos los medios disponibles.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Identificador del recaudador (entidad). Si no se envía, se usa el del token.</summary>
    [JsonPropertyName("collector_id")]
    public string? CollectorId { get; set; }

    /// <summary>Medios de pago pre-seleccionados (opcional).</summary>
    [JsonPropertyName("payment_methods")]
    public List<PayPerTicPaymentMethod>? PaymentMethods { get; set; }

    /// <summary>Datos adicionales en formato JSON libre.</summary>
    [JsonPropertyName("metadata")]
    public object? Metadata { get; set; }
}
