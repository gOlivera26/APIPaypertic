using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PagoTicAPI.API.Config;

namespace PagoTicAPI.Tests.Unit.Config;

public sealed class SecurityConfigurationTests
{
    [Fact]
    public void AddConfig_WithoutAllowedOrigins_FailsClosed()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = new string('s', 32)
        });

        var action = () => new ServiceCollection().AddConfig(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cors:AllowedOrigins*");
    }

    [Fact]
    public void AddConfig_WithoutExplicitJwtSecret_FailsClosed()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://example.test"
        });

        var action = () => new ServiceCollection().AddConfig(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:SecretKey*");
    }

    [Fact]
    public void AddConfig_WithKnownDevelopmentJwtSecret_FailsClosed()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://example.test",
            ["Jwt:SecretKey"] = "YourSecretKeyHere12345"
        });

        var action = () => new ServiceCollection().AddConfig(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:SecretKey*");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
