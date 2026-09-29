namespace PagoTicAPI.Domain.Models.AutomaticDebits;

public class PayPerTicConfiguration
{
    public long Id { get; set; }
    public long JurisdictionId { get; set; }
    public string AuthUrl { get; set; } = string.Empty;
    public string ApiUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string CollectorId { get; set; } = string.Empty;
    public string? NotificationUrl { get; set; }
    public string? ReturnUrl { get; set; }
    public string? BackUrl { get; set; }
    public bool IsActive { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
