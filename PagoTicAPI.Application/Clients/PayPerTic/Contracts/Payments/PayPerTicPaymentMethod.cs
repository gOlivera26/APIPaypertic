using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Medio de pago pre-seleccionado que se envía opcionalmente al crear un pago.
/// Ver tabla de media_payment_id en la documentación de PayPerTIC.
/// Ej: 5=Mastercard, 9=Visa, 10=Visa Débito, 97=Rapipago, 98=Pagofácil.
/// </summary>
public class PayPerTicPaymentMethod
{
    /// <summary>Código del medio de pago según la tabla de PayPerTIC.</summary>
    [JsonPropertyName("media_payment_id")]
    public int MediaPaymentId { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    [JsonPropertyName("installments")]
    public int? Installments { get; set; }
}
