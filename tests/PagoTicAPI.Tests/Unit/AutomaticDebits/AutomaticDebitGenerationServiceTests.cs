using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PagoTicAPI.Application.Clients.AutomaticDebits;
using PagoTicAPI.Application.Configuration.AutomaticDebits;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;
using PagoTicAPI.Application.Services.Implementations;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;
using PagoTicAPI.Domain.Context;
using PagoTicAPI.Domain.Models.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public class AutomaticDebitGenerationServiceTests
{
    private static readonly InMemoryDatabaseRoot SharedRoot = new();
    private const long JurisdictionId = 2419;
    private const string AccountId = "VGBIN88256";
    private const string ObligationId = "6012353270";
    private static readonly DateTime Now = new(2026, 9, 8, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAsync_WhenEligible_ReservesBeforeProviderAndPersistsIssuedOperation()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        AutomaticDebitProviderPaymentRequest? captured = null;
        fixture.Client.Setup(x => x.CreateDebitAsync(
                JurisdictionId, "ppt-adhesion", It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, AutomaticDebitProviderPaymentRequest, CancellationToken>((_, _, request, _) =>
            {
                context.AutomaticDebitOperations.Count().Should().Be(1, "the operation must exist before network I/O");
                captured = request;
            })
            .ReturnsAsync(new AutomaticDebitProviderPayment("ppt-payment", "issued"));

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));

        result.Code.Should().Be(200);
        result.Data.Should().BeEquivalentTo(new
        {
            Id = 8001L,
            JurisdictionId,
            TaxpayerAccountId = AccountId,
            ObligationId,
            ProviderTransactionId = "ppt-payment",
            ExternalTransactionId = "PPT-DA-2419-6012353270-8001",
            ProviderState = AutomaticDebitProviderStates.Issued,
            ProcessingState = "PENDING",
            Amount = 36469.09m
        });
        captured.Should().BeEquivalentTo(new AutomaticDebitProviderPaymentRequest(
            "PPT-DA-2419-6012353270-8001", new DateTimeOffset(new DateTime(2026, 12, 15), TimeSpan.FromHours(-3)),
            ObligationId, "6", "Tasa por Servicio a la Propiedad", 36469.09m));
    }

    [Fact]
    public async Task CreateAsync_WhenOpenOperationExists_ReturnsItWithoutProviderPost()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        context.AutomaticDebitOperations.Add(ExistingOperation());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));

        result.Code.Should().Be(200);
        result.Data!.Id.Should().Be(7000);
        fixture.Client.Verify(x => x.CreateDebitAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        (await context.AutomaticDebitOperations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenProviderOutcomeIsUncertain_KeepsReservationAndDoesNotRetry()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.Setup(x => x.CreateDebitAsync(
                JurisdictionId, "ppt-adhesion", It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AutomaticDebitProviderException("transport"));

        var first = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));
        var second = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));

        first.Code.Should().Be(502);
        second.Code.Should().Be(200);
        var reservation = await context.AutomaticDebitOperations.SingleAsync();
        reservation.ProviderTransactionId.Should().BeNull();
        reservation.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        fixture.Client.Verify(x => x.CreateDebitAsync(
            JurisdictionId, "ppt-adhesion", It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("400")]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("404")]
    [InlineData("408")]
    [InlineData("409")]
    [InlineData("422")]
    [InlineData("425")]
    [InlineData("429")]
    [InlineData("500")]
    [InlineData("502")]
    [InlineData("503")]
    [InlineData("timeout")]
    [InlineData("http_request_exception")]
    [InlineData("invalid_json")]
    public async Task CreateAsync_RealClientIssuanceFailure_PersistsPendingReservationWithoutRetry(string scenario)
    {
        var options = CreateOptions(Guid.NewGuid().ToString(), SharedRoot);
        await using var context = new gtwContext(options);
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        await context.SaveChangesAsync();

        var handler = new IssuanceFailureHandler(scenario);
        var configuration = new Mock<IJurisdictionPayPerTicConfigurationProvider>();
        configuration.Setup(x => x.GetActiveAsync(JurisdictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JurisdictionPayPerTicConfiguration(
                JurisdictionId, "https://auth.example.test/token", "https://api.example.test/",
                "user", "password", "client", "secret", "collector-2419",
                "https://callbacks.example.test/debits", "https://portal.example.test/return",
                "https://portal.example.test/back"));
        var tokens = new Mock<IPayPerTicJurisdictionTokenProvider>();
        tokens.Setup(x => x.GetTokenAsync(JurisdictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");
        var providerClient = new AutomaticDebitPayPerTicClient(
            new HttpClient(handler), configuration.Object, tokens.Object,
            NullLogger<AutomaticDebitPayPerTicClient>.Instance);
        var fixture = CreateFixture(context, providerClient: providerClient);

        var first = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));
        first.Code.Should().Be(502);
        await using var verification = new gtwContext(options);
        var reservation = await verification.AutomaticDebitOperations.SingleAsync();
        reservation.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        reservation.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Pending);
        reservation.ProviderTransactionId.Should().BeNull();
        reservation.ExternalTransactionId.Should().Be("PPT-DA-2419-6012353270-8001");

        var second = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));

        second.Code.Should().Be(200);
        second.Data!.Id.Should().Be(reservation.Id);
        handler.PostCount.Should().Be(1);
        (await verification.AutomaticDebitOperations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenClientReportsDefinitiveRejection_MarksReservationFailedAndRemainsIdempotent()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.Setup(x => x.CreateDebitAsync(
                JurisdictionId, "ppt-adhesion", It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AutomaticDebitProviderException(
                "rejected", failureKind: AutomaticDebitProviderFailureKind.DefinitiveRejection));

        var first = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));
        var second = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));

        first.Code.Should().Be(422);
        second.Code.Should().Be(200);
        second.Data!.Id.Should().Be(8001);
        var reservation = await context.AutomaticDebitOperations.SingleAsync();
        reservation.ProviderState.Should().Be(AutomaticDebitProviderStates.Rejected);
        reservation.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Failed);
        reservation.ExternalTransactionId.Should().Be("PPT-DA-2419-6012353270-8001");
        fixture.Client.Verify(x => x.CreateDebitAsync(
            JurisdictionId, "ppt-adhesion", It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(AutomaticDebitProviderStates.Pending, 409)]
    [InlineData(AutomaticDebitProviderStates.Cancelled, 409)]
    public async Task CreateAsync_WithoutActiveAdhesion_DoesNotReserveOrCallProvider(string adhesionState, int expectedCode)
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion(adhesionState));
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));

        result.Code.Should().Be(expectedCode);
        (await context.AutomaticDebitOperations.CountAsync()).Should().Be(0);
        fixture.Client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenObligationIsIneligible_DoesNotReserveOrCallProvider()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context, Snapshot() with { InstallmentNumber = "000" });

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));

        result.Code.Should().Be(422);
        result.Message.Should().Contain("installment_000_excluded");
        (await context.AutomaticDebitOperations.CountAsync()).Should().Be(0);
        fixture.Client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenDatabaseUniquenessDetectsCompetingReservation_ReturnsExistingWithoutProviderPost()
    {
        var root = new InMemoryDatabaseRoot();
        var options = CreateOptions(Guid.NewGuid().ToString(), root);
        await using var seed = new gtwContext(options);
        seed.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        await seed.SaveChangesAsync();
        await using var context = new FailingReservationContext(options, async cancellationToken =>
        {
            await using var competitor = new gtwContext(options);
            competitor.AutomaticDebitOperations.Add(ExistingOperation());
            await competitor.SaveChangesAsync(cancellationToken);
        });
        var fixture = CreateFixture(context);

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, ObligationId));

        result.Code.Should().Be(200);
        result.Data!.Id.Should().Be(7000);
        fixture.Client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReconcileAsync_WithExistingProviderId_UsesAuthoritativeReadBackWithoutLookup()
    {
        await using var context = CreateContext();
        context.AutomaticDebitOperations.Add(ExistingOperation());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadBack("approved", Now));

        var result = await fixture.Sut.ReconcileAsync(7000);

        result.Code.Should().Be(200);
        result.Data!.ProviderState.Should().Be(AutomaticDebitProviderStates.Approved);
        (await context.AutomaticDebitOperations.SingleAsync()).ProviderState
            .Should().Be(AutomaticDebitProviderStates.Approved);
        fixture.Client.Verify(x => x.GetPaymentReadBackAsync(
            JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Client.Verify(x => x.FindDebitByExternalIdAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AutomaticDebitProviderStates.Active)]
    [InlineData(AutomaticDebitProviderStates.Cancelled)]
    public async Task ReconcileAsync_WhenReservationIsPending_FindsPaymentAndPersistsAuthoritativeResult(string adhesionState)
    {
        await using var context = CreateContext();
        var adhesion = ActiveAdhesion(adhesionState);
        if (adhesionState == AutomaticDebitProviderStates.Cancelled)
            adhesion.DeactivatedAt = Now;
        context.AutomaticDebitAdhesions.Add(adhesion);
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        SetupMatch(fixture.Client);

        var result = await fixture.Sut.ReconcileAsync(7000);

        result.Code.Should().Be(200);
        result.Data!.ProviderTransactionId.Should().Be("ppt-reconciled");
        result.Data.ProviderState.Should().Be(AutomaticDebitProviderStates.Issued);
        (await context.AutomaticDebitOperations.SingleAsync()).ExternalTransactionId
            .Should().Be("PPT-DA-2419-6012353270-7000");
        (await context.AutomaticDebitEvents.SingleAsync()).ProviderObjectId.Should().Be("ppt-reconciled");
        fixture.Reader.Verify(x => x.FindAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var repeated = await fixture.Sut.ReconcileAsync(operation.Id);
        repeated.Code.Should().Be(200);
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(1);
        fixture.Client.Verify(x => x.GetAdhesionReadBackAsync(
            JurisdictionId, "ppt-adhesion", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_WhenRepeatedOutcomeIsUncertain_KeepsSameReservationAndExternalTransactionId()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.Setup(x => x.GetAdhesionReadBackAsync(
                JurisdictionId, "ppt-adhesion", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdhesionReadBack());
        fixture.Client.Setup(x => x.FindDebitByExternalIdAsync(JurisdictionId,
                It.IsAny<string>(), It.IsAny<DateTime>(), "13727", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AutomaticDebitProviderException("timeout"));

        var first = await fixture.Sut.ReconcileAsync(7000);
        var second = await fixture.Sut.ReconcileAsync(7000);

        first.Code.Should().Be(502);
        second.Code.Should().Be(502);
        var reservation = await context.AutomaticDebitOperations.SingleAsync();
        reservation.Id.Should().Be(7000);
        reservation.ProviderTransactionId.Should().BeNull();
        reservation.ExternalTransactionId.Should().Be("PPT-DA-2419-6012353270-7000");
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("readback_error")]
    [InlineData("wrong_id")]
    [InlineData("wrong_collector")]
    [InlineData("wrong_reference")]
    [InlineData("wrong_type")]
    public async Task ReconcileAsync_WhenCancelledAdhesionReadBackIsUnreliable_StaysPending(string scenario)
    {
        await using var context = CreateContext();
        var adhesion = ActiveAdhesion(AutomaticDebitProviderStates.Cancelled);
        adhesion.DeactivatedAt = Now;
        context.AutomaticDebitAdhesions.Add(adhesion);
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        SetupMatch(fixture.Client);
        if (scenario == "readback_error")
            fixture.Client.Setup(x => x.GetAdhesionReadBackAsync(JurisdictionId, "ppt-adhesion", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AutomaticDebitProviderException("readback failed"));
        else
            fixture.Client.Setup(x => x.GetAdhesionReadBackAsync(JurisdictionId, "ppt-adhesion", It.IsAny<CancellationToken>()))
                .ReturnsAsync(scenario switch
                {
                    "wrong_id" => AdhesionReadBack() with { Id = "other" },
                    "wrong_collector" => AdhesionReadBack() with { CollectorId = "" },
                    "wrong_reference" => AdhesionReadBack() with { ExternalReference = "other" },
                    _ => AdhesionReadBack() with { ObjectType = AutomaticDebitWebhookObjectTypes.Payment }
                });

        (await fixture.Sut.ReconcileAsync(operation.Id)).Code.Should().Be(502);
        (await context.AutomaticDebitOperations.SingleAsync()).ProviderTransactionId.Should().BeNull();
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(0);
        fixture.Client.Verify(x => x.FindDebitByExternalIdAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Client.Verify(x => x.CreateDebitAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed_concept")]
    public async Task ReconcileAsync_DoesNotConsultCurrentObligation(string scenario)
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        SetupMatch(fixture.Client);
        fixture.Reader.Setup(x => x.FindAsync(JurisdictionId, ObligationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scenario == "missing" ? null : Snapshot() with { TaxTypeId = "changed", Concept = "Changed" });
        fixture.Client.Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "ppt-reconciled", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullReadBack() with { DetailConceptId = "old", DetailConceptDescription = "Old" });

        (await fixture.Sut.ReconcileAsync(operation.Id)).Code.Should().Be(200);
        fixture.Reader.Verify(x => x.FindAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        (await context.AutomaticDebitOperations.SingleAsync()).ProviderTransactionId.Should().Be("ppt-reconciled");
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_WhenLocalAdhesionAccountDiffers_DoesNotCallProvider()
    {
        await using var context = CreateContext();
        var adhesion = ActiveAdhesion();
        adhesion.TaxpayerAccountId = "other";
        context.AutomaticDebitAdhesions.Add(adhesion);
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);

        (await fixture.Sut.ReconcileAsync(operation.Id)).Code.Should().Be(502);
        operation.ProviderTransactionId.Should().BeNull();
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("payment_readback_error")]
    [InlineData("lookup_collector_mismatch")]
    public async Task ReconcileAsync_WhenProviderCorrelationFails_PreservesPendingReservation(string scenario)
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        SetupMatch(fixture.Client);
        if (scenario == "payment_readback_error")
            fixture.Client.Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "ppt-reconciled", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AutomaticDebitProviderException("payment readback failed"));
        else
            fixture.Client.Setup(x => x.FindDebitByExternalIdAsync(JurisdictionId,
                    operation.ExternalTransactionId, operation.RequestedAt, "13727", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Match() with { CollectorId = "current-collector" });

        (await fixture.Sut.ReconcileAsync(operation.Id)).Code.Should().Be(502);
        var saved = await context.AutomaticDebitOperations.SingleAsync();
        saved.ProviderTransactionId.Should().BeNull();
        saved.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(0);
        fixture.Client.Verify(x => x.CreateDebitAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_WhenLookupFindsMultiplePayments_LeavesReservationPendingWithoutPost()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.Setup(x => x.GetAdhesionReadBackAsync(
                JurisdictionId, "ppt-adhesion", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdhesionReadBack());
        fixture.Client.Setup(x => x.FindDebitByExternalIdAsync(
                JurisdictionId, operation.ExternalTransactionId, operation.RequestedAt,
                "13727", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AutomaticDebitProviderException("Multiple payment matches."));

        var result = await fixture.Sut.ReconcileAsync(operation.Id);

        result.Code.Should().Be(502);
        var saved = await context.AutomaticDebitOperations.SingleAsync();
        saved.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        saved.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Pending);
        saved.ProviderTransactionId.Should().BeNull();
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(0);
        fixture.Client.Verify(x => x.GetPaymentReadBackAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_WhenConcurrent_LooksUpOnceAndBothCallersObserveSameOperation()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        SetupMatch(fixture.Client);
        fixture.Client.Setup(x => x.FindDebitByExternalIdAsync(
                JurisdictionId, It.IsAny<string>(), It.IsAny<DateTime>(), "13727", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Delay(25);
                return Match();
            });

        var results = await Task.WhenAll(
            fixture.Sut.ReconcileAsync(7000),
            fixture.Sut.ReconcileAsync(7000));

        results.Should().OnlyContain(x => x.Code == 200 && x.Data!.Id == 7000);
        results.Should().OnlyContain(x => x.Data!.ProviderTransactionId == "ppt-reconciled");
        (await context.AutomaticDebitOperations.CountAsync()).Should().Be(1);
        fixture.Client.Verify(x => x.FindDebitByExternalIdAsync(
            JurisdictionId, It.IsAny<string>(), It.IsAny<DateTime>(), "13727", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Client.Verify(x => x.GetPaymentReadBackAsync(
            JurisdictionId, "ppt-reconciled", It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_WhenProviderApproved_UpdatesOperationAndCreatesIdempotentEvent()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        context.AutomaticDebitOperations.Add(ExistingOperation());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderReadBack(
                AutomaticDebitWebhookObjectTypes.Payment,
                "existing-payment",
                "approved",
                "13727",
                "PPT-DA-2419-6012353270-7000",
                new DateTimeOffset(Now)));

        var first = await fixture.Sut.GetAsync(7000);
        var second = await fixture.Sut.GetAsync(7000);

        first.Code.Should().Be(200);
        first.Data!.ProviderState.Should().Be(AutomaticDebitProviderStates.Approved);
        second.Code.Should().Be(200);
        var saved = await context.AutomaticDebitOperations.SingleAsync();
        saved.ProviderState.Should().Be(AutomaticDebitProviderStates.Approved);
        saved.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Pending);
        saved.ApprovedAt.Should().Be(Now);
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_WhenProviderOverdue_UpdatesTerminalStateWithoutMarkingRejection()
    {
        await using var context = CreateContext();
        context.AutomaticDebitOperations.Add(ExistingOperation());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadBack("overdue", Now.AddDays(1)));

        var result = await fixture.Sut.GetAsync(7000);

        result.Code.Should().Be(200);
        result.Data!.ProviderState.Should().Be(AutomaticDebitProviderStates.Overdue);
        var saved = await context.AutomaticDebitOperations.SingleAsync();
        saved.ProviderState.Should().Be(AutomaticDebitProviderStates.Overdue);
        saved.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Pending);
        saved.RejectedAt.Should().BeNull();
        (await context.AutomaticDebitEvents.SingleAsync()).ProviderState
            .Should().Be(AutomaticDebitProviderStates.Overdue);
    }

    [Theory]
    [InlineData(AutomaticDebitProviderStates.InProcess, "issued")]
    [InlineData(AutomaticDebitProviderStates.Approved, "rejected")]
    [InlineData(AutomaticDebitProviderStates.Rejected, "approved")]
    [InlineData(AutomaticDebitProviderStates.Cancelled, "in_process")]
    [InlineData(AutomaticDebitProviderStates.Overdue, "approved")]
    public async Task GetAsync_DoesNotRegressOrReplaceTerminalPaymentState(
        string currentState,
        string reportedState)
    {
        await using var context = CreateContext();
        var operation = ExistingOperation();
        operation.ProviderState = currentState;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadBack(reportedState, Now.AddMinutes(1)));

        var result = await fixture.Sut.GetAsync(7000);

        result.Code.Should().Be(200);
        result.Data!.ProviderState.Should().Be(currentState);
        (await context.AutomaticDebitOperations.SingleAsync()).ProviderState.Should().Be(currentState);
    }

    [Fact]
    public async Task GetAsync_WhenProviderReferenceDoesNotMatch_DoesNotMutateOperation()
    {
        await using var context = CreateContext();
        context.AutomaticDebitOperations.Add(ExistingOperation());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderReadBack(
                AutomaticDebitWebhookObjectTypes.Payment,
                "existing-payment",
                "approved",
                "13727",
                "different-reference",
                new DateTimeOffset(Now)));

        var result = await fixture.Sut.GetAsync(7000);

        result.Code.Should().Be(502);
        (await context.AutomaticDebitOperations.SingleAsync()).ProviderState
            .Should().Be(AutomaticDebitProviderStates.Issued);
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CancelAsync_WhenProviderPaymentIsIssued_CancelsAndPersistsAuthoritativeState()
    {
        await using var context = CreateContext();
        context.AutomaticDebitOperations.Add(ExistingOperation());
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client.SetupSequence(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadBack("issued", Now.AddMinutes(-1)))
            .ReturnsAsync(ReadBack("cancelled", Now));

        var result = await fixture.Sut.CancelAsync(
            7000,
            new CancelAutomaticDebitRequest("Controlled sandbox cancellation"));

        result.Code.Should().Be(200);
        result.Data!.ProviderState.Should().Be(AutomaticDebitProviderStates.Cancelled);
        fixture.Client.Verify(x => x.CancelPaymentAsync(
            JurisdictionId,
            "existing-payment",
            "Controlled sandbox cancellation",
            It.IsAny<CancellationToken>()), Times.Once);
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CancelAsync_WhenOperationIsAlreadyCancelled_ReturnsSuccessWithoutCallingProviderOrAddingEvents()
    {
        await using var context = CreateContext();
        var operation = ExistingOperation();
        operation.ProviderState = AutomaticDebitProviderStates.Cancelled;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);

        var result = await fixture.Sut.CancelAsync(
            7000,
            new CancelAutomaticDebitRequest("Repeated cancellation"));

        result.Code.Should().Be(200);
        result.Data!.ProviderState.Should().Be(AutomaticDebitProviderStates.Cancelled);
        fixture.Client.Verify(x => x.GetPaymentReadBackAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Client.Verify(x => x.CancelPaymentAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(AutomaticDebitProviderStates.Approved, "issued")]
    [InlineData(AutomaticDebitProviderStates.Approved, "rejected")]
    [InlineData(AutomaticDebitProviderStates.InProcess, "issued")]
    [InlineData(AutomaticDebitProviderStates.Rejected, "approved")]
    [InlineData(AutomaticDebitProviderStates.Cancelled, "in_process")]
    [InlineData(AutomaticDebitProviderStates.Overdue, "approved")]
    public async Task GetAsync_ConcurrentStateWrite_DoesNotOverwriteNewerState(
        string competingState, string staleReadBackState)
    {
        var options = CreateOptions(Guid.NewGuid().ToString(), SharedRoot);
        await using var seed = new gtwContext(options);
        seed.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        seed.AutomaticDebitOperations.Add(ExistingOperation());
        await seed.SaveChangesAsync();

        await using var staleContext = new gtwContext(options);
        await using var competingContext = new gtwContext(options);
        var fixture = CreateFixture(staleContext);
        var enteredReadBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReadBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                enteredReadBack.SetResult();
                await releaseReadBack.Task;
                return ReadBack(staleReadBackState, Now);
            });

        var pendingGet = fixture.Sut.GetAsync(7000);
        await enteredReadBack.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var competing = await competingContext.AutomaticDebitOperations.SingleAsync();
        competing.ProviderState = competingState;
        await competingContext.SaveChangesAsync();
        releaseReadBack.SetResult();

        var result = await pendingGet.WaitAsync(TimeSpan.FromSeconds(10));
        result.Code.Should().Be(200);
        await using var verification = new gtwContext(options);
        (await verification.AutomaticDebitOperations.SingleAsync()).ProviderState.Should().Be(competingState);
        fixture.Client.Verify(x => x.GetPaymentReadBackAsync(
            JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_ConcurrentProviderIdWrite_DoesNotReplaceCompetingIdOrPost()
    {
        var options = CreateOptions(Guid.NewGuid().ToString(), SharedRoot);
        await using var seed = new gtwContext(options);
        seed.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        seed.AutomaticDebitOperations.Add(operation);
        await seed.SaveChangesAsync();

        await using var staleContext = new gtwContext(options);
        await using var competingContext = new gtwContext(options);
        var fixture = CreateFixture(staleContext);
        SetupMatch(fixture.Client);
        var enteredReadBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReadBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "ppt-reconciled", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                enteredReadBack.SetResult();
                await releaseReadBack.Task;
                return FullReadBack();
            });

        var pendingReconcile = fixture.Sut.ReconcileAsync(7000);
        await enteredReadBack.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var competing = await competingContext.AutomaticDebitOperations.SingleAsync();
        competing.ProviderTransactionId = "other-provider-payment";
        competing.ProviderState = AutomaticDebitProviderStates.Approved;
        await competingContext.SaveChangesAsync();
        releaseReadBack.SetResult();

        var result = await pendingReconcile.WaitAsync(TimeSpan.FromSeconds(10));
        result.Code.Should().BeOneOf(200, 409, 502);
        await using var verification = new gtwContext(options);
        var persisted = await verification.AutomaticDebitOperations.SingleAsync();
        persisted.ProviderTransactionId.Should().Be("other-provider-payment");
        persisted.ProviderState.Should().Be(AutomaticDebitProviderStates.Approved);
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AutomaticDebitProviderStates.Approved)]
    [InlineData(AutomaticDebitProviderStates.Issued)]
    public async Task ReconcileAsync_ConcurrentSameProviderIdWrite_PreservesNewerStateWithoutPost(
        string competingState)
    {
        var options = CreateOptions(Guid.NewGuid().ToString(), SharedRoot);
        await using var seed = new gtwContext(options);
        seed.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        seed.AutomaticDebitOperations.Add(operation);
        await seed.SaveChangesAsync();

        await using var staleContext = new gtwContext(options);
        await using var competingContext = new gtwContext(options);
        var fixture = CreateFixture(staleContext);
        SetupMatch(fixture.Client);
        var enteredReadBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReadBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "ppt-reconciled", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                enteredReadBack.SetResult();
                await releaseReadBack.Task;
                return FullReadBack();
            });

        var pendingReconcile = fixture.Sut.ReconcileAsync(operation.Id);
        await enteredReadBack.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var competing = await competingContext.AutomaticDebitOperations.SingleAsync();
        competing.ProviderTransactionId = "ppt-reconciled";
        competing.ProviderState = competingState;
        await competingContext.SaveChangesAsync();
        releaseReadBack.SetResult();

        var result = await pendingReconcile.WaitAsync(TimeSpan.FromSeconds(10));
        result.Code.Should().Be(200);
        await using var verification = new gtwContext(options);
        var persisted = await verification.AutomaticDebitOperations.SingleAsync();
        persisted.ProviderTransactionId.Should().Be("ppt-reconciled");
        persisted.ProviderState.Should().Be(competingState);
        fixture.Client.Verify(x => x.GetPaymentReadBackAsync(
            JurisdictionId, "ppt-reconciled", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetOrReconcileAsync_ConcurrentEventInsert_RecoversUniqueCollisionWithoutSecondProviderCall(
        bool reconcile)
    {
        var options = CreateOptions(Guid.NewGuid().ToString(), SharedRoot);
        await using var seed = new gtwContext(options);
        seed.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        var operation = ExistingOperation();
        if (reconcile)
        {
            operation.ProviderTransactionId = null;
            operation.ProviderState = AutomaticDebitProviderStates.Pending;
        }
        seed.AutomaticDebitOperations.Add(operation);
        await seed.SaveChangesAsync();

        await using var racingContext = new EventCollisionContext(options);
        var fixture = CreateFixture(racingContext);
        if (reconcile)
            SetupMatch(fixture.Client);
        else
            fixture.Client.Setup(x => x.GetPaymentReadBackAsync(
                    JurisdictionId, "existing-payment", It.IsAny<CancellationToken>()))
                .ReturnsAsync(ReadBack("approved", Now));

        var result = reconcile
            ? await fixture.Sut.ReconcileAsync(operation.Id).WaitAsync(TimeSpan.FromSeconds(10))
            : await fixture.Sut.GetAsync(operation.Id).WaitAsync(TimeSpan.FromSeconds(10));

        result.Code.Should().Be(200);
        racingContext.InjectedCollisions.Should().Be(1);
        racingContext.SaveAttempts.Should().Be(2, "the local recovery must be bounded to one retry");
        await using var verification = new gtwContext(options);
        var persisted = await verification.AutomaticDebitOperations.SingleAsync();
        persisted.ProviderTransactionId.Should().Be(reconcile ? "ppt-reconciled" : "existing-payment");
        persisted.ProviderState.Should().Be(reconcile
            ? AutomaticDebitProviderStates.Issued
            : AutomaticDebitProviderStates.Approved);
        (await verification.AutomaticDebitEvents.CountAsync()).Should().Be(1);
        fixture.Client.Verify(x => x.GetPaymentReadBackAsync(JurisdictionId,
            reconcile ? "ppt-reconciled" : "existing-payment", It.IsAny<CancellationToken>()), Times.Once);
        if (reconcile)
        {
            fixture.Client.Verify(x => x.GetAdhesionReadBackAsync(
                JurisdictionId, "ppt-adhesion", It.IsAny<CancellationToken>()), Times.Once);
            fixture.Client.Verify(x => x.FindDebitByExternalIdAsync(
                JurisdictionId, operation.ExternalTransactionId, operation.RequestedAt,
                "13727", It.IsAny<CancellationToken>()), Times.Once);
        }
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Fixture CreateFixture(
        gtwContext context,
        AutomaticDebitObligationSnapshot? snapshot = null,
        IAutomaticDebitPayPerTicClient? providerClient = null)
    {
        var reader = new Mock<IAutomaticDebitObligationReader>();
        reader.Setup(x => x.FindAsync(JurisdictionId, ObligationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot ?? Snapshot());
        var client = new Mock<IAutomaticDebitPayPerTicClient>();
        var ids = new Mock<IAutomaticDebitIdGenerator>();
        ids.Setup(x => x.NextOperationIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(8001);
        var webhookIds = new Mock<IAutomaticDebitWebhookIdGenerator>();
        var nextEventId = 9000L;
        webhookIds.Setup(x => x.NextEventIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++nextEventId);
        var sut = new AutomaticDebitGenerationService(
            context, reader.Object, providerClient ?? client.Object, ids.Object, webhookIds.Object,
            new AutomaticDebitOperationCreationLock(),
            Options.Create(new AutomaticDebitFeatureOptions { Enabled = true }), new FrozenTimeProvider(Now),
            Mock.Of<ILogger<AutomaticDebitGenerationService>>());
        return new(sut, client, reader);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong_amount")]
    [InlineData("wrong_currency")]
    [InlineData("missing_currency")]
    [InlineData("wrong_readback")]
    [InlineData("wrong_type")]
    [InlineData("wrong_reference")]
    [InlineData("wrong_payment_id")]
    [InlineData("wrong_external_id")]
    [InlineData("missing_details")]
    public async Task ReconcileAsync_WithoutStrongAuthoritativeMatch_RemainsPending(string scenario)
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ActiveAdhesion());
        var operation = ExistingOperation();
        operation.ProviderTransactionId = null;
        operation.ProviderState = AutomaticDebitProviderStates.Pending;
        context.AutomaticDebitOperations.Add(operation);
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        SetupMatch(fixture.Client);
        if (scenario == "missing")
            fixture.Client.Setup(x => x.FindDebitByExternalIdAsync(JurisdictionId,
                It.IsAny<string>(), It.IsAny<DateTime>(), "13727", It.IsAny<CancellationToken>()))
                .ReturnsAsync((AutomaticDebitProviderPaymentMatch?)null);
        if (scenario != "missing")
            fixture.Client.Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "ppt-reconciled", It.IsAny<CancellationToken>()))
                .ReturnsAsync(scenario switch
                {
                    "wrong_amount" => FullReadBack() with { DetailAmount = 1m },
                    "wrong_currency" => FullReadBack() with { PaymentCurrencyId = "USD" },
                    "missing_currency" => FullReadBack() with { PaymentCurrencyId = null },
                    "wrong_readback" => FullReadBack() with { CollectorId = "wrong" },
                    "wrong_type" => FullReadBack() with { PaymentType = "online" },
                    "wrong_reference" => FullReadBack() with { DetailExternalReference = "other" },
                    "wrong_payment_id" => FullReadBack() with { Id = "other" },
                    "wrong_external_id" => FullReadBack() with { ExternalReference = "other" },
                    _ => FullReadBack() with { DetailAmount = null }
                });

        var result = await fixture.Sut.ReconcileAsync(operation.Id);

        result.Code.Should().Be(502);
        operation.ProviderTransactionId.Should().BeNull();
        operation.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        fixture.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AutomaticDebitProviderPaymentMatch Match() => new(
        "ppt-reconciled", "PPT-DA-2419-6012353270-7000", "13727");

    private static AutomaticDebitProviderReadBack FullReadBack() => new(
        AutomaticDebitWebhookObjectTypes.Payment, "ppt-reconciled", "issued", "13727",
        "PPT-DA-2419-6012353270-7000", new DateTimeOffset(Now))
    {
        PaymentType = "debit", PaymentCurrencyId = "ARS", DetailAmount = 36469.09m,
        DetailExternalReference = ObligationId, DetailConceptId = "6",
        DetailConceptDescription = "Tasa por Servicio a la Propiedad"
    };

    private static void SetupMatch(Mock<IAutomaticDebitPayPerTicClient> client)
    {
        client.Setup(x => x.FindDebitByExternalIdAsync(JurisdictionId,
                "PPT-DA-2419-6012353270-7000", Now.AddMinutes(-1), "13727", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Match());
        client.Setup(x => x.GetAdhesionReadBackAsync(JurisdictionId, "ppt-adhesion", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdhesionReadBack());
        client.Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "ppt-reconciled", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullReadBack());
    }

    private static gtwContext CreateContext() => new(CreateOptions(
        Guid.NewGuid().ToString(), SharedRoot));

    private static DbContextOptions<gtwContext> CreateOptions(string name, InMemoryDatabaseRoot root) =>
        new DbContextOptionsBuilder<gtwContext>().UseInMemoryDatabase(name, root).Options;

    private static AutomaticDebitObligationSnapshot Snapshot() => new(
        ObligationId, JurisdictionId, AccountId, "6", "Tasa por Servicio a la Propiedad", "012", "2026",
        new DateTime(2026, 12, 15), "PP", "DN", false, 36469.09m, 0m);

    private static AutomaticDebitAdhesion ActiveAdhesion(string state = AutomaticDebitProviderStates.Active) => new()
    {
        Id = 4, JurisdictionId = JurisdictionId, TaxpayerAccountId = AccountId, PersonId = "MVB343570",
        ProviderAdhesionId = "ppt-adhesion", ExternalReference = AccountId, ProviderState = state,
        ProcessingState = AutomaticDebitProcessingStates.Pending, RequestedAt = Now.AddDays(-1),
        CreatedBy = "test", CreatedAt = Now.AddDays(-1)
    };

    private static AutomaticDebitOperation ExistingOperation() => new()
    {
        Id = 7000, AdhesionId = 4, JurisdictionId = JurisdictionId, TaxpayerAccountId = AccountId,
        ObligationId = ObligationId, ProviderTransactionId = "existing-payment",
        ExternalTransactionId = "PPT-DA-2419-6012353270-7000", Amount = 36469.09m,
        DueDate = new DateTime(2026, 12, 15), ProviderState = AutomaticDebitProviderStates.Issued,
        ProcessingState = AutomaticDebitProcessingStates.Pending, RequestedAt = Now.AddMinutes(-1),
        CreatedBy = "test", CreatedAt = Now.AddMinutes(-1)
    };

    private static AutomaticDebitProviderReadBack ReadBack(string status, DateTime lastUpdate) => new(
        AutomaticDebitWebhookObjectTypes.Payment,
        "existing-payment",
        status,
        "13727",
        "PPT-DA-2419-6012353270-7000",
        new DateTimeOffset(lastUpdate));

    private static AutomaticDebitProviderReadBack AdhesionReadBack() => new(
        AutomaticDebitWebhookObjectTypes.Subscription, "ppt-adhesion", "active", "13727",
        AccountId, new DateTimeOffset(Now));

    private sealed record Fixture(
        AutomaticDebitGenerationService Sut,
        Mock<IAutomaticDebitPayPerTicClient> Client,
        Mock<IAutomaticDebitObligationReader> Reader);

    private sealed class IssuanceFailureHandler(string scenario) : HttpMessageHandler
    {
        public int PostCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Method.Should().Be(HttpMethod.Post);
            PostCount++;
            if (scenario == "timeout")
                return Task.FromException<HttpResponseMessage>(new TaskCanceledException("Simulated timeout."));
            if (scenario == "http_request_exception")
                return Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated transport failure."));

            var statusCode = scenario == "invalid_json"
                ? HttpStatusCode.OK
                : (HttpStatusCode)int.Parse(scenario, System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    scenario == "invalid_json" ? "{" : "{\"code\":\"failure\"}",
                    Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FrozenTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class FailingReservationContext(
        DbContextOptions<gtwContext> options,
        Func<CancellationToken, Task> beforeFailure) : gtwContext(options)
    {
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await beforeFailure(cancellationToken);
            throw new DbUpdateException("Simulated unique operation race.");
        }
    }

    private sealed class EventCollisionContext : gtwContext
    {
        private readonly DbContextOptions<gtwContext> _options;

        public EventCollisionContext(DbContextOptions<gtwContext> options) : base(options) =>
            _options = options;

        public int InjectedCollisions { get; private set; }
        public int SaveAttempts { get; private set; }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (InjectedCollisions != 0)
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            var pendingEvent = ChangeTracker.Entries<AutomaticDebitEvent>()
                .Single(x => x.State == EntityState.Added);
            var competingEvent = (AutomaticDebitEvent)pendingEvent.CurrentValues.ToObject();
            InjectedCollisions++;
            await using var competitor = new gtwContext(_options);
            competitor.AutomaticDebitEvents.Add(competingEvent);
            await competitor.SaveChangesAsync(cancellationToken);

            var constructor = typeof(Oracle.ManagedDataAccess.Client.OracleException).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(int), typeof(string), typeof(string), typeof(string), typeof(int)], null)!;
            var oracle = (Oracle.ManagedDataAccess.Client.OracleException)constructor.Invoke(
                [1, "", "", "ORA-00001: unique constraint (UK_DEB_AUT_EVENTO_DEDUP) violated", 0]);
            throw new DbUpdateException("event unique collision", oracle);
        }
    }
}

