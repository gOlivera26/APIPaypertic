using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PagoTicAPI.API;
using PagoTicAPI.Application.Configuration.AutomaticDebits;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;
using PagoTicAPI.Application.Clients.Interfaces;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Domain.Context;
using PagoTicAPI.Tests.Helpers;

namespace PagoTicAPI.Tests.Integration.Controllers;

/// <summary>
/// WebApplicationFactory base para tests de integración de controllers.
///
/// Estrategia:
///   - Mockea IPayPerTicService → prueba el pipeline HTTP (routing, auth, serialización)
///     sin depender del DbContext Oracle ni de PayPerTIC real.
///   - Auth JWT reemplazada por TestAuthHandler (siempre autentica, sin token real).
///   - El DbContext Oracle también se reemplaza por InMemory para evitar errores de startup.
/// </summary>
public class IntegrationTestBase : WebApplicationFactory<Program>
{
    /// <summary>Mock de IPayPerTicService, configurable por cada test.</summary>
    public Mock<IPayPerTicService> MockPayPerTicService { get; } = new();
    public Mock<IAutomaticDebitAdhesionService> MockAutomaticDebitAdhesionService { get; } = new();
    public Mock<IAutomaticDebitGenerationService> MockAutomaticDebitGenerationService { get; } = new();
    public Mock<IAutomaticDebitWebhookProcessor> MockAutomaticDebitWebhookProcessor { get; } = new();
    public Mock<IJurisdictionPayPerTicConfigurationProvider> MockAutomaticDebitConfigurationProvider { get; } = new();
    public Mock<IOperationalAuditService> MockOperationalAuditService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:AutomaticDebit"] = bool.TrueString
            }));

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(NullLoggerProvider.Instance);
        });

        builder.ConfigureServices(services =>
        {
            // ─── 1. Reemplazar Oracle gtwContext con InMemory para evitar errores de startup ─
            ReplaceDbContext(services);

            // ─── 2. Reemplazar IPayPerTicService con mock ─────────────────────────
            RemoveAll<IPayPerTicService>(services);
            services.AddScoped<IPayPerTicService>(_ => MockPayPerTicService.Object);
            RemoveAll<IAutomaticDebitAdhesionService>(services);
            services.AddScoped<IAutomaticDebitAdhesionService>(_ => MockAutomaticDebitAdhesionService.Object);
            RemoveAll<IAutomaticDebitGenerationService>(services);
            services.AddScoped<IAutomaticDebitGenerationService>(_ => MockAutomaticDebitGenerationService.Object);
            RemoveAll<IAutomaticDebitWebhookProcessor>(services);
            services.AddScoped<IAutomaticDebitWebhookProcessor>(_ => MockAutomaticDebitWebhookProcessor.Object);
            RemoveAll<IJurisdictionPayPerTicConfigurationProvider>(services);
            services.AddScoped<IJurisdictionPayPerTicConfigurationProvider>(
                _ => MockAutomaticDebitConfigurationProvider.Object);
            RemoveAll<IOperationalAuditService>(services);
            services.AddScoped<IOperationalAuditService>(_ => MockOperationalAuditService.Object);

            // También limpiar los clientes HTTP de PayPerTIC (no son necesarios)
            RemoveAll<IPayPerTicClient>(services);
            RemoveAll<IPayPerTicAuthClient>(services);
            services.AddSingleton<IPayPerTicClient>(_ => Mock.Of<IPayPerTicClient>());
            services.AddSingleton<IPayPerTicAuthClient>(_ => Mock.Of<IPayPerTicAuthClient>());

            // ─── 3. Reemplazar autenticación JWT con TestAuthHandler ───────────────
            services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                opts.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });
        });

        builder.UseEnvironment("Testing");
    }

    /// <summary>
    /// Cliente HTTP autenticado: agrega el header X-Test-Auth que TestAuthHandler reconoce.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeader, "true");
        return client;
    }

    public WebApplicationFactory<Program> WithAutomaticDebitFeature(bool enabled) =>
        WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Features:AutomaticDebit"] = enabled.ToString()
                })));

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void ReplaceDbContext(IServiceCollection services)
    {
        // Remover todos los descriptores relacionados con gtwContext y Oracle
        var toRemove = services
            .Where(d => d.ServiceType == typeof(DbContextOptions<gtwContext>)
                     || d.ServiceType == typeof(gtwContext)
                     || (d.ImplementationType != null && d.ImplementationType.IsAssignableTo(typeof(gtwContext))))
            .ToList();
        foreach (var d in toRemove) services.Remove(d);

        // Registrar TestGtwContext con InMemory mediante una factory explícita
        var dbName = $"IntegrationTest_{Guid.NewGuid():N}";
        services.AddScoped<gtwContext>(_ =>
        {
            var options = new DbContextOptionsBuilder<gtwContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            return new TestGtwContext(options);
        });
    }

    private static void RemoveAll<T>(IServiceCollection services)
    {
        var found = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var d in found) services.Remove(d);
    }
}
