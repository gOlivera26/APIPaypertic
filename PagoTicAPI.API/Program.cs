var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConfig(builder.Configuration);
builder.Services.AddOpenTelemetryTracing(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddCheck<PagoTicAPI.API.Health.InternalConfigurationHealthCheck>("internal_configuration",
        tags: ["ready"])
    .AddCheck<PagoTicAPI.API.Health.OracleReadinessHealthCheck>("oracle",
        tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

app.UseOpenTelemetry(builder.Configuration);

if (builder.Configuration.GetValue<bool>("Uses:UseSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors();

app.UseRateLimiter();

app.UseAutomaticDebitFeatureGate();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();

// Backward-compatible readiness endpoint.
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();

app.Run();

public partial class Program { }
