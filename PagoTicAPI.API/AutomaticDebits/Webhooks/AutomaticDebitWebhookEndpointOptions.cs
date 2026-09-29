using System.ComponentModel.DataAnnotations;

namespace PagoTicAPI.API.AutomaticDebits.Webhooks;

public sealed class AutomaticDebitWebhookEndpointOptions
{
    public const string SectionName = "AutomaticDebitWebhook";
    public const string RateLimitPolicy = "AutomaticDebitWebhook";

    [Range(1024, 1024 * 1024)]
    public int MaxBodySizeBytes { get; set; } = 65_536;

    public AutomaticDebitWebhookRateLimitOptions RateLimit { get; set; } = new();
}

public sealed class AutomaticDebitWebhookRateLimitOptions
{
    [Range(1, 10_000)]
    public int PermitLimit { get; set; } = 60;

    [Range(1, 3_600)]
    public int WindowSeconds { get; set; } = 60;
}
