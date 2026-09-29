using PagoTicAPI.API.Controllers;

namespace PagoTicAPI.Tests.Unit.Controllers;

/// <summary>
/// Tests unitarios del PayPerTicController.
///
/// Objetivo: verificar que el controller delega al service y mapea correctamente
/// el OperationResponse.Code al HTTP status code via BaseController.Return().
///
/// No prueba lógica de negocio (eso está en PayPerTicServiceTests).
/// </summary>
public class PayPerTicControllerTests
{
    private readonly Mock<IPayPerTicService> _mockService = new();
    private readonly PayPerTicController     _controller;

    public PayPerTicControllerTests()
    {
        _controller = new PayPerTicController(_mockService.Object);
    }

    [Fact]
    public async Task CrearCheckout_CuandoServiceRetorna200_HttpStatusEs200()
    {
        _mockService
            .Setup(s => s.CrearCheckoutAsync(It.IsAny<CreatePayPerTicCheckoutDto>()))
            .ReturnsAsync(OkResponse(new PayPerTicCheckoutResultDto
            {
                IsSuccess = true,
                FormUrl   = "https://pago.paypertic.com/form/abc"
            }));

        var result = await _controller.CrearCheckout(new CreatePayPerTicCheckoutDto());

        result.Should().BeOfType<OkObjectResult>()
              .Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task CrearCheckout_CuandoServiceRetorna400_HttpStatusEs400()
    {
        _mockService
            .Setup(s => s.CrearCheckoutAsync(It.IsAny<CreatePayPerTicCheckoutDto>()))
            .ReturnsAsync(BadRequestResponse<PayPerTicCheckoutResultDto>("Datos inválidos"));

        var result = await _controller.CrearCheckout(new CreatePayPerTicCheckoutDto());

        result.Should().BeOfType<BadRequestObjectResult>()
              .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CrearCheckout_CuandoServiceRetorna500_HttpStatusEs500()
    {
        _mockService
            .Setup(s => s.CrearCheckoutAsync(It.IsAny<CreatePayPerTicCheckoutDto>()))
            .ReturnsAsync(ErrorResponse<PayPerTicCheckoutResultDto>());

        var result = await _controller.CrearCheckout(new CreatePayPerTicCheckoutDto());

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task CrearCheckout_DelegaAlServicio_ExactamenteUnaVez()
    {
        _mockService
            .Setup(s => s.CrearCheckoutAsync(It.IsAny<CreatePayPerTicCheckoutDto>()))
            .ReturnsAsync(OkResponse(new PayPerTicCheckoutResultDto()));

        await _controller.CrearCheckout(new CreatePayPerTicCheckoutDto());

        _mockService.Verify(s => s.CrearCheckoutAsync(It.IsAny<CreatePayPerTicCheckoutDto>()), Times.Once);
    }

    [Fact]
    public async Task RecibirWebhook_CuandoServiceRetorna200_HttpStatusEs200()
    {
        _mockService
            .Setup(s => s.RecibirWebhookAsync(It.IsAny<PayPerTicWebhookNotification>()))
            .ReturnsAsync(OkResponse(true));

        var result = await _controller.RecibirWebhook(new PayPerTicWebhookNotification { Id = "ppt-001" });

        result.Should().BeOfType<OkObjectResult>()
              .Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RecibirWebhook_CuandoServiceRetorna404_HttpStatusEs404()
    {
        _mockService
            .Setup(s => s.RecibirWebhookAsync(It.IsAny<PayPerTicWebhookNotification>()))
            .ReturnsAsync(OperationResponse<bool>.NotFoundResponse());

        var result = await _controller.RecibirWebhook(new PayPerTicWebhookNotification { Id = "no-existe" });

        result.Should().BeOfType<NotFoundObjectResult>()
              .Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RecibirWebhook_CuandoServiceRetorna400_HttpStatusEs400()
    {
        _mockService
            .Setup(s => s.RecibirWebhookAsync(It.IsAny<PayPerTicWebhookNotification>()))
            .ReturnsAsync(BadRequestResponse<bool>("El campo 'id' es requerido."));

        var result = await _controller.RecibirWebhook(new PayPerTicWebhookNotification { Id = "" });

        result.Should().BeOfType<BadRequestObjectResult>()
              .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CancelarPago_CuandoServiceRetorna200_HttpStatusEs200()
    {
        _mockService
            .Setup(s => s.CancelarPagoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(OkResponse(true));

        var result = await _controller.CancelarPago("ppt-001", new PayPerTicCancelRequest { StatusDetail = "Cancelado por test" });

        result.Should().BeOfType<OkObjectResult>()
              .Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task CancelarPago_CuandoServiceRetorna400_HttpStatusEs400()
    {
        _mockService
            .Setup(s => s.CancelarPagoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(BadRequestResponse<bool>("Solo se pueden cancelar pagos en estado 'pending'."));

        var result = await _controller.CancelarPago("ppt-001", new PayPerTicCancelRequest { StatusDetail = "motivo" });

        result.Should().BeOfType<BadRequestObjectResult>()
              .Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task CancelarPago_CuandoServiceRetorna404_HttpStatusEs404()
    {
        _mockService
            .Setup(s => s.CancelarPagoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(OperationResponse<bool>.NotFoundResponse());

        var result = await _controller.CancelarPago("ppt-no-existe", new PayPerTicCancelRequest { StatusDetail = "motivo" });

        result.Should().BeOfType<NotFoundObjectResult>()
              .Which.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DevolverPago_CuandoServiceRetorna200_HttpStatusEs200()
    {
        _mockService
            .Setup(s => s.DevolverPagoAsync(It.IsAny<string>(), It.IsAny<PayPerTicRefundRequest>()))
            .ReturnsAsync(OkResponse(new PayPerTicRefundResponse
            {
                Id     = "refund-001",
                Status = "approved",
                Amount = 500m
            }));

        var result = await _controller.DevolverPago("ppt-001", new PayPerTicRefundRequest { Type = "online" });

        result.Should().BeOfType<OkObjectResult>()
              .Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task DevolverPago_CuandoServiceRetorna400_HttpStatusEs400()
    {
        _mockService
            .Setup(s => s.DevolverPagoAsync(It.IsAny<string>(), It.IsAny<PayPerTicRefundRequest>()))
            .ReturnsAsync(BadRequestResponse<PayPerTicRefundResponse>("Solo se pueden devolver pagos aprobados."));

        var result = await _controller.DevolverPago("ppt-001", new PayPerTicRefundRequest());

        result.Should().BeOfType<BadRequestObjectResult>()
              .Which.StatusCode.Should().Be(400);
    }

    private static OperationResponse<T> OkResponse<T>(T data) =>
        OperationResponse<T>.SuccessResponse(data);

    private static OperationResponse<T> BadRequestResponse<T>(string msg) =>
        OperationResponse<T>.BadRequestResponse(msg);

    private static OperationResponse<T> ErrorResponse<T>() =>
        OperationResponse<T>.ErrorResponse("Error de prueba");
}
