using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PagoTicAPI.API.Health;

public sealed class OracleReadinessHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<OracleReadinessHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<gtwContext>();
            return await context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Oracle is reachable.")
                : HealthCheckResult.Unhealthy("Oracle is not reachable.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falló la verificación de disponibilidad de Oracle.");
            return HealthCheckResult.Unhealthy("Falló la verificación de disponibilidad de Oracle.");
        }
    }
}
