using PagoTicAPI.Application.Configuration.AutomaticDebits;
using PagoTicAPI.Domain.Context;
using PagoTicAPI.Domain.Models.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public class ConfigurationProviderTests
{
    [Fact]
    public async Task GetActiveAsync_WhenConfigurationIsMissing_ThrowsBeforeReturningConfiguration()
    {
        await using var context = CreateContext();
        var sut = new JurisdictionPayPerTicConfigurationProvider(
            context,
            Mock.Of<ILogger<JurisdictionPayPerTicConfigurationProvider>>());

        var act = () => sut.GetActiveAsync(2419);

        await act.Should().ThrowAsync<PayPerTicConfigurationNotFoundException>()
            .WithMessage("*2419*");
    }

    [Fact]
    public async Task GetActiveAsync_WhenOnlyConfigurationIsInactive_ThrowsBeforeReturningConfiguration()
    {
        await using var context = CreateContext();
        context.PayPerTicConfigurations.Add(CreateConfiguration(2419, false, "inactive-user"));
        await context.SaveChangesAsync();
        var sut = new JurisdictionPayPerTicConfigurationProvider(
            context,
            Mock.Of<ILogger<JurisdictionPayPerTicConfigurationProvider>>());

        var act = () => sut.GetActiveAsync(2419);

        await act.Should().ThrowAsync<PayPerTicConfigurationNotFoundException>()
            .WithMessage("*2419*");
    }

    [Fact]
    public async Task GetActiveAsync_ForTwoJurisdictions_ReturnsOnlyRequestedConfiguration()
    {
        await using var context = CreateContext();
        context.PayPerTicConfigurations.AddRange(
            CreateConfiguration(2419, true, "vgb-user"),
            CreateConfiguration(2389, true, "unq-user"));
        await context.SaveChangesAsync();
        var sut = new JurisdictionPayPerTicConfigurationProvider(
            context,
            Mock.Of<ILogger<JurisdictionPayPerTicConfigurationProvider>>());

        var vgb = await sut.GetActiveAsync(2419);
        var unq = await sut.GetActiveAsync(2389);

        vgb.JurisdictionId.Should().Be(2419);
        vgb.Username.Should().Be("vgb-user");
        unq.JurisdictionId.Should().Be(2389);
        unq.Username.Should().Be("unq-user");
    }

    private static gtwContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<gtwContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new gtwContext(options);
    }

    private static PayPerTicConfiguration CreateConfiguration(
        long jurisdictionId,
        bool isActive,
        string username) => new()
        {
            Id = jurisdictionId,
            JurisdictionId = jurisdictionId,
            IsActive = isActive,
            AuthUrl = $"https://auth-{jurisdictionId}.example.test/token",
            ApiUrl = $"https://api-{jurisdictionId}.example.test",
            Username = username,
            Password = $"password-{jurisdictionId}",
            ClientId = $"client-{jurisdictionId}",
            ClientSecret = $"secret-{jurisdictionId}",
            CollectorId = $"collector-{jurisdictionId}"
        };
}

