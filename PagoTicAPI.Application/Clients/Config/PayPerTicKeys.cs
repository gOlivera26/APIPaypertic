namespace PagoTicAPI.Application.Clients.Config;

/// <summary>
/// Credenciales y URLs de configuración para la pasarela PayPerTIC.
/// Se vincula desde la sección "PayPerTicKeys" del appsettings.json.
/// </summary>
public class PayPerTicKeys
{
    /// <summary>URL del endpoint OAuth2 de PayPerTIC para obtener el token.
    /// Ej: https://a.paypertic.com/auth/realms/entidades/protocol/openid-connect/token</summary>
    public string AuthUrl { get; set; } = string.Empty;

    /// <summary>URL base de la API de pagos de PayPerTIC.
    /// Ej: https://api.paypertic.com</summary>
    public string ApiUrl { get; set; } = string.Empty;

    /// <summary>Usuario de la cuenta PayPerTIC.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Contraseña de la cuenta PayPerTIC.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Client ID de la aplicación OAuth2 registrada en PayPerTIC.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client Secret de la aplicación OAuth2 registrada en PayPerTIC.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Identificador del recaudador (entidad) en PayPerTIC.</summary>
    public string CollectorId { get; set; } = string.Empty;

    /// <summary>URL donde PayPerTIC enviará las notificaciones webhook del estado de pago.</summary>
    public string NotificationUrl { get; set; } = string.Empty;

    /// <summary>URL de retorno luego de que el pagador complete el formulario.</summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>URL de retorno cuando el pagador hace clic en "volver".</summary>
    public string BackUrl { get; set; } = string.Empty;
}
