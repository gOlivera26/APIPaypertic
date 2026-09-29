using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Auth;

/// <summary>
/// Respuesta del endpoint OAuth2 de PayPerTIC.
/// El access_token se usa como Bearer en todas las llamadas posteriores a la API.
/// </summary>
public class PayPerTicTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Tiempo de expiración del token en segundos.</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_expires_in")]
    public int RefreshExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>Siempre "Bearer".</summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
}
