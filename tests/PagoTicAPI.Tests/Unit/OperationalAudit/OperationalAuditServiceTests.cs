using PagoTicAPI.Application.RequestDto.OperationalAudit;
using PagoTicAPI.Application.ResponseDto.OperationalAudit;
using System.Text.Json;
using PagoTicAPI.Application.Services.Implementations;
using PagoTicAPI.Domain.Models.AutomaticDebits;
using PagoTicAPI.Tests.Helpers;

namespace PagoTicAPI.Tests.Unit.OperationalAudit;

public sealed class OperationalAuditServiceTests
{
    [Fact]
    public async Task AutomaticDebits_AreFilteredOrderedPagedAndUseArgentinaOffset()
    {
        await using var context = CreateContext();
        context.AutomaticDebitOperations.AddRange(
            Operation(1, "ACC-1", "OBL-1", new DateTime(2026, 9, 18, 15, 30, 0)),
            Operation(2, "ACC-1", "OBL-2", new DateTime(2026, 9, 18, 15, 30, 0)),
            Operation(3, "ACC-2", "OBL-3", new DateTime(2026, 9, 19, 15, 30, 0)));
        await context.SaveChangesAsync();
        var service = new OperationalAuditService(context, TimeProvider.System);

        var response = await service.GetAutomaticDebitsAsync(
            new AutomaticDebitAuditFilter { TaxpayerAccountId = "ACC-1", PageSize = 1 },
            CancellationToken.None);

        response.Success.Should().BeTrue();
        response.TotalRows.Should().Be(2);
        response.Data!.Items.Should().ContainSingle().Which.Id.Should().Be(2);
        response.Data.Items[0].RequestedAt.Offset.Should().Be(TimeSpan.FromHours(-3));
        response.Data.Items[0].RequestedAt.Hour.Should().Be(12);
        response.Data.Items[0].DueDate.Should().Be(new DateTime(2026, 10, 14));
    }

    [Theory]
    [InlineData(0, 20, "page must be greater than or equal to 1.")]
    [InlineData(1, 101, "pageSize must be between 1 and 100.")]
    public async Task InvalidPagination_ReturnsClearBadRequest(int page, int pageSize, string message)
    {
        await using var context = CreateContext();
        var service = new OperationalAuditService(context, TimeProvider.System);

        var response = await service.GetAutomaticDebitsAsync(
            new AutomaticDebitAuditFilter { Page = page, PageSize = pageSize },
            CancellationToken.None);

        response.Code.Should().Be(400);
        response.Message.Should().Be(message);
    }

    [Fact]
    public async Task CheckoutAudit_FiltersByObligationWithoutReturningContributorOrWebhookPayload()
    {
        await using var context = CreateContext();
        var payment = new GtwPago
        {
            PagId = 10, PagFecha = new DateTime(2026, 9, 18, 15, 30, 0), PagOrigen = "web",
            PagModoPago = "online", PagFormaPago = "card", PagMetodoPago = "visa",
            PagEstado = "PENDING", PagMoneda = "ARS", PagImporteAbonado = 100,
            PagImporteCancelado = 0, PagIdJurisdiccion = 2419
        };
        payment.GtwPagoDetalles.Add(new GtwPagoDetalle
        {
            PgdId = 20, PgdPagId = 10, PgdIdTributoContribuyente = "ACC-1",
            PgdIdObligacion = "OBL-1", PgdConcepto = "Tax", PgdClaveBien = "0001",
            PgdContribuyente = "SENSITIVE NAME", PgdAnoCuota = 2026, PgdNroCuota = 1,
            PgdCapitalFacturado = 100, PgdIntereses = 0, PgdDeudaActualizada = 100
        });
        context.GtwPagos.Add(payment);
        context.PayPerTicPaymentWebhookInbox.Add(new PayPerTicPaymentWebhookInbox
        {
            Id = 1, JurisdictionId = 2419, PaymentId = 10, ProviderPaymentId = "provider-1",
            DeduplicationKey = "dedup", PayloadHash = "hash", Result = PayPerTicPaymentWebhookInboxResults.Processed,
            ReceivedAt = new DateTime(2026, 9, 18, 15, 31, 0)
        });
        await context.SaveChangesAsync();
        var service = new OperationalAuditService(context, TimeProvider.System);

        var list = await service.GetCheckoutsAsync(
            new CheckoutAuditFilter { ObligationId = "OBL-1" }, CancellationToken.None);
        var detail = await service.GetCheckoutAsync(10, CancellationToken.None);
        var history = await service.GetCheckoutHistoryAsync(10, new AuditHistoryFilter(), CancellationToken.None);

        list.Data!.Items.Should().ContainSingle();
        detail.Data!.Details.Should().ContainSingle();
        JsonSerializer.Serialize(detail.Data).Should().NotContain("SENSITIVE NAME");
        JsonSerializer.Serialize(history.Data).Should().NotContain("Payload").And.NotContain("hash");
    }

