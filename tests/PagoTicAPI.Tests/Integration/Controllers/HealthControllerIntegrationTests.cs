using System.Net;
using System.Text.Json;

namespace PagoTicAPI.Tests.Integration.Controllers;

public sealed class HealthControllerIntegrationTests : IClassFixture<IntegrationTestBase>
{
    private readonly IntegrationTestBase _factory;

    public HealthControllerIntegrationTests(IntegrationTestBase factory) => _factory = factory;

    [Fact]
    public async Task Live_IsPublicAndDoesNotRequireDependencies()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_VerifiesInternalDependenciesWithoutCallingPayPerTic()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockAutomaticDebitConfigurationProvider.VerifyNoOtherCalls();
        _factory.MockPayPerTicService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Swagger_DocumentsHealthEndpointsAndTheirRuntimeResponses()
    {
        using var client = _factory.CreateClient();

        using var swagger = JsonDocument.Parse(
            await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = swagger.RootElement.GetProperty("paths");

        AssertHealthOperation(paths, "/health/live");
        AssertHealthOperation(paths, "/health/ready");
        AssertHealthOperation(paths, "/health");
    }

    private static void AssertHealthOperation(JsonElement paths, string path)
    {
        var operation = paths.GetProperty(path).GetProperty("get");
        var responses = operation.GetProperty("responses");

        responses.TryGetProperty("200", out _).Should().BeTrue();
        responses.TryGetProperty("503", out _).Should().BeTrue();
    }
}
