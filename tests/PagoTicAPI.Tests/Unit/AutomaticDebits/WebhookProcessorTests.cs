using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PagoTicAPI.Application.Clients.AutomaticDebits;
using PagoTicAPI.Application.Configuration.AutomaticDebits;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;
using PagoTicAPI.Application.Services.Implementations;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;
using PagoTicAPI.Domain.Context;
using PagoTicAPI.Domain.Models.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public class WebhookProcessorTests
{
    private const long JurisdictionId = 2419;
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessAsync_FeatureDisabledRejectsBeforeConfigurationReadBackOrPersistence()
    {
        await using var context = CreateContext();
        var fixture = CreateFixture(context, enabled: false);

        var result = await fixture.Sut.ProcessAsync(
            JurisdictionId,
            "{\"type\":\"debit\",\"id\":\"pay-1\"}"u8.ToArray());

        result.StatusCode.Should().Be(404);
        result.Outcome.Should().Be("feature_disabled");
        fixture.Client.VerifyNoOtherCalls();
        (await context.AutomaticDebitWebhookInbox.CountAsync()).Should().Be(0);
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_MalformedPayloadRejectsWithoutPersistenceOrProviderCall()
    {
        await using var context = CreateContext();
        var fixture = CreateFixture(context);
        var raw = "not-json"u8.ToArray();

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, raw);

        result.StatusCode.Should().Be(400);
        result.Outcome.Should().Be("malformed_hint");
        context.AutomaticDebitWebhookInbox.Should().BeEmpty();
        context.AutomaticDebitEvents.Should().BeEmpty();
        fixture.Client.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("debit", "unknown-payment", "operation_not_found")]
    [InlineData("subscription", "unknown-adhesion", "adhesion_not_found")]
    public async Task ProcessAsync_UnknownLocalCorrelationRejectsBeforeProviderReadBack(
        string notificationType,
        string providerId,
        string expectedOutcome)
    {
        await using var context = CreateContext();
        var fixture = CreateFixture(context);
        var raw = System.Text.Encoding.UTF8.GetBytes(
            $"{{\"type\":\"{notificationType}\",\"id\":\"{providerId}\"}}");

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, raw);

        result.StatusCode.Should().Be(404);
        result.Outcome.Should().Be(expectedOutcome);
        context.AutomaticDebitWebhookInbox.Should().BeEmpty();
        context.AutomaticDebitEvents.Should().BeEmpty();
        fixture.Client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_ReadBackUnavailableReturnsRetryableStatusWithoutMutation()
    {
        await using var context = CreateContext();
        SeedOperation(context);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AutomaticDebitProviderException("unavailable", 503));

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        result.StatusCode.Should().Be(503);
        context.AutomaticDebitWebhookInbox.Should().BeEmpty();
        context.AutomaticDebitEvents.Should().BeEmpty();
        context.AutomaticDebitOperations.Single().ProviderState.Should().Be(AutomaticDebitProviderStates.Issued);
    }

    [Theory]
    [InlineData("wrong-collector", "ext-1")]
    [InlineData("collector-2419", "wrong-reference")]
    public async Task ProcessAsync_AuthoritativeMismatchReturnsUnauthorizedWithoutMutation(
        string collectorId,
        string externalReference)
    {
        await using var context = CreateContext();
        SeedOperation(context);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentReadBack(collectorId, externalReference));

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        result.StatusCode.Should().Be(401);
        context.AutomaticDebitWebhookInbox.Should().BeEmpty();
        context.AutomaticDebitEvents.Should().BeEmpty();
        context.AutomaticDebitOperations.Single().ProviderState.Should().Be(AutomaticDebitProviderStates.Issued);
    }

    [Theory]
    [InlineData("payment", "different-payment")]
    [InlineData("subscription", "pay-1")]
    public async Task ProcessAsync_AuthoritativeTypeOrProviderIdMismatchReturnsUnauthorized(
        string objectType,
        string providerId)
    {
        await using var context = CreateContext();
        SeedOperation(context);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentReadBack() with { ObjectType = objectType, Id = providerId });

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        result.StatusCode.Should().Be(401);
        context.AutomaticDebitWebhookInbox.Should().BeEmpty();
        context.AutomaticDebitEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_UnsupportedAuthoritativeStateReturnsUnprocessableWithoutMutation()
    {
        await using var context = CreateContext();
        SeedOperation(context);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentReadBack() with { Status = "unknown" });

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        result.StatusCode.Should().Be(422);
        context.AutomaticDebitWebhookInbox.Should().BeEmpty();
        context.AutomaticDebitEvents.Should().BeEmpty();
        context.AutomaticDebitOperations.Single().ProviderState.Should().Be(AutomaticDebitProviderStates.Issued);
    }

    [Fact]
    public async Task ProcessAsync_CrossJurisdictionProviderIdCannotMutateOperation()
    {
        await using var context = CreateContext();
        SeedOperation(context, jurisdictionId: 2420);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentReadBack());

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        result.StatusCode.Should().Be(404);
        context.AutomaticDebitWebhookInbox.Should().BeEmpty();
        context.AutomaticDebitEvents.Should().BeEmpty();
        context.AutomaticDebitOperations.Single().ProviderState.Should().Be(AutomaticDebitProviderStates.Issued);
    }

    [Fact]
    public async Task ProcessAsync_ApprovedPaymentAtomicallyPersistsInboxEventAndPendingInternalState()
    {
        await using var context = CreateContext();
        SeedOperation(context);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentReadBack());

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        result.StatusCode.Should().Be(200);
        result.Duplicate.Should().BeFalse();
        var operation = context.AutomaticDebitOperations.Should().ContainSingle().Subject;
        operation.ProviderState.Should().Be(AutomaticDebitProviderStates.Approved);
        operation.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Pending);
        var inbox = context.AutomaticDebitWebhookInbox.Should().ContainSingle().Subject;
        inbox.Result.Should().Be(AutomaticDebitInboxResults.Processed);
        inbox.PayloadHash.Should().HaveLength(64);
        inbox.AuthoritativeHash.Should().HaveLength(64);
        inbox.AuthoritativeUpdatedAt.Should().Be(DateTime.Parse("2026-09-01T10:00:00Z").ToUniversalTime());
        var providerEvent = context.AutomaticDebitEvents.Should().ContainSingle().Subject;
        providerEvent.OperationId.Should().Be(operation.Id);
        providerEvent.DeduplicationKey.Should().Be(inbox.DeduplicationKey);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateAuthoritativeStateAcknowledgesWithoutRepeatedTransition()
    {
        await using var context = CreateContext();
        SeedOperation(context);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentReadBack());

        var first = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint("{\"type\":\"debit\",\"id\":\"pay-1\"}"));
        var second = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint("{ \"type\": \"debit\", \"id\": \"pay-1\" }"));

        first.Duplicate.Should().BeFalse();
        second.StatusCode.Should().Be(200);
        second.Duplicate.Should().BeTrue();
        context.AutomaticDebitWebhookInbox.Should().ContainSingle();
        context.AutomaticDebitEvents.Should().ContainSingle();
        fixture.Client.Verify(
            x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessAsync_OverduePaymentPersistsTerminalStateAndAcknowledgesDuplicate()
    {
        await using var context = CreateContext();
        SeedOperation(context);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentReadBack() with { Status = "overdue" });

        var first = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());
        var second = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        first.StatusCode.Should().Be(200);
        first.Duplicate.Should().BeFalse();
        second.StatusCode.Should().Be(200);
        second.Duplicate.Should().BeTrue();
        var operation = context.AutomaticDebitOperations.Should().ContainSingle().Subject;
        operation.ProviderState.Should().Be(AutomaticDebitProviderStates.Overdue);
        operation.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Pending);
        operation.RejectedAt.Should().BeNull();
        context.AutomaticDebitWebhookInbox.Should().ContainSingle();
        context.AutomaticDebitEvents.Should().ContainSingle()
            .Which.ProviderState.Should().Be(AutomaticDebitProviderStates.Overdue);
    }

    [Fact]
    public async Task ProcessAsync_OutOfOrderPaymentDoesNotRegressInProcessToIssued()
    {
        await using var context = CreateContext();
        SeedOperation(context, providerState: AutomaticDebitProviderStates.InProcess);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentReadBack() with { Status = "issued" });

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        result.StatusCode.Should().Be(200);
        context.AutomaticDebitOperations.Single().ProviderState.Should().Be(AutomaticDebitProviderStates.InProcess);
    }

    [Theory]
    [InlineData(AutomaticDebitProviderStates.Approved, "rejected")]
    [InlineData(AutomaticDebitProviderStates.Rejected, "approved")]
    [InlineData(AutomaticDebitProviderStates.Cancelled, "in_process")]
    [InlineData(AutomaticDebitProviderStates.Overdue, "approved")]
    public async Task ProcessAsync_TerminalPaymentDoesNotChangeFromLaterReadBack(
        string currentState,
        string reportedState)
    {
        await using var context = CreateContext();
        SeedOperation(context, providerState: currentState);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaymentReadBack() with { Status = reportedState });

        await fixture.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        context.AutomaticDebitOperations.Single().ProviderState.Should().Be(currentState);
    }

    [Fact]
    public async Task ProcessAsync_OutOfOrderAdhesionDoesNotRegressActiveToPending()
    {
        await using var context = CreateContext();
        SeedAdhesion(context, providerState: AutomaticDebitProviderStates.Active);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetAdhesionReadBackAsync(JurisdictionId, "adh-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdhesionReadBack("pending"));

        await fixture.Sut.ProcessAsync(JurisdictionId, SubscriptionHint());

        context.AutomaticDebitAdhesions.Single().ProviderState.Should().Be(AutomaticDebitProviderStates.Active);
    }

    [Fact]
    public async Task ProcessAsync_RootAdhesionUsesSubscriptionReadBackAndPreservesNotificationType()
    {
        await using var context = CreateContext();
        SeedAdhesion(context);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetAdhesionReadBackAsync(JurisdictionId, "adh-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdhesionReadBack("active"));
        var raw = "{\"type\":\"adhesion\",\"id\":\"adh-1\",\"notifications\":[{\"type\":\"subscription\"}]}"u8.ToArray();

        var result = await fixture.Sut.ProcessAsync(JurisdictionId, raw);

        result.StatusCode.Should().Be(200);
        result.Duplicate.Should().BeFalse();
        context.AutomaticDebitAdhesions.Should().ContainSingle()
            .Which.ProviderState.Should().Be(AutomaticDebitProviderStates.Active);
        var inbox = context.AutomaticDebitWebhookInbox.Should().ContainSingle().Subject;
        inbox.ObjectType.Should().Be(AutomaticDebitWebhookObjectTypes.Subscription);
        inbox.Result.Should().Be(AutomaticDebitInboxResults.Processed);
        inbox.RawPayload.Should().Be(System.Text.Encoding.UTF8.GetString(raw));
        inbox.PayloadHash.Should().Be(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)).ToLowerInvariant());
        using var metadata = System.Text.Json.JsonDocument.Parse(inbox.SanitizedMetadata!);
        metadata.RootElement.GetProperty("notification_type").GetString().Should().Be("adhesion");
        var providerEvent = context.AutomaticDebitEvents.Should().ContainSingle().Subject;
        providerEvent.AdhesionId.Should().Be(1);
        providerEvent.OperationId.Should().BeNull();
        providerEvent.EventType.Should().Be(AutomaticDebitWebhookObjectTypes.Subscription);
        providerEvent.ProviderState.Should().Be(AutomaticDebitProviderStates.Active);
        fixture.Client.Verify(
            x => x.GetAdhesionReadBackAsync(JurisdictionId, "adh-1", It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Client.Verify(x => x.GetPaymentReadBackAsync(
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProcessAsync_CancelledAdhesionDoesNotReturnToActive()
    {
        await using var context = CreateContext();
        SeedAdhesion(context, providerState: AutomaticDebitProviderStates.Cancelled);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetAdhesionReadBackAsync(JurisdictionId, "adh-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdhesionReadBack("active"));

        await fixture.Sut.ProcessAsync(JurisdictionId, SubscriptionHint());

        context.AutomaticDebitAdhesions.Single().ProviderState.Should().Be(AutomaticDebitProviderStates.Cancelled);
    }

    [Fact]
    public async Task ProcessAsync_NoAuthoritativeTimestampUsesCanonicalReadBackHashForDeduplication()
    {
        await using var context = CreateContext();
        SeedAdhesion(context);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.GetAdhesionReadBackAsync(JurisdictionId, "adh-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderReadBack(
                "subscription", "adh-1", "active", "collector-2419", "VGBIN85447", null));

        var first = await fixture.Sut.ProcessAsync(JurisdictionId, SubscriptionHint());
        var second = await fixture.Sut.ProcessAsync(JurisdictionId, SubscriptionHint());

        first.StatusCode.Should().Be(200);
        second.Duplicate.Should().BeTrue();
        var inbox = context.AutomaticDebitWebhookInbox.Should().ContainSingle().Subject;
        inbox.AuthoritativeUpdatedAt.Should().BeNull();
        inbox.DeduplicationKey.Should().Be(inbox.AuthoritativeHash);
        context.AutomaticDebitAdhesions.Single().ProviderState.Should().Be(AutomaticDebitProviderStates.Active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessAsync_AfterGetOrReconcileWithSameReadBack_AcceptsNewInboxWithoutSecondEvent(
        bool reconcile)
    {
        var options = SharedOptions();
        await using var seed = new gtwContext(options);
        SeedOperation(seed);
        if (reconcile)
        {
            var operation = await seed.AutomaticDebitOperations.SingleAsync();
            operation.ProviderTransactionId = null;
            operation.ProviderState = AutomaticDebitProviderStates.Pending;
            await seed.SaveChangesAsync();
        }

        var readBack = FullPaymentReadBack();
        await using var generationContext = new gtwContext(options);
        var generation = CreateGenerationService(generationContext, readBack);
        var generationResult = reconcile
            ? await generation.Sut.ReconcileAsync(10)
            : await generation.Sut.GetAsync(10);
        generationResult.Code.Should().Be(200);
        generation.Client.Verify(x => x.CreateDebitAsync(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<AutomaticDebitProviderPaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        await using var webhookContext = new gtwContext(options);
        var webhook = CreateFixture(webhookContext);
        webhook.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(readBack);
        var first = await webhook.Sut.ProcessAsync(JurisdictionId, PaymentHint());
        var duplicate = await webhook.Sut.ProcessAsync(
            JurisdictionId, PaymentHint("{ \"type\": \"debit\", \"id\": \"pay-1\" }"));

        first.StatusCode.Should().Be(200);
        first.Duplicate.Should().BeFalse("the webhook has a new inbox even when its event exists");
        duplicate.StatusCode.Should().Be(200);
        duplicate.Duplicate.Should().BeTrue();
        await using var verification = new gtwContext(options);
        var recordedEvents = await verification.AutomaticDebitEvents.ToListAsync();
        recordedEvents.Should().ContainSingle($"keys: {string.Join(", ", recordedEvents.Select(x => x.DeduplicationKey))}");
        (await verification.AutomaticDebitWebhookInbox.CountAsync()).Should().Be(1);
        (await verification.AutomaticDebitOperations.SingleAsync()).ProviderState
            .Should().Be(AutomaticDebitProviderStates.Approved);
    }

    [Fact]
    public async Task ProcessAsync_AfterGetWithDifferentAuthoritativeStatus_CreatesDistinctEvents()
    {
        var options = SharedOptions();
        await using var seed = new gtwContext(options);
        SeedOperation(seed);
        await using var generationContext = new gtwContext(options);
        var generation = CreateGenerationService(generationContext,
            FullPaymentReadBack() with { Status = "in_process" });
        (await generation.Sut.GetAsync(10)).Code.Should().Be(200);

        await using var webhookContext = new gtwContext(options);
        var webhook = CreateFixture(webhookContext);
        webhook.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullPaymentReadBack());
        var result = await webhook.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        result.StatusCode.Should().Be(200);
        await using var verification = new gtwContext(options);
        var events = await verification.AutomaticDebitEvents.ToListAsync();
        events.Should().HaveCount(2);
        events.Select(x => x.DeduplicationKey).Should().OnlyHaveUniqueItems();
        (await verification.AutomaticDebitWebhookInbox.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(AutomaticDebitProviderStates.Approved, "issued")]
    [InlineData(AutomaticDebitProviderStates.InProcess, "issued")]
    [InlineData(AutomaticDebitProviderStates.Rejected, "approved")]
    public async Task ProcessAsync_ConcurrentStateWrite_DoesNotRegressOperationAndAcceptsInbox(
        string competingState, string staleReadBackState)
    {
        var options = SharedOptions();
        await using var seed = new gtwContext(options);
        SeedOperation(seed);
        await using var staleContext = new gtwContext(options);
        await using var competingContext = new gtwContext(options);
        var webhook = CreateFixture(staleContext);
        var enteredReadBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReadBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        webhook.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                enteredReadBack.SetResult();
                await releaseReadBack.Task;
                return FullPaymentReadBack() with { Status = staleReadBackState };
            });

        var pendingWebhook = webhook.Sut.ProcessAsync(JurisdictionId, PaymentHint());
        await enteredReadBack.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var competing = await competingContext.AutomaticDebitOperations.SingleAsync();
        competing.ProviderState = competingState;
        await competingContext.SaveChangesAsync();
        releaseReadBack.SetResult();

        var result = await pendingWebhook.WaitAsync(TimeSpan.FromSeconds(10));
        result.StatusCode.Should().Be(200);
        await using var verification = new gtwContext(options);
        (await verification.AutomaticDebitOperations.SingleAsync()).ProviderState.Should().Be(competingState);
        (await verification.AutomaticDebitWebhookInbox.CountAsync()).Should().Be(1);
        (await verification.AutomaticDebitEvents.CountAsync()).Should().Be(1);
        webhook.Client.Verify(x => x.GetPaymentReadBackAsync(
            JurisdictionId, "pay-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_UnrelatedDbUpdateException_IsNotTreatedAsDuplicate()
    {
        var options = SharedOptions();
        await using var seed = new gtwContext(options);
        SeedOperation(seed);
        await using var failingContext = new FailingSaveContext(options);
        var webhook = CreateFixture(failingContext);
        webhook.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullPaymentReadBack());

        Func<Task> act = () => webhook.Sut.ProcessAsync(JurisdictionId, PaymentHint());

        await act.Should().ThrowAsync<DbUpdateException>()
            .WithMessage("unrelated persistence failure");
        await using var verification = new gtwContext(options);
        (await verification.AutomaticDebitWebhookInbox.CountAsync()).Should().Be(0);
        (await verification.AutomaticDebitEvents.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessAsync_InboxUniqueCollision_RequiresMatchingInboxAndEvent(bool mismatchedInbox)
    {
        var options = SharedOptions();
        await using var seed = new gtwContext(options);
        SeedOperation(seed);
        await using var context = new InboxCollisionContext(options, mismatchedInbox);
        var webhook = CreateFixture(context);
        webhook.Client.Setup(x => x.GetPaymentReadBackAsync(
                JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullPaymentReadBack());

        if (mismatchedInbox)
        {
            Func<Task> act = () => webhook.Sut.ProcessAsync(JurisdictionId, PaymentHint());
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The existing automatic-debit inbox does not match the authoritative read-back.");
        }
        else
        {
            var result = await webhook.Sut.ProcessAsync(JurisdictionId, PaymentHint());
            result.StatusCode.Should().Be(200);
            result.Duplicate.Should().BeTrue();
        }

        context.InjectedCollisions.Should().Be(1);
        webhook.Client.Verify(x => x.GetPaymentReadBackAsync(
            JurisdictionId, "pay-1", It.IsAny<CancellationToken>()), Times.Once);
        await using var verification = new gtwContext(options);
        (await verification.AutomaticDebitWebhookInbox.CountAsync()).Should().Be(1);
        (await verification.AutomaticDebitEvents.CountAsync()).Should().Be(1);
    }

    private static (AutomaticDebitGenerationService Sut, Mock<IAutomaticDebitPayPerTicClient> Client)
        CreateGenerationService(gtwContext context, AutomaticDebitProviderReadBack readBack)
    {
        var client = new Mock<IAutomaticDebitPayPerTicClient>(MockBehavior.Strict);
        client.Setup(x => x.GetPaymentReadBackAsync(JurisdictionId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(readBack);
        client.Setup(x => x.GetAdhesionReadBackAsync(JurisdictionId, "adh-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdhesionReadBack("active"));
        client.Setup(x => x.FindDebitByExternalIdAsync(JurisdictionId, "ext-1",
                It.IsAny<DateTime>(), "collector-2419", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderPaymentMatch("pay-1", "ext-1", "collector-2419"));
        var operationIds = new Mock<IAutomaticDebitIdGenerator>();
        var webhookIds = new SequentialWebhookIdGenerator(nextEvent: 900);
        var sut = new AutomaticDebitGenerationService(
            context, Mock.Of<IAutomaticDebitObligationReader>(), client.Object, operationIds.Object,
            webhookIds, new AutomaticDebitOperationCreationLock(),
            Options.Create(new AutomaticDebitFeatureOptions { Enabled = true }),
            new FixedTimeProvider(Now), NullLogger<AutomaticDebitGenerationService>.Instance);
        return (sut, client);
    }

    private static AutomaticDebitProviderReadBack FullPaymentReadBack() => PaymentReadBack() with
    {
        PaymentType = "debit", PaymentCurrencyId = "ARS", DetailAmount = 11.14m,
        DetailExternalReference = "1038454109"
    };

    private static DbContextOptions<gtwContext> SharedOptions() =>
        new DbContextOptionsBuilder<gtwContext>()
            .UseInMemoryDatabase($"WebhookCrossPath_{Guid.NewGuid():N}", new InMemoryDatabaseRoot())
            .Options;

    private static WebhookFixture CreateFixture(gtwContext context, bool enabled = true)
    {
        var client = new Mock<IAutomaticDebitPayPerTicClient>(MockBehavior.Strict);
        var configuration = new Mock<IJurisdictionPayPerTicConfigurationProvider>(MockBehavior.Strict);
        configuration.Setup(x => x.GetActiveAsync(JurisdictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Configuration());
        var idGenerator = new SequentialWebhookIdGenerator();
        var sut = new AutomaticDebitWebhookProcessor(
            context,
            configuration.Object,
            client.Object,
            idGenerator,
            Options.Create(new AutomaticDebitFeatureOptions { Enabled = enabled }),
            new FixedTimeProvider(Now),
            NullLogger<AutomaticDebitWebhookProcessor>.Instance);
        return new WebhookFixture(sut, client);
    }

    private static gtwContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<gtwContext>()
            .UseInMemoryDatabase($"Webhook_{Guid.NewGuid():N}")
            .Options;
        return new gtwContext(options);
    }

    private static void SeedOperation(
        gtwContext context,
        long jurisdictionId = JurisdictionId,
        string providerState = AutomaticDebitProviderStates.Issued)
    {
        var adhesion = SeedAdhesion(context, jurisdictionId);
        context.AutomaticDebitOperations.Add(new AutomaticDebitOperation
        {
            Id = 10,
            AdhesionId = adhesion.Id,
            JurisdictionId = jurisdictionId,
            TaxpayerAccountId = "VGBIN85447",
            ObligationId = "1038454109",
            ProviderTransactionId = "pay-1",
            ExternalTransactionId = "ext-1",
            Amount = 11.14m,
            ProviderState = providerState,
            ProcessingState = AutomaticDebitProcessingStates.Pending,
            RequestedAt = Now.UtcDateTime,
            CreatedBy = "TEST",
            CreatedAt = Now.UtcDateTime
        });
        context.SaveChanges();
    }

    private static AutomaticDebitAdhesion SeedAdhesion(
        gtwContext context,
        long jurisdictionId = JurisdictionId,
        string providerState = AutomaticDebitProviderStates.Pending)
    {
        var adhesion = new AutomaticDebitAdhesion
        {
            Id = 1,
            JurisdictionId = jurisdictionId,
            TaxpayerAccountId = "VGBIN85447",
            PersonId = "MVGB318713",
            ProviderAdhesionId = "adh-1",
            ExternalReference = "VGBIN85447",
            ProviderState = providerState,
            ProcessingState = AutomaticDebitProcessingStates.Pending,
            RequestedAt = Now.UtcDateTime,
            CreatedBy = "TEST",
            CreatedAt = Now.UtcDateTime
        };
        context.AutomaticDebitAdhesions.Add(adhesion);
        context.SaveChanges();
        return adhesion;
    }

    private static AutomaticDebitProviderReadBack PaymentReadBack(
        string collectorId = "collector-2419",
        string externalReference = "ext-1") => new(
            "payment",
            "pay-1",
            "approved",
            collectorId,
            externalReference,
            DateTimeOffset.Parse("2026-09-01T10:00:00Z"));

    private static AutomaticDebitProviderReadBack AdhesionReadBack(string status) => new(
        "subscription",
        "adh-1",
        status,
        "collector-2419",
        "VGBIN85447",
        DateTimeOffset.Parse("2026-09-01T10:00:00Z"));

    private static byte[] PaymentHint(string json = "{\"type\":\"debit\",\"id\":\"pay-1\"}") =>
        System.Text.Encoding.UTF8.GetBytes(json);

    private static byte[] SubscriptionHint() =>
        "{\"type\":\"subscription\",\"id\":\"adh-1\"}"u8.ToArray();

    private static JurisdictionPayPerTicConfiguration Configuration() => new(
        JurisdictionId,
        "https://auth.example.test",
        "https://api.example.test",
        "user",
        "password",
        "client",
        "secret",
        "collector-2419",
        null,
        null,
        null);

    private sealed record WebhookFixture(
        AutomaticDebitWebhookProcessor Sut,
        Mock<IAutomaticDebitPayPerTicClient> Client);

    private sealed class SequentialWebhookIdGenerator(long nextEvent = 200) : IAutomaticDebitWebhookIdGenerator
    {
        private long _nextInbox = 100;
        private long _nextEvent = nextEvent;

        public Task<long> NextInboxIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_nextInbox++);

        public Task<long> NextEventIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_nextEvent++);
    }

    private sealed class FailingSaveContext(DbContextOptions<gtwContext> options) : gtwContext(options)
    {
        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("unrelated persistence failure", new InvalidOperationException("not a unique violation"));
    }

    private sealed class InboxCollisionContext : gtwContext
    {
        private readonly DbContextOptions<gtwContext> _options;
        private readonly bool _mismatchedInbox;

        public InboxCollisionContext(DbContextOptions<gtwContext> options, bool mismatchedInbox) : base(options)
        {
            _options = options;
            _mismatchedInbox = mismatchedInbox;
        }

        public int InjectedCollisions { get; private set; }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (InjectedCollisions != 0)
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            InjectedCollisions++;

            var inbox = (AutomaticDebitWebhookInbox)ChangeTracker.Entries<AutomaticDebitWebhookInbox>()
                .Single(x => x.State == EntityState.Added).CurrentValues.ToObject();
            var recordedEvent = (AutomaticDebitEvent)ChangeTracker.Entries<AutomaticDebitEvent>()
                .Single(x => x.State == EntityState.Added).CurrentValues.ToObject();
            if (_mismatchedInbox)
                inbox.ProviderObjectId = "different-payment";
            await using var competitor = new gtwContext(_options);
            competitor.AutomaticDebitWebhookInbox.Add(inbox);
            competitor.AutomaticDebitEvents.Add(recordedEvent);
            await competitor.SaveChangesAsync(cancellationToken);

            var constructor = typeof(Oracle.ManagedDataAccess.Client.OracleException).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, [typeof(int), typeof(string), typeof(string), typeof(string), typeof(int)], null)!;
            var oracle = (Oracle.ManagedDataAccess.Client.OracleException)constructor.Invoke(
                [1, "", "", "ORA-00001: unique constraint (UK_DEB_AUT_NOTIF_DEDUP) violated", 0]);
            throw new DbUpdateException("inbox unique collision", oracle);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