    [Fact]
    public async Task CheckoutAudit_UsesProviderPaymentIdContractAndFilter()
    {
        await using var context = CreateContext();
        context.GtwPagos.Add(Payment(11, 2419, "ppt-payment-11", new DateTime(2026, 9, 18, 15, 30, 0)));
        context.GtwPagos.Add(Payment(12, 2419, "ppt-payment-12", new DateTime(2026, 9, 18, 15, 31, 0)));
        await context.SaveChangesAsync();
        var service = new OperationalAuditService(context, TimeProvider.System);

        var response = await service.GetCheckoutsAsync(
            new CheckoutAuditFilter { ProviderPaymentId = "ppt-payment-11" }, CancellationToken.None);

        response.Data!.Items.Should().ContainSingle();
        response.Data.Items[0].ProviderPaymentId.Should().Be("ppt-payment-11");
        JsonSerializer.Serialize(response.Data).Should().NotContain("ExternalTransactionId");
    }

    [Fact]
    public async Task ExtremeHistoryPage_ReturnsBadRequestInsteadOfOverflow()
    {
        await using var context = CreateContext();
        var service = new OperationalAuditService(context, TimeProvider.System);

        var response = await service.GetAutomaticDebitHistoryAsync(
            1, new AuditHistoryFilter { Page = int.MaxValue, PageSize = 100 }, CancellationToken.None);

        response.Code.Should().Be(400);
        response.Message.Should().Be("page is too large.");
    }

    [Fact]
    public async Task Summary_AggregatesLocalDataByJurisdictionAndArgentinaDateRange()
    {
        await using var context = CreateContext();
        context.AutomaticDebitOperations.AddRange(
            Operation(21, "ACC-1", "OBL-1", new DateTime(2026, 9, 18, 15, 30, 0)),
            Operation(22, "ACC-2", "OBL-2", new DateTime(2026, 9, 18, 17, 0, 0)));
        context.AutomaticDebitWebhookInbox.Add(new AutomaticDebitWebhookInbox
        {
            Id = 31, JurisdictionId = 2419, DeduplicationKey = "d-31", PayloadHash = "hidden",
            Result = AutomaticDebitInboxResults.Processed, ReceivedAt = new DateTime(2026, 9, 18, 15, 35, 0)
        });
        context.GtwPagos.AddRange(
            Payment(41, 2419, "ppt-41", new DateTime(2026, 9, 18, 15, 40, 0)),
            Payment(42, 9999, "ppt-42", new DateTime(2026, 9, 18, 15, 45, 0)));
        context.PayPerTicPaymentWebhookInbox.Add(new PayPerTicPaymentWebhookInbox
        {
            Id = 51, JurisdictionId = 2419, PaymentId = 41, ProviderPaymentId = "ppt-41",
            DeduplicationKey = "d-51", PayloadHash = "hidden", Result = PayPerTicPaymentWebhookInboxResults.Processed,
            ReceivedAt = new DateTime(2026, 9, 18, 15, 50, 0)
        });
        await context.SaveChangesAsync();
        var service = new OperationalAuditService(context, TimeProvider.System);

        var response = await service.GetSummaryAsync(new OperationalSummaryFilter
        {
            JurisdictionId = 2419,
            From = new DateTime(2026, 9, 18, 12, 0, 0),
            To = new DateTime(2026, 9, 18, 13, 0, 0)
        }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Data!.AutomaticDebits.Total.Should().Be(1);
        response.Data.AutomaticDebits.TotalAmount.Should().Be(10);
        response.Data.Checkouts.Total.Should().Be(1);
        response.Data.Checkouts.TotalPaidAmount.Should().Be(100);
        response.Data.Inboxes.AutomaticDebitTotal.Should().Be(1);
        response.Data.Inboxes.CheckoutTotal.Should().Be(1);
        response.Data.From!.Value.Offset.Should().Be(TimeSpan.FromHours(-3));
        response.Data.GeneratedAt.Offset.Should().Be(TimeSpan.FromHours(-3));
        JsonSerializer.Serialize(response.Data).Should().NotContain("hidden");
    }

    private static gtwContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<gtwContext>()
            .UseInMemoryDatabase($"GatewayAudit_{Guid.NewGuid():N}").Options;
        return new TestGtwContext(options);
    }

    private static AutomaticDebitOperation Operation(long id, string account, string obligation, DateTime requestedAt) => new()
    {
        Id = id, AdhesionId = 1, JurisdictionId = 2419, TaxpayerAccountId = account,
        ObligationId = obligation, ExternalTransactionId = $"EXT-{id}", Amount = 10,
        DueDate = new DateTime(2026, 10, 14), ProviderState = "ISSUED",
        ProcessingState = AutomaticDebitProcessingStates.Pending, RequestedAt = requestedAt, CreatedAt = requestedAt,
        CreatedBy = "TEST"
    };

    private static GtwPago Payment(int id, int jurisdictionId, string providerPaymentId, DateTime createdAt) => new()
    {
        PagId = id, PagFecha = createdAt, PagOrigen = "web", PagModoPago = "online",
        PagFormaPago = "card", PagMetodoPago = "visa", PagEstado = "PENDING",
        PagMoneda = "ARS", PagImporteAbonado = 100, PagImporteCancelado = 0,
        PagIdJurisdiccion = jurisdictionId, PagIdExterno = providerPaymentId
    };
}



