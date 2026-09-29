using PagoTicAPI.API.AutomaticDebits.Infrastructure;
using PagoTicAPI.API.Middlewares;
using PagoTicAPI.Application.Clients.AutomaticDebits;
using PagoTicAPI.Application.Configuration.AutomaticDebits;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;
using PagoTicAPI.Application.Services.Implementations;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;
using PagoTicAPI.Application.Scheduling.AutomaticDebits;
using PagoTicAPI.API.AutomaticDebits.Scheduling;
using PagoTicAPI.API.AutomaticDebits.Webhooks;
using PagoTicAPI.API.AutomaticDebits.PublicEndpoints;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace PagoTicAPI.API.Config;

public static class AutomaticDebitConfig
{
    public static IServiceCollection AddAutomaticDebit(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Gateway");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The Gateway connection string is required for the automatic-debit module.");
        }

        services.Configure<AutomaticDebitFeatureOptions>(options =>
            options.Enabled = configuration.GetValue<bool>("Features:AutomaticDebit"));
        services.AddOptions<AutomaticDebitSchedulerOptions>()
            .Bind(configuration.GetSection(AutomaticDebitSchedulerOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.LeaseSeconds > options.ExecutionTimeoutSeconds,
                "AutomaticDebitScheduler:LeaseSeconds must be greater than ExecutionTimeoutSeconds.")
            .ValidateOnStart();
        services.AddOptions<AutomaticDebitWebhookEndpointOptions>()
            .Bind(configuration.GetSection(AutomaticDebitWebhookEndpointOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options =>
                    options.RateLimit is not null &&
                    options.RateLimit.PermitLimit is >= 1 and <= 10_000 &&
                    options.RateLimit.WindowSeconds is >= 1 and <= 3_600,
                "AutomaticDebitWebhook:RateLimit contains invalid values.")
            .ValidateOnStart();
        services.AddOptions<AutomaticDebitPublicEndpointOptions>()
            .Bind(configuration.GetSection(AutomaticDebitPublicEndpointOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options =>
                    options.RateLimit is not null &&
                    options.RateLimit.PermitLimit is >= 1 and <= 1_000 &&
                    options.RateLimit.WindowSeconds is >= 1 and <= 3_600,
                "AutomaticDebitPublicEndpoints:RateLimit contains invalid values.")
            .ValidateOnStart();
        services.AddRateLimiter(options =>
        {
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                if (context.HttpContext.Request.Path.StartsWithSegments(
                        "/api/debitos-automaticos/notificaciones"))
                {
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        AutomaticDebitWebhookProcessingResult.Rejected(429, "rate_limited"),
                        cancellationToken);
                    return;
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    OperationResponse<object>.CustomErrorResponse(
                        StatusCodes.Status429TooManyRequests,
                        "Se excedió el límite de solicitudes. Intente nuevamente más tarde."),
                    cancellationToken);
            };
            options.AddPolicy(AutomaticDebitWebhookEndpointOptions.RateLimitPolicy, httpContext =>
            {
                var webhookOptions = httpContext.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<AutomaticDebitWebhookEndpointOptions>>()
                    .Value;
                return
                RateLimitPartition.GetFixedWindowLimiter(
                    $"{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}:{httpContext.Request.RouteValues["idJurisdiccion"]}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = webhookOptions.RateLimit.PermitLimit,
                        Window = TimeSpan.FromSeconds(webhookOptions.RateLimit.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
            options.AddPolicy(AutomaticDebitPublicEndpointOptions.RateLimitPolicy, httpContext =>
            {
                var endpointOptions = httpContext.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<AutomaticDebitPublicEndpointOptions>>()
                    .Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = endpointOptions.RateLimit.PermitLimit,
                        Window = TimeSpan.FromSeconds(endpointOptions.RateLimit.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAutomaticDebitAdhesionCreationLock, AutomaticDebitAdhesionCreationLock>();
        services.AddSingleton<IAutomaticDebitOperationCreationLock, AutomaticDebitOperationCreationLock>();
        services.AddScoped<IJurisdictionPayPerTicConfigurationProvider, JurisdictionPayPerTicConfigurationProvider>();
        services.AddScoped<ITributaryAccountReader, OracleTributaryAccountReader>();
        services.AddScoped<IAutomaticDebitObligationReader, OracleAutomaticDebitObligationReader>();
        services.AddScoped<OracleAutomaticDebitIdGenerator>();
        services.AddScoped<IAutomaticDebitIdGenerator>(provider =>
            provider.GetRequiredService<OracleAutomaticDebitIdGenerator>());
        services.AddScoped<IAutomaticDebitWebhookIdGenerator>(provider =>
            provider.GetRequiredService<OracleAutomaticDebitIdGenerator>());
        services.AddScoped<IPayPerTicJurisdictionTokenProvider, PayPerTicJurisdictionTokenProvider>();
        services.AddHttpClient<IPayPerTicTokenAcquirer, PayPerTicTokenAcquirer>();
        services.AddHttpClient<IAutomaticDebitPayPerTicClient, AutomaticDebitPayPerTicClient>();
        services.AddScoped<IAutomaticDebitAdhesionService, AutomaticDebitAdhesionService>();
        services.AddScoped<IAutomaticDebitGenerationService, AutomaticDebitGenerationService>();
        services.AddScoped<IAutomaticDebitWebhookProcessor, AutomaticDebitWebhookProcessor>();
        services.AddScoped<IAutomaticDebitSchedulerLease, OracleAutomaticDebitSchedulerLease>();
        services.AddScoped<IAutomaticDebitCandidateSource, DisabledAutomaticDebitCandidateSource>();
        services.AddScoped<IAutomaticDebitSchedulerOrchestrator, AutomaticDebitSchedulerOrchestrator>();
        services.AddHostedService<AutomaticDebitSchedulerHostedService>();
        return services;
    }

    public static IApplicationBuilder UseAutomaticDebitFeatureGate(this IApplicationBuilder app) =>
        app.UseMiddleware<AutomaticDebitFeatureGateMiddleware>();
}

