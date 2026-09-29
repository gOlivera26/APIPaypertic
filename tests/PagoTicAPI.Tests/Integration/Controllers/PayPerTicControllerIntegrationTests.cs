using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;
using PagoTicAPI.Application.Clients.PayPerTic.Contracts.Webhooks;
using PagoTicAPI.Application.RequestDto.PayPerTic;
using PagoTicAPI.Application.ResponseDto.PayPerTic;
using PagoTicAPI.Application.Services.Interfaces;

namespace PagoTicAPI.Tests.Integration.Controllers;

/// <summary>
/// Tests de integración del PayPerTicController.
///
/// Prueban el pipeline HTTP completo:
///   routing → autenticación → controller → (mock) IPayPerTicService
///
/// Lo que se verifica:
///   - Routing correcto (POST /api/pagostic/*)
///   - Autenticación: endpoints protegidos devuelven 401 sin token
///   - AllowAnonymous: /webhook accesible sin autenticación
///   - Mapeo HTTP: OperationResponse.Code → HTTP status code
///   - Deserialización de request body (JSON → DTO)
///
/// La lógica de negocio se prueba en PayPerTicServiceTests (unit).
/// </summary>
[Collection("IntegrationTests")]
public class PayPerTicControllerIntegrationTests : IClassFixture<IntegrationTestBase>
{
    private readonly IntegrationTestBase _factory;
    private readonly HttpClient          _authClient;   // autenticado via TestAuthHandler
    private readonly HttpClient          _anonClient;   // sin autenticación

