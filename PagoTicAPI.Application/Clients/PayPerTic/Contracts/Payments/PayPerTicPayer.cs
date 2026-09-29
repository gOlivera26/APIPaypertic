using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Información del pagador que se envía a PayPerTIC.
/// </summary>
public class PayPerTicPayer
{
    /// <summary>Nombre completo o razón social.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Referencia del pagador en el sistema propio (ej. DNI, CUIT, código interno).</summary>
    [JsonPropertyName("external_reference")]
    public string? ExternalReference { get; set; }

    [JsonPropertyName("identification")]
    public PayPerTicIdentification Identification { get; set; } = new();

    [JsonPropertyName("phones")]
    public List<PayPerTicPhone>? Phones { get; set; }
}
