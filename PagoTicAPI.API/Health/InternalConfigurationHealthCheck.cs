using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PagoTicAPI.API.Health;

public sealed class InternalConfigurationHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        var missingKeys = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Gateway")))
        {
            missingKeys.Add("ConnectionStrings:Gateway");
        }

        if (string.IsNullOrWhiteSpace(configuration["DBSCHEMA"]))
        {
            missingKeys.Add("DBSCHEMA");
        }

        var result = missingKeys.Count == 0
            ? HealthCheckResult.Healthy("Internal configuration is available.")
            : HealthCheckResult.Unhealthy(
                $"Required internal configuration is missing: {string.Join(", ", missingKeys)}.");

        return Task.FromResult(result);
    }
}