    public PayPerTicControllerIntegrationTests(IntegrationTestBase factory)
    {
        _factory    = factory;
        _authClient = factory.CreateAuthenticatedClient();
        _anonClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/pagostic/webhook  (AllowAnonymous)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_SinAutenticacion_Accesible_Retorna200()
    {
        // El webhook debe ser accesible sin token (AllowAnonymous)
        _factory.MockPayPerTicService
            .Setup(s => s.RecibirWebhookAsync(It.IsAny<PayPerTicWebhookNotification>()))
            .ReturnsAsync(OkBoolResponse());

        var response = await _anonClient.PostAsJsonAsync("/api/pagostic/webhook",
            new PayPerTicWebhookNotification { Id = "ppt-001", Status = "approved" });

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Webhook_CuandoServiceRetorna404_HttpStatusEs404()
    {
        _factory.MockPayPerTicService
            .Setup(s => s.RecibirWebhookAsync(It.IsAny<PayPerTicWebhookNotification>()))
            .ReturnsAsync(OperationResponse<bool>.NotFoundResponse());

        var response = await _authClient.PostAsJsonAsync("/api/pagostic/webhook",
            new PayPerTicWebhookNotification { Id = "ppt-inexistente", Status = "approved" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Webhook_CuandoServiceRetorna400_HttpStatusEs400()
    {
        _factory.MockPayPerTicService
            .Setup(s => s.RecibirWebhookAsync(It.IsAny<PayPerTicWebhookNotification>()))
            .ReturnsAsync(OperationResponse<bool>.BadRequestResponse("El campo 'id' es requerido."));

        var response = await _authClient.PostAsJsonAsync("/api/pagostic/webhook",
            new PayPerTicWebhookNotification { Id = "", Status = "approved" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Checkout_ConAutenticacion_CuandoServiceRetorna200_Retorna200()
    {
        _factory.MockPayPerTicService
            .Setup(s => s.CrearCheckoutAsync(It.IsAny<CreatePayPerTicCheckoutDto>()))
            .ReturnsAsync(OperationResponse<PayPerTicCheckoutResultDto>.SuccessResponse(
                new PayPerTicCheckoutResultDto
                {
                    IsSuccess = true,
                    FormUrl   = "https://pago.paypertic.com/form/abc",
                    PaymentId = "ppt-123"
                }));

        var response = await _authClient.PostAsJsonAsync("/api/pagostic/checkout",
            BuildCheckoutRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Checkout_CuandoServiceRetorna500_Retorna500()
    {
        _factory.MockPayPerTicService
            .Setup(s => s.CrearCheckoutAsync(It.IsAny<CreatePayPerTicCheckoutDto>()))
            .ReturnsAsync(OperationResponse<PayPerTicCheckoutResultDto>.ErrorResponse("Error de conexión"));

        var response = await _authClient.PostAsJsonAsync("/api/pagostic/checkout",
            BuildCheckoutRequest());

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Checkout_ConImporteNoPositivo_Retorna400SinInvocarServicio()
    {
        var calls = _factory.MockPayPerTicService.Invocations.Count;
        var request = BuildCheckoutRequest();
        request.Details[0].Amount = 0;

        var response = await _anonClient.PostAsJsonAsync("/api/pagostic/checkout", request);

        response.StatusCode.Should().Be(
            HttpStatusCode.BadRequest,
            await response.Content.ReadAsStringAsync());
        _factory.MockPayPerTicService.Invocations.Count.Should().Be(calls);
    }

    [Fact]
    public async Task Checkout_SinDetalles_Retorna400SinInvocarServicio()
    {
        var calls = _factory.MockPayPerTicService.Invocations.Count;
        var request = BuildCheckoutRequest();
        request.Details.Clear();

        var response = await _anonClient.PostAsJsonAsync("/api/pagostic/checkout", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.MockPayPerTicService.Invocations.Count.Should().Be(calls);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/pagostic/cancelar/{pagIdExterno}  (requiere autenticación)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_SinAutenticacion_Retorna401()
    {
        var response = await _anonClient.PostAsJsonAsync("/api/pagostic/cancelar/ppt-001",
            new { status_detail = "motivo" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cancelar_ConAutenticacion_CuandoServiceRetorna200_Retorna200()
    {
        _factory.MockPayPerTicService
            .Setup(s => s.CancelarPagoAsync("ppt-pendiente", It.IsAny<string>()))
            .ReturnsAsync(OkBoolResponse());

        var response = await _authClient.PostAsJsonAsync(
            "/api/pagostic/cancelar/ppt-pendiente", new { status_detail = "Cancelado por test" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cancelar_CuandoServiceRetorna404_Retorna404()
    {
        _factory.MockPayPerTicService
            .Setup(s => s.CancelarPagoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResponse<bool>.NotFoundResponse());

        var response = await _authClient.PostAsJsonAsync(
            "/api/pagostic/cancelar/ppt-no-existe", new { status_detail = "motivo" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/pagostic/devolucion/{pagIdExterno}  (requiere autenticación)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Devolucion_SinAutenticacion_Retorna401()
    {
        var response = await _anonClient.PostAsJsonAsync("/api/pagostic/devolucion/ppt-001",
            new PayPerTicRefundRequest { Type = "online" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Devolucion_ConAutenticacion_CuandoServiceRetorna200_Retorna200()
    {
        _factory.MockPayPerTicService
            .Setup(s => s.DevolverPagoAsync("ppt-aprobado", It.IsAny<PayPerTicRefundRequest>()))
            .ReturnsAsync(OperationResponse<PayPerTicRefundResponse>.SuccessResponse(
                new PayPerTicRefundResponse { Id = "refund-001", Status = "approved", Amount = 500m }));

        var response = await _authClient.PostAsJsonAsync("/api/pagostic/devolucion/ppt-aprobado",
            new PayPerTicRefundRequest { Type = "online", Reason = "Test" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Devolucion_CuandoServiceRetorna400_Retorna400()
    {
        _factory.MockPayPerTicService
            .Setup(s => s.DevolverPagoAsync(It.IsAny<string>(), It.IsAny<PayPerTicRefundRequest>()))
            .ReturnsAsync(OperationResponse<PayPerTicRefundResponse>.BadRequestResponse(
                "Solo se pueden devolver pagos aprobados."));

        var response = await _authClient.PostAsJsonAsync("/api/pagostic/devolucion/ppt-pending",
            new PayPerTicRefundRequest { Type = "online" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static OperationResponse<bool> OkBoolResponse() =>
        OperationResponse<bool>.SuccessResponse(true);

    private static CreatePayPerTicCheckoutDto BuildCheckoutRequest() => new()
    {
        ExternalTransactionId = $"TXN-{Guid.NewGuid():N}",
        CurrencyId            = "ARS",
        JurisdiccionId        = 2389,
        Origen                = "web",
        Details = new List<CheckoutDetailItemDto>
        {
            new()
            {
                Amount             = 1500m,
                ConceptId          = "0001",
                ConceptDescription = "Tasa de prueba",
                IdObligacion       = "OBL-001",
                AnoCuota           = 2024,
                NroCuota           = 1,
                CapitalFacturado   = 1500m,
                DeudaActualizada   = 1500m
            }
        },
        Payer = new CheckoutPayerDto
        {
            Name                  = "Test User",
            Email                 = "test@test.com",
            IdentificationNumber  = "12345678",
            IdentificationType    = "DNI_ARG",
            IdentificationCountry = "ARG"
        }
    };
}
