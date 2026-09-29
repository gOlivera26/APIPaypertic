using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Documento de identidad del pagador.
/// </summary>
public class PayPerTicIdentification
{
    /// <summary>Tipo de documento. Ej: "DNI_ARG", "CUIT_ARG".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "DNI_ARG";

    /// <summary>Número de documento sin espacios ni guiones.</summary>
    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    /// <summary>Código ISO 3166-1 alpha-3 del país. Ej: "ARG".</summary>
    [JsonPropertyName("country")]
    public string Country { get; set; } = "ARG";
}
