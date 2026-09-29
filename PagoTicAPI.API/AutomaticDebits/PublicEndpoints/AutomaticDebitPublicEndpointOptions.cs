using System.ComponentModel.DataAnnotations;

namespace PagoTicAPI.API.AutomaticDebits.PublicEndpoints;

public sealed class AutomaticDebitPublicEndpointOptions
{
    public const string SectionName = "AutomaticDebitPublicEndpoints";
    public const string RateLimitPolicy = "AutomaticDebitPublicEndpoints";

    public AutomaticDebitPublicRateLimitOptions RateLimit { get; set; } = new();
}

public sealed class AutomaticDebitPublicRateLimitOptions
{
    [Range(1, 1_000)]
    public int PermitLimit { get; set; } = 30;

    [Range(1, 3_600)]
    public int WindowSeconds { get; set; } = 60;
}
