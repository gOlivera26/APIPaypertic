using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;
using PagoTicAPI.Application.RequestDto.PayPerTic;
using PagoTicAPI.Application.ResponseDto.PayPerTic;
using PagoTicAPI.Tests.Helpers;

namespace PagoTicAPI.Tests.Integration.Controllers;

[Collection("IntegrationTests")]
public sealed class AutomaticDebitWiringIntegrationTests(IntegrationTestBase factory)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkout_PreservesObservableResponse_WhenAutomaticDebitFlagChanges(bool enabled)
    {
        factory.MockPayPerTicService
            .Setup(service => service.CrearCheckoutAsync(It.IsAny<CreatePayPerTicCheckoutDto>()))
            .ReturnsAsync(OperationResponse<PayPerTicCheckoutResultDto>.SuccessResponse(
                new PayPerTicCheckoutResultDto
                {
                    IsSuccess = true,
                    FormUrl = "https://checkout.example/form/fixture",
                    PaymentId = "checkout-fixture"
                }));
        using var host = factory.WithAutomaticDebitFeature(enabled);
        using var client = CreateAuthenticatedClient(host);

        var response = await client.PostAsJsonAsync("/api/pagostic/checkout", CheckoutRequest());
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("checkout-fixture");
        body.Should().Contain("https://checkout.example/form/fixture");
    }

    [Theory]
    [InlineData("POST", "/api/debitos-automaticos/adhesiones")]
    [InlineData("GET", "/api/debitos-automaticos/adhesiones/VGBIN85447?idJurisdiccion=2419")]
    [InlineData("DELETE", "/api/debitos-automaticos/adhesiones/VGBIN85447?idJurisdiccion=2419")]
    [InlineData("POST", "/api/debitos-automaticos/debitos")]
    [InlineData("GET", "/api/debitos-automaticos/debitos/1")]
    [InlineData("DELETE", "/api/debitos-automaticos/debitos/1")]
    [InlineData("POST", "/api/debitos-automaticos/debitos/1/reconciliar")]
    [InlineData("POST", "/api/debitos-automaticos/notificaciones/2419")]
    [InlineData("GET", "/api/debitos-automaticos/retorno/2419")]
    public async Task AutomaticDebitRoutes_FailClosed_WhenFeatureIsDisabled(string method, string route)
    {
        var adhesionCalls = factory.MockAutomaticDebitAdhesionService.Invocations.Count;
        var webhookCalls = factory.MockAutomaticDebitWebhookProcessor.Invocations.Count;
        using var host = factory.WithAutomaticDebitFeature(false);
        using var client = CreateAuthenticatedClient(host);

        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method is "POST" or "DELETE")
        {
            request.Content = new StringContent(
                "{\"type\":\"debit\",\"id\":\"payment-1\"}",
                Encoding.UTF8,
                "application/json");
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.MockAutomaticDebitAdhesionService.Invocations.Count.Should().Be(adhesionCalls);
        factory.MockAutomaticDebitWebhookProcessor.Invocations.Count.Should().Be(webhookCalls);
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeader, "true");
        return client;
    }

    private static CreatePayPerTicCheckoutDto CheckoutRequest() => new()
    {
        ExternalTransactionId = "CHECKOUT-PARITY-FIXTURE",
        CurrencyId = "ARS",
        JurisdiccionId = 2419,
        Origen = "web",
        Details =
        [
            new CheckoutDetailItemDto
            {
                Amount = 11.14m,
                ConceptId = "6",
                ConceptDescription = "Tasa de prueba",
                IdObligacion = "1038454109",
                AnoCuota = 2003,
                NroCuota = 8,
                CapitalFacturado = 11.14m,
                DeudaActualizada = 11.14m
            }
        ],
        Payer = new CheckoutPayerDto
        {
            Name = "Fixture User",
            Email = "fixture@example.test",
            IdentificationNumber = "20123456789",
            IdentificationType = "CUIT_ARG",
            IdentificationCountry = "ARG"
        }
    };
}
