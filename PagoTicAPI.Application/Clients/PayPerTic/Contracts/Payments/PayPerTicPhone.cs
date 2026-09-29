using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Teléfono de contacto del pagador.
/// </summary>
public class PayPerTicPhone
{
    [JsonPropertyName("country_code")]
    public int? CountryCode { get; set; }

    [JsonPropertyName("area_code")]
    public int? AreaCode { get; set; }

    [JsonPropertyName("number")]
    public int? Number { get; set; }
}
