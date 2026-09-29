namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

public sealed record JurisdictionPayPerTicConfiguration(long JurisdictionId, string AuthUrl, string ApiUrl, string Username, string Password, string ClientId, string ClientSecret, string CollectorId, string? NotificationUrl, string? ReturnUrl, string? BackUrl);
