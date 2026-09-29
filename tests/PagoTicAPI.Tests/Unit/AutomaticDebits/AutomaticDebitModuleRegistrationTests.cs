using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using PagoTicAPI.API.Config;
using PagoTicAPI.API.AutomaticDebits.Infrastructure;
using PagoTicAPI.Application.Clients.AutomaticDebits;
using PagoTicAPI.Application.Configuration.AutomaticDebits;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Services.Implementations;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;
using PagoTicAPI.Domain.Context;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public sealed class AutomaticDebitModuleRegistrationTests
{
    [Fact]
    public void AddAutomaticDebit_RegistersResolvableProductionGraphWithoutOpeningExternalConnections()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Gateway"] = "Data Source=unused-test-endpoint",
                ["Features:AutomaticDebit"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContext<gtwContext>(options =>
            options.UseInMemoryDatabase($"AutomaticDebitModule_{Guid.NewGuid():N}"));

        services.AddAutomaticDebit(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<gtwContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITributaryAccountReader>()
            .Should().BeOfType<OracleTributaryAccountReader>();
        scope.ServiceProvider.GetRequiredService<IAutomaticDebitIdGenerator>()
            .Should().BeOfType<OracleAutomaticDebitIdGenerator>();
        scope.ServiceProvider.GetRequiredService<IAutomaticDebitWebhookIdGenerator>()
            .Should().BeOfType<OracleAutomaticDebitIdGenerator>();
        scope.ServiceProvider.GetRequiredService<IPayPerTicTokenAcquirer>()
            .Should().BeOfType<PayPerTicTokenAcquirer>();
        scope.ServiceProvider.GetRequiredService<IAutomaticDebitPayPerTicClient>()
            .Should().BeOfType<AutomaticDebitPayPerTicClient>();
        scope.ServiceProvider.GetRequiredService<IAutomaticDebitAdhesionService>()
            .Should().BeOfType<AutomaticDebitAdhesionService>();
        scope.ServiceProvider.GetRequiredService<IAutomaticDebitWebhookProcessor>()
            .Should().BeOfType<AutomaticDebitWebhookProcessor>();
    }

    [Fact]
    public void AddAutomaticDebit_RequiresGatewayConnectionConfiguration()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var act = () => services.AddAutomaticDebit(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Gateway*");
    }
}

