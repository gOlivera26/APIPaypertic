namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Auth;

/// <summary>
/// Datos necesarios para autenticarse contra el endpoint OAuth2 de PayPerTIC.
/// Se envía como application/x-www-form-urlencoded.
/// Endpoint: POST https://a.paypertic.com/auth/realms/entidades/protocol/openid-connect/token
/// </summary>
public class PayPerTicTokenRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Siempre "password" para el flujo Resource Owner Password Credentials.</summary>
    public string GrantType { get; set; } = "password";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
