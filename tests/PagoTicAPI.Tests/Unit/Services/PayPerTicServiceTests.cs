using PagoTicAPI.Tests.Helpers;
using PagoTicAPI.Application.Clients.AutomaticDebits;
using PagoTicAPI.Application.Configuration.AutomaticDebits;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.Services;

public class PayPerTicServiceTests : IDisposable
{
    private readonly TestGtwContext _context;
    private readonly Mock<IPayPerTicClient> _mockClient;
    private readonly Mock<IAutomaticDebitPayPerTicClient> _automaticDebitClient;
    private readonly Mock<IJurisdictionPayPerTicConfigurationProvider> _configurationProvider;
    private readonly PayPerTicService _service;

    public PayPerTicServiceTests()
    {
        var options = new DbContextOptionsBuilder<gtwContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _context = new TestGtwContext(options);
        _mockClient = new Mock<IPayPerTicClient>();
        _automaticDebitClient = new Mock<IAutomaticDebitPayPerTicClient>();
        _configurationProvider = new Mock<IJurisdictionPayPerTicConfigurationProvider>();

        _configurationProvider
            .Setup(x => x.GetActiveAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JurisdictionPayPerTicConfiguration(
                2389,
                "https://auth.example/token",
                "https://api.example",
                "user",
                "password",
                "client",
                "secret",
                "collector-test",
                "https://test.api.com/pagostic/webhook",
                "https://test.front.com/pago/resultado",
                "https://test.front.com/pago/volver"));
        _automaticDebitClient
            .Setup(x => x.GetPaymentReadBackAsync(
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((long _, string providerId, CancellationToken _) =>
                new AutomaticDebitProviderReadBack(
                    AutomaticDebitWebhookObjectTypes.Payment,
                    providerId,
                    "approved",
                    "collector-test",
                    "external-reference",
                    DateTimeOffset.UtcNow));

        var keys = Options.Create(new PayPerTicKeys
        {
            CollectorId = "collector-test",
            NotificationUrl = "https://test.api.com/pagostic/webhook",
            ReturnUrl = "https://test.front.com/pago/resultado",
            BackUrl = "https://test.front.com/pago/volver"
        });

        _service = new PayPerTicService(
            _context,
            _mockClient.Object,
            keys,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PayPerTicService>.Instance,
            _automaticDebitClient.Object,
            _configurationProvider.Object
        );
    }

    public void Dispose() => _context.Dispose();


    [Fact]
    public async Task CrearCheckout_ConDatosValidos_GuardaPagoYRetornaFormUrl()
    {
        // Arrange
        var payPerTicId = Guid.NewGuid().ToString();
        var formUrl = "https://pago.paypertic.com/form/abc123";

        _mockClient
            .Setup(c => c.CreatePaymentAsync(It.IsAny<PayPerTicCreatePaymentRequest>()))
            .ReturnsAsync(new PayPerTicCreatePaymentResponse
            {
                Id = payPerTicId,
                FormUrl = formUrl,
                FinalAmount = 500m,
                Status = "pending"
            });

        var request = BuildCheckoutRequest(totalAmount: 500m);

        // Act
        var result = await _service.CrearCheckoutAsync(request);

        // Assert
        result.Success.Should().BeTrue($"Error: {result.Message} | Detalle: {result.Exception}");
        result.Code.Should().Be(200);
        result.Data!.IsSuccess.Should().BeTrue();
        result.Data.FormUrl.Should().Be(formUrl);
        result.Data.PaymentId.Should().Be(payPerTicId);
        result.Data.LocalPagId.Should().BeGreaterThan(0);

        // Verificar que se persistió en BD
        var pago = await _context.GtwPagos.FindAsync(result.Data.LocalPagId);
        pago.Should().NotBeNull();
        pago!.PagIdExterno.Should().Be(payPerTicId);
        pago.PagEstado.Should().Be("pending");
        pago.PagModoPago.Should().Be("PayPerTIC");

        // Verificar que se guardaron los detalles
        var detalles = _context.GtwPagoDetalles.Where(d => d.PgdPagId == pago.PagId).ToList();
        detalles.Should().HaveCount(1);
        detalles[0].PgdConcepto.Should().Be("Tasa de Seguridad e Higiene");
    }

    [Fact]
    public async Task CrearCheckout_ConMultiplesDetalles_GuardaTodosLosDetalles()
    {
        // Arrange
        _mockClient
            .Setup(c => c.CreatePaymentAsync(It.IsAny<PayPerTicCreatePaymentRequest>()))
            .ReturnsAsync(new PayPerTicCreatePaymentResponse
            {
                Id = Guid.NewGuid().ToString(),
                FormUrl = "https://form.url",
                FinalAmount = 300m,
                Status = "pending"
            });

        var request = BuildCheckoutRequest(cantidadDetalles: 3);

        // Act
        var result = await _service.CrearCheckoutAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var detalles = _context.GtwPagoDetalles
            .Where(d => d.PgdPagId == result.Data!.LocalPagId)
            .ToList();
        detalles.Should().HaveCount(3);
    }

    [Fact]
    public async Task CrearCheckout_CuandoPayPerTicLanzaExcepcion_DejaEstadoErrorYRetorna500()
    {
        // Arrange
        _mockClient
            .Setup(c => c.CreatePaymentAsync(It.IsAny<PayPerTicCreatePaymentRequest>()))
            .ThrowsAsync(new HttpRequestException("PayPerTIC no disponible"));

        var request = BuildCheckoutRequest();

        // Act
        var result = await _service.CrearCheckoutAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Code.Should().Be(500);

        result.Exception.Should().NotContain("PayPerTIC no disponible");

        // La preparación local se confirma antes de llamar al proveedor para conservar
        // una referencia conciliable aunque el resultado remoto sea incierto.
        var pago = await _context.GtwPagos.FirstOrDefaultAsync();
        pago.Should().NotBeNull();
        pago!.PagEstado.Should().Be("pending");
        pago.PagIdExterno.Should().BeNull();
        pago.PagEstadoDetalle.Should().Contain("Resultado PayPerTIC incierto");
        pago.PagEstadoDetalle.Should().Contain("ExternalTransactionId");
    }

    [Fact]
    public async Task CrearCheckout_ConDetalles_GtwPagoYDetallesSePersistenJuntos()
    {
        // Arrange
        _mockClient
            .Setup(c => c.CreatePaymentAsync(It.IsAny<PayPerTicCreatePaymentRequest>()))
            .ReturnsAsync(new PayPerTicCreatePaymentResponse
            {
                Id = "ppt-atomicity-test",
                FormUrl = "https://form.url",
                FinalAmount = 200m,
                Status = "pending"
            });

        var request = BuildCheckoutRequest(totalAmount: 200m, cantidadDetalles: 2);

        // Act
        var result = await _service.CrearCheckoutAsync(request);

        // Assert 
        result.Success.Should().BeTrue();
        result.Code.Should().Be(200);

        _context.GtwPagos.Count().Should().Be(1, "debe existir exactamente un GtwPago");
        _context.GtwPagoDetalles.Count().Should().Be(2, "debe existir un GtwPagoDetalle por cada línea del request");

        var localPagId = result.Data!.LocalPagId;
        localPagId.Should().BeGreaterThan(0);

        var detalles = _context.GtwPagoDetalles.Where(d => d.PgdPagId == localPagId).ToList();
        detalles.Should().HaveCount(2, "los detalles deben estar vinculados al GtwPago por PgdPagId");
    }

    [Fact]
    public async Task CrearCheckout_SinExternalTransactionId_GeneraUnoAutomaticamente()
    {
        // Arrange
        _mockClient
            .Setup(c => c.CreatePaymentAsync(It.IsAny<PayPerTicCreatePaymentRequest>()))
            .ReturnsAsync(new PayPerTicCreatePaymentResponse
            {
                Id = "ppt-id",
                FormUrl = "https://form.url",
                FinalAmount = 100m,
                Status = "pending"
            });

        var request = BuildCheckoutRequest();
        request.ExternalTransactionId = null;

        // Act
        var result = await _service.CrearCheckoutAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.ExternalTransactionId.Should().StartWith("PPT-");

        // Verifica que se envió a PayPerTIC con el ID generado
        _mockClient.Verify(c => c.CreatePaymentAsync(
            It.Is<PayPerTicCreatePaymentRequest>(r => r.ExternalTransactionId.StartsWith("PPT-"))),
            Times.Once);
    }

    [Fact]
    public async Task RecibirWebhook_ConPagoExistente_ActualizaEstadoCorrectamente()
    {
        // Arrange
        var payPerTicId = "ppt-123";
        _context.GtwPagos.Add(BuildPago(pagIdExterno: payPerTicId, estado: "pending"));
        await _context.SaveChangesAsync();
        ConfigureReadBack(payPerTicId, "approved");

        var notification = new PayPerTicWebhookNotification
        {
            Id = payPerTicId,
            Status = "approved",
            StatusDetail = "Pago aprobado exitosamente",
            Type = "online",
            PaymentMethods = new List<PayPerTicWebhookPaymentMethod>
            {
                new() { MediaPaymentDetail = "VISA CREDIT", MediaPaymentId = 9 }
            }
        };

        // Act
        var result = await _service.RecibirWebhookAsync(notification);

        // Assert
        result.Success.Should().BeTrue();
        result.Code.Should().Be(200);

        var pago = await _context.GtwPagos.FirstAsync(p => p.PagIdExterno == payPerTicId);
        pago.PagEstado.Should().Be("approved");
        pago.PagEstadoDetalle.Should().Contain("approved");
        pago.PagMetodoPago.Should().BeEmpty();
        pago.PagFormaPago.Should().Be("online");
    }

    [Fact]
    public async Task RecibirWebhook_ConPagoNoEncontrado_Retorna404()
    {
        // Arrange - BD vacía, ningún pago con ese ID
        var notification = new PayPerTicWebhookNotification
        {
            Id = "ppt-id-inexistente",
            Status = "approved"
        };

        // Act
        var result = await _service.RecibirWebhookAsync(notification);

        // Assert
        result.Success.Should().BeFalse();
        result.Code.Should().Be(404);
    }

    [Fact]
    public async Task RecibirWebhook_SinId_Retorna400()
    {
        // Arrange
        var notification = new PayPerTicWebhookNotification { Id = "", Status = "approved" };

        // Act
        var result = await _service.RecibirWebhookAsync(notification);

        // Assert
        result.Success.Should().BeFalse();
        result.Code.Should().Be(400);
    }

    [Theory]
    [InlineData("pending", "pending")]
    [InlineData("issued", "pending")]
    [InlineData("in_process", "in_process")]
    [InlineData("approved", "approved")]
    [InlineData("rejected", "rejected")]
    [InlineData("cancelled", "cancelled")]
    [InlineData("refunded", "refunded")]
    [InlineData("charged_back", "charged_back")]
    [InlineData("in_mediation", "in_mediation")]
    public async Task RecibirWebhook_MapeoDeEstados_EsCorrecto(
        string estadoPayPerTic, string estadoEsperado)
    {
        // Arrange
        var payPerTicId = $"ppt-{Guid.NewGuid()}";
        _context.GtwPagos.Add(BuildPago(pagIdExterno: payPerTicId));
        await _context.SaveChangesAsync();
        ConfigureReadBack(payPerTicId, estadoPayPerTic);

        var notification = new PayPerTicWebhookNotification
        { Id = payPerTicId, Status = estadoPayPerTic };

        // Act
        await _service.RecibirWebhookAsync(notification);

        // Assert
        var pago = await _context.GtwPagos.FirstAsync(p => p.PagIdExterno == payPerTicId);
        pago.PagEstado.Should().Be(estadoEsperado);
    }

    [Fact]
    public async Task RecibirWebhook_ConCollectorNoCoincidente_NoModificaElPago()
    {
        const string providerId = "ppt-collector-mismatch";
        _context.GtwPagos.Add(BuildPago(providerId, "pending"));
        await _context.SaveChangesAsync();
        _automaticDebitClient
            .Setup(x => x.GetPaymentReadBackAsync(2419, providerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderReadBack(
                AutomaticDebitWebhookObjectTypes.Payment,
                providerId,
                "approved",
                "otro-recaudador",
                "external-reference",
                DateTimeOffset.UtcNow));

        var result = await _service.RecibirWebhookAsync(new PayPerTicWebhookNotification
        {
            Id = providerId,
            Status = "approved"
        });

        result.Success.Should().BeFalse();
        result.Code.Should().Be(401);
        (await _context.GtwPagos.SingleAsync()).PagEstado.Should().Be("pending");
    }

    [Fact]
    public async Task RecibirWebhook_DuplicadoFueraDeOrden_NoHaceRetrocederEstadoFinal()
    {
        const string providerId = "ppt-state-regression";
        _context.GtwPagos.Add(BuildPago(providerId, "approved"));
        await _context.SaveChangesAsync();
        ConfigureReadBack(providerId, "pending");

        var result = await _service.RecibirWebhookAsync(new PayPerTicWebhookNotification
        {
            Id = providerId,
            Status = "pending"
        });

        result.Success.Should().BeTrue();
        (await _context.GtwPagos.SingleAsync()).PagEstado.Should().Be("approved");
    }

    [Fact]
    public async Task RecibirWebhook_DuplicadoProcesado_NoRepiteConsultaNiTransicion()
    {
        const string providerId = "ppt-inbox-duplicate";
        _context.GtwPagos.Add(BuildPago(providerId));
        await _context.SaveChangesAsync();
        ConfigureReadBack(providerId, "approved");
        var notification = new PayPerTicWebhookNotification
        {
            Id = providerId,
            Status = "approved",
            Type = "online"
        };

        var first = await _service.RecibirWebhookAsync(notification);
        var duplicate = await _service.RecibirWebhookAsync(notification);

        first.Success.Should().BeTrue();
        duplicate.Success.Should().BeTrue();
        var inbox = await _context.PayPerTicPaymentWebhookInbox.SingleAsync();
        inbox.Result.Should().Be(PayPerTicPaymentWebhookInboxResults.Processed);
        inbox.Attempts.Should().Be(1);
        _automaticDebitClient.Verify(x => x.GetPaymentReadBackAsync(
            2419,
            providerId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecibirWebhook_SiFallaReadBack_ConservaInboxYPermiteReintento()
    {
        const string providerId = "ppt-inbox-retry";
        _context.GtwPagos.Add(BuildPago(providerId));
        await _context.SaveChangesAsync();
        _automaticDebitClient
            .SetupSequence(x => x.GetPaymentReadBackAsync(
                2419,
                providerId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider unavailable"))
            .ReturnsAsync(new AutomaticDebitProviderReadBack(
                AutomaticDebitWebhookObjectTypes.Payment,
                providerId,
                "approved",
                "collector-test",
                "external-reference",
                DateTimeOffset.UtcNow));
        var notification = new PayPerTicWebhookNotification
        {
            Id = providerId,
            Status = "approved",
            Type = "online"
        };

        var failed = await _service.RecibirWebhookAsync(notification);
        var afterFailure = await _context.PayPerTicPaymentWebhookInbox.SingleAsync();
        failed.Success.Should().BeFalse();
        afterFailure.Result.Should().Be(PayPerTicPaymentWebhookInboxResults.Failed);
        afterFailure.Attempts.Should().Be(1);

        var retried = await _service.RecibirWebhookAsync(notification);

        retried.Success.Should().BeTrue();
        var inbox = await _context.PayPerTicPaymentWebhookInbox.SingleAsync();
        inbox.Result.Should().Be(PayPerTicPaymentWebhookInboxResults.Processed);
        inbox.Attempts.Should().Be(2);
        inbox.ProcessedAt.Should().NotBeNull();
        (await _context.GtwPagos.SingleAsync()).PagEstado.Should().Be("approved");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CancelarPagoAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelarPago_ConEstadoPending_CancelaPagoYActualizaBD()
    {
        // Arrange
        var payPerTicId = "ppt-cancelar";
        _context.GtwPagos.Add(BuildPago(pagIdExterno: payPerTicId, estado: "pending"));
        await _context.SaveChangesAsync();

        _mockClient
            .Setup(c => c.CancelPaymentAsync(payPerTicId, It.IsAny<PayPerTicCancelRequest>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CancelarPagoAsync(payPerTicId, "Cancelado por el usuario");

        // Assert
        result.Success.Should().BeTrue();
        result.Code.Should().Be(200);

        var pago = await _context.GtwPagos.FirstAsync(p => p.PagIdExterno == payPerTicId);
        pago.PagEstado.Should().Be("cancelled");
        pago.PagEstadoDetalle.Should().Be("Cancelado por el usuario");

        _mockClient.Verify(
            c => c.CancelPaymentAsync(payPerTicId, It.IsAny<PayPerTicCancelRequest>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelarPago_ConEstadoApproved_Retorna400()
    {
        // Arrange
        var payPerTicId = "ppt-aprobado";
        _context.GtwPagos.Add(BuildPago(pagIdExterno: payPerTicId, estado: "approved"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.CancelarPagoAsync(payPerTicId, "Cancelar");

        // Assert
        result.Success.Should().BeFalse();
        result.Code.Should().Be(400);

        // No debe haber llamado a PayPerTIC
        _mockClient.Verify(
            c => c.CancelPaymentAsync(It.IsAny<string>(), It.IsAny<PayPerTicCancelRequest>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelarPago_ConPagoNoEncontrado_Retorna404()
    {
        var result = await _service.CancelarPagoAsync("ppt-no-existe", "motivo");

        result.Success.Should().BeFalse();
        result.Code.Should().Be(404);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DevolverPagoAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DevolverPago_ConEstadoApproved_ProcesaDevolucion()
    {
        // Arrange
        var payPerTicId = "ppt-devolver";
        _context.GtwPagos.Add(BuildPago(pagIdExterno: payPerTicId, estado: "approved"));
        await _context.SaveChangesAsync();

        _mockClient
            .Setup(c => c.RefundPaymentAsync(payPerTicId, It.IsAny<PayPerTicRefundRequest>()))
            .ReturnsAsync(new PayPerTicRefundResponse
            {
                Id = "refund-001",
                Status = "approved",
                Type = "online",
                Amount = 500m,
                Reason = "Devolución solicitada por el contribuyente"
            });

        // Act
        var result = await _service.DevolverPagoAsync(
            payPerTicId, new PayPerTicRefundRequest { Type = "online", Reason = "Devolución" });

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be("approved");
        result.Data.Amount.Should().Be(500m);

        var pago = await _context.GtwPagos.FirstAsync(p => p.PagIdExterno == payPerTicId);
        pago.PagEstado.Should().Be("refunded");
    }

    [Fact]
    public async Task DevolverPago_ConEstadoPending_Retorna400()
    {
        // Arrange
        var payPerTicId = "ppt-pending";
        _context.GtwPagos.Add(BuildPago(pagIdExterno: payPerTicId, estado: "pending"));
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.DevolverPagoAsync(
            payPerTicId, new PayPerTicRefundRequest { Type = "online" });

        // Assert
        result.Success.Should().BeFalse();
        result.Code.Should().Be(400);
        _mockClient.Verify(
            c => c.RefundPaymentAsync(It.IsAny<string>(), It.IsAny<PayPerTicRefundRequest>()),
            Times.Never);
    }

    [Fact]
    public async Task DevolverPago_ConPagoNoEncontrado_Retorna404()
    {
        var result = await _service.DevolverPagoAsync(
            "ppt-no-existe", new PayPerTicRefundRequest { Type = "online" });

        result.Success.Should().BeFalse();
        result.Code.Should().Be(404);
    }

    [Fact]
    public async Task DevolverPago_CuandoRefundNoAprobado_NoCambiaEstadoEnBD()
    {
        // Arrange: el pago está approved, pero PayPerTIC devuelve un refund en estado "pending"
        var payPerTicId = "ppt-refund-pending";
        _context.GtwPagos.Add(BuildPago(pagIdExterno: payPerTicId, estado: "approved"));
        await _context.SaveChangesAsync();

        _mockClient
            .Setup(c => c.RefundPaymentAsync(payPerTicId, It.IsAny<PayPerTicRefundRequest>()))
            .ReturnsAsync(new PayPerTicRefundResponse
            {
                Id = "refund-002",
                Status = "pending",   // ← devolución aún no aprobada
                Amount = 500m
            });

        // Act
        var result = await _service.DevolverPagoAsync(
            payPerTicId, new PayPerTicRefundRequest { Type = "online" });

        // Assert: la operación es exitosa (200), pero el pago NO se marca como "refunded"
        result.Success.Should().BeTrue();
        result.Code.Should().Be(200);

        var pago = await _context.GtwPagos.FirstAsync(p => p.PagIdExterno == payPerTicId);
        pago.PagEstado.Should().Be("approved"); // sin cambio hasta que se apruebe la devolución
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CancelarPagoAsync — casos edge
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelarPago_CuandoPayPerTicLanzaExcepcion_Retorna500()
    {
        // Arrange
        var payPerTicId = "ppt-error-cancel";
        _context.GtwPagos.Add(BuildPago(pagIdExterno: payPerTicId, estado: "pending"));
        await _context.SaveChangesAsync();

        _mockClient
            .Setup(c => c.CancelPaymentAsync(payPerTicId, It.IsAny<PayPerTicCancelRequest>()))
            .ThrowsAsync(new HttpRequestException("PayPerTIC no disponible"));

        // Act
        var result = await _service.CancelarPagoAsync(payPerTicId, "motivo");

        // Assert
        result.Success.Should().BeFalse();
        result.Code.Should().Be(500);

        // El estado NO debe haberse cambiado a "cancelled" si PayPerTIC falló
        var pago = await _context.GtwPagos.FirstAsync(p => p.PagIdExterno == payPerTicId);
        pago.PagEstado.Should().Be("pending");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RecibirWebhookAsync — casos edge
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecibirWebhook_SinPaymentMethods_ActualizaEstadoSinRomper()
    {
        // Arrange: PayPerTIC puede enviar el webhook sin lista de métodos de pago
        var payPerTicId = "ppt-sin-metodos";
        _context.GtwPagos.Add(BuildPago(pagIdExterno: payPerTicId, estado: "pending"));
        await _context.SaveChangesAsync();
        ConfigureReadBack(payPerTicId, "approved");

        var notification = new PayPerTicWebhookNotification
        {
            Id = payPerTicId,
            Status = "approved",
            StatusDetail = "Pago aprobado",
            PaymentMethods = null   // ← sin métodos de pago
        };

        // Act
        var result = await _service.RecibirWebhookAsync(notification);

        // Assert: no debe lanzar excepción, debe actualizar el estado igual
        result.Success.Should().BeTrue();
        result.Code.Should().Be(200);

        var pago = await _context.GtwPagos.FirstAsync(p => p.PagIdExterno == payPerTicId);
        pago.PagEstado.Should().Be("approved");
        pago.PagMetodoPago.Should().Be(string.Empty); // sin cambio
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CrearCheckoutAsync — precedencia de CollectorId y NotificationUrl
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrearCheckout_CollectorIdDelRequest_NoPuedeReemplazarLaConfig()
    {
        // Arrange
        _mockClient
            .Setup(c => c.CreatePaymentAsync(It.IsAny<PayPerTicCreatePaymentRequest>()))
            .ReturnsAsync(new PayPerTicCreatePaymentResponse
            {
                Id = "ppt-id",
                FormUrl = "https://form.url",
                FinalAmount = 100m,
                Status = "pending"
            });

        var request = BuildCheckoutRequest();
        request.CollectorId = "collector-del-request"; // distinto al de config ("collector-test")

        // Act
        await _service.CrearCheckoutAsync(request);

        // Assert: un caller anónimo no puede redirigir la recaudación.
        _mockClient.Verify(c => c.CreatePaymentAsync(
            It.Is<PayPerTicCreatePaymentRequest>(r => r.CollectorId == "collector-test")),
            Times.Once);
    }

    [Fact]
    public async Task CrearCheckout_NotificationUrlDelRequest_NoPuedeReemplazarLaConfig()
    {
        // Arrange
        _mockClient
            .Setup(c => c.CreatePaymentAsync(It.IsAny<PayPerTicCreatePaymentRequest>()))
            .ReturnsAsync(new PayPerTicCreatePaymentResponse
            {
                Id = "ppt-id",
                FormUrl = "https://form.url",
                FinalAmount = 100m,
                Status = "pending"
            });

        var request = BuildCheckoutRequest();
        request.NotificationUrl = "https://custom.webhook.com/hook";

        // Act
        await _service.CrearCheckoutAsync(request);

        // Assert: un caller anónimo no puede desviar notificaciones.
        _mockClient.Verify(c => c.CreatePaymentAsync(
            It.Is<PayPerTicCreatePaymentRequest>(r =>
                r.NotificationUrl == "https://test.api.com/pagostic/webhook")),
            Times.Once);
    }

    [Fact]
    public async Task CrearCheckout_SinCollectorIdEnRequest_UsaCollectorIdDeConfig()
    {
        // Arrange
        _mockClient
            .Setup(c => c.CreatePaymentAsync(It.IsAny<PayPerTicCreatePaymentRequest>()))
            .ReturnsAsync(new PayPerTicCreatePaymentResponse
            {
                Id = "ppt-id",
                FormUrl = "https://form.url",
                FinalAmount = 100m,
                Status = "pending"
            });

        var request = BuildCheckoutRequest();
        request.CollectorId = null; // sin CollectorId → debe usar el de keys ("collector-test")

        // Act
        await _service.CrearCheckoutAsync(request);

        // Assert
        _mockClient.Verify(c => c.CreatePaymentAsync(
            It.Is<PayPerTicCreatePaymentRequest>(r => r.CollectorId == "collector-test")),
            Times.Once);
    }

    private static CreatePayPerTicCheckoutDto BuildCheckoutRequest(
        decimal totalAmount = 100m, int cantidadDetalles = 1)
    {
        var detalles = Enumerable.Range(1, cantidadDetalles).Select(i =>
            new CheckoutDetailItemDto
            {
                Amount = totalAmount / cantidadDetalles,
                ConceptId = $"0001",
                ConceptDescription = "Tasa de Seguridad e Higiene",
                IdObligacion = $"OBL-{i}",
                AnoCuota = 2024,
                NroCuota = i,
                CapitalFacturado = totalAmount / cantidadDetalles,
                DeudaActualizada = totalAmount / cantidadDetalles
            }).ToList();

        return new CreatePayPerTicCheckoutDto
        {
            ExternalTransactionId = $"TXN-{Guid.NewGuid():N}",
            CurrencyId = "ARS",
            JurisdiccionId = 2389,
            Origen = "web",
            Details = detalles,
            Payer = new CheckoutPayerDto
            {
                Name = "Juan Perez",
                Email = "juan@test.com",
                IdentificationNumber = "12345678",
                IdentificationType = "DNI_ARG",
                IdentificationCountry = "ARG"
            }
        };
    }

    private void ConfigureReadBack(string providerId, string status)
    {
        _automaticDebitClient
            .Setup(x => x.GetPaymentReadBackAsync(
                It.IsAny<long>(),
                providerId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderReadBack(
                AutomaticDebitWebhookObjectTypes.Payment,
                providerId,
                status,
                "collector-test",
                "external-reference",
                DateTimeOffset.UtcNow));
    }

    private static GtwPago BuildPago(string pagIdExterno, string estado = "pending") =>
        new()
        {
            PagFecha = DateTime.Now,
            PagOrigen = "web",
            PagModoPago = "PayPerTIC",
            PagFormaPago = "online",
            PagMetodoPago = string.Empty,
            PagEstado = estado,
            PagMoneda = "ARS",
            PagIdExterno = pagIdExterno,
            PagImporteAbonado = 500m,
            PagImporteCancelado = 500m,
            PagIdJurisdiccion = 2419
        };
}
