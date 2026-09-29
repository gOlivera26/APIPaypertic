using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PagoTicAPI.Application.Configuration.AutomaticDebits;

namespace PagoTicAPI.Tests.Integration.Controllers;

[Collection("IntegrationTests")]
public sealed class AutomaticDebitReturnControllerIntegrationTests : IClassFixture<IntegrationTestBase>
{
    private const string PortalUrl = "https://www.sigemyt.com/portalmunicipalvgb/#/inicio";
    private readonly IntegrationTestBase _factory;

    public AutomaticDebitReturnControllerIntegrationTests(IntegrationTestBase factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Return_RedirectsGetAndPostToConfiguredPortalWithSeeOther(string method)
    {
        ConfigureJurisdiction();
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            "/api/debitos-automaticos/retorno/2419");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        response.Headers.Location.Should().Be(new Uri(PortalUrl));
    }

    [Fact]
    public async Task Return_WhenJurisdictionHasNoConfiguration_ReturnsNotFound()
    {
        _factory.MockAutomaticDebitConfigurationProvider
            .Setup(x => x.GetActiveAsync(9999, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PayPerTicConfigurationNotFoundException(9999));
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/debitos-automaticos/retorno/9999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private void ConfigureJurisdiction()
    {
        _factory.MockAutomaticDebitConfigurationProvider
            .Setup(x => x.GetActiveAsync(2419, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JurisdictionPayPerTicConfiguration(
                2419,
                "https://auth.example/token",
                "https://api.example",
                "user",
                "password",
                "client",
                "secret",
                "collector",
                "https://api.example/notificaciones/2419",
                "https://api.example/retorno/2419",
                PortalUrl));
    }
}
