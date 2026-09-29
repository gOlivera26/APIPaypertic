using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.Common;
using PagoTicAPI.Domain.Models.AutomaticDebits;

namespace PagoTicAPI.Tests.Integration.Controllers;

[Collection("IntegrationTests")]
public class AutomaticDebitControllerIntegrationTests : IClassFixture<IntegrationTestBase>
{
    private readonly IntegrationTestBase _factory;
    private readonly HttpClient _authenticatedClient;
    private readonly HttpClient _anonymousClient;

    public AutomaticDebitControllerIntegrationTests(IntegrationTestBase factory)
    {
        _factory = factory;
        _factory.MockAutomaticDebitAdhesionService.Invocations.Clear();
        _factory.MockAutomaticDebitGenerationService.Invocations.Clear();
        _authenticatedClient = factory.CreateAuthenticatedClient();
        _anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Create_WithoutAuthentication_UsesDedicatedRoute()
    {
        _factory.MockAutomaticDebitAdhesionService
            .Setup(x => x.CreateAsync(It.IsAny<CreateAutomaticDebitAdhesionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitAdhesionDto>.SuccessResponse(Response()));

        var response = await _anonymousClient.PostAsJsonAsync(
            "/api/debitos-automaticos/adhesiones",
            new CreateAutomaticDebitAdhesionRequest(2419, "VGBIN85447"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_WithAuthentication_UsesDedicatedRoute()
    {
        _factory.MockAutomaticDebitAdhesionService
            .Setup(x => x.CreateAsync(It.IsAny<CreateAutomaticDebitAdhesionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitAdhesionDto>.SuccessResponse(Response()));

        var response = await _authenticatedClient.PostAsJsonAsync(
            "/api/debitos-automaticos/adhesiones",
            new CreateAutomaticDebitAdhesionRequest(2419, "VGBIN85447"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAdhesion_WithoutAuthentication_IsPublicAndScopedByJurisdictionAndAccount()
    {
        _factory.MockAutomaticDebitAdhesionService
            .Setup(x => x.GetAsync(2419, "VGBIN85447", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitAdhesionDto>.SuccessResponse(Response()));

        var response = await _anonymousClient.GetAsync(
            "/api/debitos-automaticos/adhesiones/VGBIN85447?idJurisdiccion=2419");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CancelAdhesion_WithoutAuthentication_IsPublicAndForwardsReason()
    {
        _factory.MockAutomaticDebitAdhesionService
            .Setup(x => x.CancelAsync(
                2419,
                "VGBIN85447",
                It.Is<CancelAutomaticDebitAdhesionRequest>(r => r.Reason == "Requested by taxpayer"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitAdhesionDto>.SuccessResponse(
                Response(AutomaticDebitProviderStates.Cancelled)));

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/debitos-automaticos/adhesiones/VGBIN85447?idJurisdiccion=2419")
        {
            Content = JsonContent.Create(new CancelAutomaticDebitAdhesionRequest("Requested by taxpayer"))
        };
        var response = await _anonymousClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(0, "VGBIN85447")]
    [InlineData(2419, "invalid account")]
    [InlineData(2419, "")]
    public async Task CreateAdhesion_WithInvalidPublicInput_ReturnsBadRequestWithoutCallingService(
        long jurisdictionId,
        string taxpayerAccountId)
    {
        var calls = _factory.MockAutomaticDebitAdhesionService.Invocations.Count;

        var response = await _anonymousClient.PostAsJsonAsync(
            "/api/debitos-automaticos/adhesiones",
            new CreateAutomaticDebitAdhesionRequest(jurisdictionId, taxpayerAccountId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.MockAutomaticDebitAdhesionService.Invocations.Count.Should().Be(calls);
    }

    [Theory]
    [InlineData("invalid account", 2419)]
    [InlineData("VGBIN85447", 0)]
    public async Task GetAdhesion_WithInvalidPublicInput_ReturnsBadRequestWithoutCallingService(
        string taxpayerAccountId,
        long jurisdictionId)
    {
        var calls = _factory.MockAutomaticDebitAdhesionService.Invocations.Count;

        var response = await _anonymousClient.GetAsync(
            $"/api/debitos-automaticos/adhesiones/{Uri.EscapeDataString(taxpayerAccountId)}?idJurisdiccion={jurisdictionId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.MockAutomaticDebitAdhesionService.Invocations.Count.Should().Be(calls);
    }

    [Fact]
    public async Task PublicAdhesionEndpoints_WhenRateLimitIsExceeded_ReturnTooManyRequests()
    {
        const int permitLimit = 2;
        _factory.MockAutomaticDebitAdhesionService
            .Setup(x => x.GetAsync(2419, "VGBIN85447", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitAdhesionDto>.SuccessResponse(Response()));
        using var rateLimitedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AutomaticDebitPublicEndpoints:RateLimit:PermitLimit"] = permitLimit.ToString(),
                    ["AutomaticDebitPublicEndpoints:RateLimit:WindowSeconds"] = "60"
                })));
        using var client = rateLimitedFactory.CreateClient();

        var responses = new List<HttpResponseMessage>();
        for (var index = 0; index < permitLimit + 1; index++)
        {
            responses.Add(await client.GetAsync(
                "/api/debitos-automaticos/adhesiones/VGBIN85447?idJurisdiccion=2419"));
        }

        responses.Take(permitLimit).Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        responses.Last().StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task CreateDebit_WithAuthentication_UsesObligationOnlyContract()
    {
        _factory.MockAutomaticDebitGenerationService
            .Setup(x => x.CreateAsync(
                It.Is<CreateAutomaticDebitRequest>(r => r.IdJurisdiccion == 2419 && r.IdObligacion == "6012353270"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(OperationResponse()));

        var response = await _authenticatedClient.PostAsJsonAsync(
            "/api/debitos-automaticos/debitos",
            new CreateAutomaticDebitRequest(2419, "6012353270"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockAutomaticDebitGenerationService.Verify(
            x => x.CreateAsync(
                It.Is<CreateAutomaticDebitRequest>(r => r.IdJurisdiccion == 2419 && r.IdObligacion == "6012353270"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateDebit_WithoutAuthentication_ReachesServiceInTesting()
    {
        _factory.MockAutomaticDebitGenerationService
            .Setup(x => x.CreateAsync(It.IsAny<CreateAutomaticDebitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(OperationResponse()));

        var response = await _anonymousClient.PostAsJsonAsync(
            "/api/debitos-automaticos/debitos",
            new CreateAutomaticDebitRequest(2419, "6012353270"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockAutomaticDebitGenerationService.Verify(
            x => x.CreateAsync(It.IsAny<CreateAutomaticDebitRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconcileDebit_WithoutAuthentication_ReachesServiceInTesting()
    {
        _factory.MockAutomaticDebitGenerationService
            .Setup(x => x.ReconcileAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(OperationResponse()));

        var response = await _anonymousClient.PostAsync(
            "/api/debitos-automaticos/debitos/1/reconciliar",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockAutomaticDebitGenerationService.Verify(
            x => x.ReconcileAsync(1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetDebit_WithoutAuthentication_ReachesServiceInTesting()
    {
        _factory.MockAutomaticDebitGenerationService
            .Setup(x => x.GetAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(OperationResponse()));

        var response = await _anonymousClient.GetAsync("/api/debitos-automaticos/debitos/2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockAutomaticDebitGenerationService.Verify(
            x => x.GetAsync(2, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelDebit_WithoutAuthentication_ReachesServiceInTesting()
    {
        _factory.MockAutomaticDebitGenerationService
            .Setup(x => x.CancelAsync(2, It.IsAny<CancelAutomaticDebitRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(OperationResponse()));

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/debitos-automaticos/debitos/2")
        {
            Content = JsonContent.Create(new CancelAutomaticDebitRequest("Controlled sandbox cancellation"))
        };
        var response = await _anonymousClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockAutomaticDebitGenerationService.Verify(
            x => x.CancelAsync(2, It.IsAny<CancelAutomaticDebitRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetDebit_WithAuthentication_ReachesService()
    {
        _factory.MockAutomaticDebitGenerationService
            .Setup(x => x.GetAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(OperationResponse()));

        var response = await _authenticatedClient.GetAsync("/api/debitos-automaticos/debitos/2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockAutomaticDebitGenerationService.Verify(
            x => x.GetAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelDebit_WithAuthentication_ReachesService()
    {
        _factory.MockAutomaticDebitGenerationService
            .Setup(x => x.CancelAsync(
                2,
                It.Is<CancelAutomaticDebitRequest>(r => r.Reason == "Controlled cancellation"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(OperationResponse()));
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/debitos-automaticos/debitos/2")
        {
            Content = JsonContent.Create(new CancelAutomaticDebitRequest("Controlled cancellation"))
        };

        var response = await _authenticatedClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockAutomaticDebitGenerationService.Verify(
            x => x.CancelAsync(2, It.IsAny<CancelAutomaticDebitRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconcileDebit_WithAuthentication_ReachesService()
    {
        _factory.MockAutomaticDebitGenerationService
            .Setup(x => x.ReconcileAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<AutomaticDebitOperationDto>.SuccessResponse(OperationResponse()));

        var response = await _authenticatedClient.PostAsync(
            "/api/debitos-automaticos/debitos/2/reconciliar",
            null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockAutomaticDebitGenerationService.Verify(
            x => x.ReconcileAsync(2, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AutomaticDebitAdhesionDto Response(string state = AutomaticDebitProviderStates.Pending) => new(
        1,
        2419,
        "VGBIN85447",
        "MVGB318713",
        "ppt-1",
        "https://form.example/1",
        state,
        DateTime.UtcNow,
        null,
        state == AutomaticDebitProviderStates.Cancelled ? DateTime.UtcNow : null);

    private static AutomaticDebitOperationDto OperationResponse() => new(
        8001, 2419, "VGBIN88256", "6012353270", "ppt-payment",
        "PPT-DA-2419-6012353270-8001", 36469.09m, new DateTime(2026, 12, 15),
        AutomaticDebitProviderStates.Issued, "PENDING", DateTime.UtcNow);
}
