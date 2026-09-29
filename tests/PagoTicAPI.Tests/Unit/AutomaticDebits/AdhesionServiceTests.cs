using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PagoTicAPI.Application.Clients.AutomaticDebits;
using PagoTicAPI.Application.Configuration.AutomaticDebits;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;
using PagoTicAPI.Application.Services.Implementations;
using PagoTicAPI.Domain.Context;
using PagoTicAPI.Domain.Models.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public class AdhesionServiceTests
{
    private const long JurisdictionId = 2419;
    private const string AccountId = "VGBIN85447";
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
    private static readonly InMemoryDatabaseRoot SharedDatabaseRoot = new();

    [Fact]
    public async Task CreateAsync_WhenFeatureIsDisabled_DoesNotReadDatabaseOrCallDependencies()
    {
        await using var context = CreateContext();
        var fixture = CreateFixture(context, enabled: false);

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        result.Code.Should().Be(404);
        context.ChangeTracker.Entries().Should().BeEmpty();
        fixture.Configuration.VerifyNoOtherCalls();
        fixture.AccountReader.VerifyNoOtherCalls();
        fixture.Client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WhenJurisdictionConfigurationIsMissing_DoesNotReadAccountOrCallProvider()
    {
        await using var context = CreateContext();
        var fixture = CreateFixture(context);
        fixture.Configuration
            .Setup(x => x.GetActiveAsync(JurisdictionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PayPerTicConfigurationNotFoundException(JurisdictionId));

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        result.Code.Should().Be(404);
        fixture.AccountReader.VerifyNoOtherCalls();
        fixture.Client.VerifyNoOtherCalls();
        (await context.AutomaticDebitAdhesions.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(AutomaticDebitProviderStates.Pending)]
    [InlineData(AutomaticDebitProviderStates.Active)]
    public async Task CreateAsync_WhenCurrentAdhesionExists_DoesNotCreateAnotherProviderAdhesion(string state)
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ExistingAdhesion(state));
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        result.Code.Should().Be(409);
        fixture.AccountReader.VerifyNoOtherCalls();
        fixture.Client.VerifyNoOtherCalls();
        (await context.AutomaticDebitAdhesions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_WhenValid_DerivesProviderPayloadFromReaderAndPersistsPendingAdhesion()
    {
        await using var context = CreateContext();
        var fixture = CreateFixture(context);
        AutomaticDebitProviderCreateRequest? captured = null;
        fixture.Client
            .Setup(x => x.CreateAdhesionAsync(
                JurisdictionId,
                It.IsAny<AutomaticDebitProviderCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<long, AutomaticDebitProviderCreateRequest, CancellationToken>((_, request, _) => captured = request)
            .ReturnsAsync(new AutomaticDebitProviderAdhesion("ppt-adh-1", "https://form.example/1", "pending"));

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        result.Code.Should().Be(200);
        result.Data.Should().NotBeNull();
        result.Data!.ProviderAdhesionId.Should().Be("ppt-adh-1");
        result.Data.FormUrl.Should().Be("https://form.example/1");
        result.Data.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        captured.Should().NotBeNull();
        captured!.ExternalReference.Should().Be(AccountId);
        captured.Payer.Should().BeEquivalentTo(new AutomaticDebitProviderPayer(
            "María Pérez", "maria@example.test", "MVGB318713", "ARG_DNI", "30111222", "ARG"));

        var persisted = await context.AutomaticDebitAdhesions.SingleAsync();
        persisted.Id.Should().Be(7001);
        persisted.JurisdictionId.Should().Be(JurisdictionId);
        persisted.TaxpayerAccountId.Should().Be(AccountId);
        persisted.PersonId.Should().Be("MVGB318713");
        persisted.ProviderAdhesionId.Should().Be("ppt-adh-1");
        persisted.FormUrl.Should().Be("https://form.example/1");
        persisted.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        persisted.RequestedAt.Should().Be(Now);
        (await context.AutomaticDebitOperations.CountAsync()).Should().Be(0);
        (await context.AutomaticDebitEvents.CountAsync()).Should().Be(0);
        (await context.AutomaticDebitWebhookInbox.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_IsScopedByJurisdictionAndAccount()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ExistingAdhesion(AutomaticDebitProviderStates.Active));
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);

        var wrongJurisdiction = await fixture.Sut.GetAsync(2389, AccountId);
        var wrongAccount = await fixture.Sut.GetAsync(JurisdictionId, "OTHER");
        var found = await fixture.Sut.GetAsync(JurisdictionId, AccountId);

        wrongJurisdiction.Code.Should().Be(404);
        wrongAccount.Code.Should().Be(404);
        found.Code.Should().Be(200);
        found.Data!.JurisdictionId.Should().Be(JurisdictionId);
        found.Data.TaxpayerAccountId.Should().Be(AccountId);
    }

    [Theory]
    [InlineData(AutomaticDebitProviderStates.Active, "pending")]
    [InlineData(AutomaticDebitProviderStates.Cancelled, "active")]
    public async Task GetAsync_DoesNotRegressAdhesionStateFromProviderReadBack(
        string currentState,
        string reportedState)
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ExistingAdhesion(currentState));
        await context.SaveChangesAsync();
        var client = new Mock<IAutomaticDebitPayPerTicClient>();
        client.Setup(x => x.GetAdhesionAsync(
                JurisdictionId,
                "ppt-existing",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderAdhesion(
                "ppt-existing",
                "https://form.example/existing",
                reportedState));
        var fixture = CreateFixture(context, client: client);

        var result = await fixture.Sut.GetAsync(JurisdictionId, AccountId);

        result.Code.Should().Be(200);
        result.Data!.ProviderState.Should().Be(currentState);
        context.AutomaticDebitAdhesions.Single().ProviderState.Should().Be(currentState);
    }

    [Fact]
    public async Task CancelAsync_CallsProviderAndPreservesCancelledHistory()
    {
        await using var context = CreateContext();
        context.AutomaticDebitAdhesions.Add(ExistingAdhesion(AutomaticDebitProviderStates.Active));
        await context.SaveChangesAsync();
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.CancelAdhesionAsync(
                JurisdictionId,
                "ppt-existing",
                "Requested by taxpayer",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderAdhesion("ppt-existing", null, "cancelled"));

        var result = await fixture.Sut.CancelAsync(
            JurisdictionId,
            AccountId,
            new CancelAutomaticDebitAdhesionRequest("Requested by taxpayer"));

        result.Code.Should().Be(200);
        var persisted = await context.AutomaticDebitAdhesions.SingleAsync();
        persisted.ProviderState.Should().Be(AutomaticDebitProviderStates.Cancelled);
        persisted.CancelledAt.Should().Be(Now);
        persisted.CancellationReason.Should().Be("Requested by taxpayer");
        persisted.DeactivatedBy.Should().Be("AUTOMATIC_DEBIT_API");
        persisted.DeactivatedAt.Should().Be(Now);
        persisted.ProviderAdhesionId.Should().Be("ppt-existing");
        (await context.AutomaticDebitAdhesions.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("internal_timeout")]
    [InlineData("http_request_exception")]
    [InlineData("http_5xx")]
    [InlineData("invalid_json")]
    [InlineData("missing_provider_id")]
    [InlineData("missing_provider_state")]
    public async Task CreateAsync_WhenProviderResultIsUncertain_KeepsPendingReservationAndReturnsSafeError(
        string failureCategory)
    {
        await using var context = CreateContext();
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.CreateAdhesionAsync(
                JurisdictionId,
                It.IsAny<AutomaticDebitProviderCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AutomaticDebitProviderException($"Provider request failed: {failureCategory}."));

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        result.Code.Should().Be(502);
        result.Message.Should().NotContain("secret");
        context.ChangeTracker.Clear();
        var reservation = await context.AutomaticDebitAdhesions.SingleAsync();
        reservation.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        reservation.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Pending);
        reservation.ProviderAdhesionId.Should().BeNull();
        fixture.Client.Verify(x => x.CreateAdhesionAsync(
            JurisdictionId,
            It.IsAny<AutomaticDebitProviderCreateRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_AfterUncertainResult_ReturnsConflictWithoutSecondProviderRequest()
    {
        await using var context = CreateContext();
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.CreateAdhesionAsync(
                JurisdictionId,
                It.IsAny<AutomaticDebitProviderCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AutomaticDebitProviderException("Provider request failed."));

        var firstResult = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));
        context.ChangeTracker.Clear();
        var reservationAfterFirstAttempt = await context.AutomaticDebitAdhesions.SingleAsync();

        var secondResult = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        firstResult.Code.Should().Be(502);
        reservationAfterFirstAttempt.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        reservationAfterFirstAttempt.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Pending);
        reservationAfterFirstAttempt.ProviderAdhesionId.Should().BeNull();
        secondResult.Code.Should().Be(409);
        secondResult.Message.Should().Be("A current automatic-debit adhesion already exists for this account.");
        (await context.AutomaticDebitAdhesions.CountAsync()).Should().Be(1);
        fixture.Client.Verify(x => x.CreateAdhesionAsync(
            JurisdictionId,
            It.IsAny<AutomaticDebitProviderCreateRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenCallerCancels_PropagatesCancellationAndKeepsPendingReservation()
    {
        await using var context = CreateContext();
        using var cancellation = new CancellationTokenSource();
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.CreateAdhesionAsync(
                JurisdictionId,
                It.IsAny<AutomaticDebitProviderCreateRequest>(),
                cancellation.Token))
            .Callback(() => cancellation.Cancel())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var action = () => fixture.Sut.CreateAsync(
            new CreateAutomaticDebitAdhesionRequest(JurisdictionId, AccountId),
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        context.ChangeTracker.Clear();
        var reservation = await context.AutomaticDebitAdhesions.SingleAsync();
        reservation.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        reservation.ProcessingState.Should().Be(AutomaticDebitProcessingStates.Pending);
        reservation.ProviderAdhesionId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_WhenProviderCreatesButReturnsUnsupportedState_KeepsReservationForReconciliation()
    {
        await using var context = CreateContext();
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.CreateAdhesionAsync(
                JurisdictionId,
                It.IsAny<AutomaticDebitProviderCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderAdhesion("ppt-created", null, "unexpected"));

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        result.Code.Should().Be(502);
        var reservation = await context.AutomaticDebitAdhesions.SingleAsync();
        reservation.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        reservation.ProviderAdhesionId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_AcrossServiceInstances_SerializesSameAccountBeforeProviderCall()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        await using var firstContext = CreateContext(databaseName, root);
        await using var secondContext = CreateContext(databaseName, root);
        var firstCoordinator = new AutomaticDebitAdhesionCreationLock();
        var secondCoordinator = new AutomaticDebitAdhesionCreationLock();
        var client = new Mock<IAutomaticDebitPayPerTicClient>();
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Setup(x => x.CreateAdhesionAsync(
                JurisdictionId,
                It.IsAny<AutomaticDebitProviderCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                providerEntered.TrySetResult();
                await releaseProvider.Task;
                return new AutomaticDebitProviderAdhesion("ppt-race", "https://form.example/race", "pending");
            });
        var first = CreateFixture(firstContext, client: client, creationLock: firstCoordinator);
        var second = CreateFixture(secondContext, client: client, creationLock: secondCoordinator);

        var firstCall = first.Sut.CreateAsync(new(JurisdictionId, AccountId));
        await providerEntered.Task;
        var secondCall = second.Sut.CreateAsync(new(JurisdictionId, AccountId));
        var completedBeforeRelease = await Task.WhenAny(secondCall, Task.Delay(TimeSpan.FromMilliseconds(250)));

        completedBeforeRelease.Should().NotBe(secondCall,
            "distinct service and lock instances must synchronize process-wide before reading the reservation");
        releaseProvider.TrySetResult();
        var results = await Task.WhenAll(firstCall, secondCall);

        results.Count(x => x.Code == 200).Should().Be(1);
        results.Count(x => x.Code == 409).Should().Be(1);
        client.Verify(x => x.CreateAdhesionAsync(
            JurisdictionId,
            It.IsAny<AutomaticDebitProviderCreateRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreationLock_DifferentAccountKeysRemainIndependentAcrossInstances()
    {
        var firstCoordinator = new AutomaticDebitAdhesionCreationLock();
        var secondCoordinator = new AutomaticDebitAdhesionCreationLock();
        await using var firstLease = await firstCoordinator.AcquireAsync(JurisdictionId, AccountId);

        var independentLeaseTask = secondCoordinator
            .AcquireAsync(JurisdictionId, "VGBIN85448")
            .AsTask();

        var completed = await Task.WhenAny(independentLeaseTask, Task.Delay(TimeSpan.FromSeconds(1)));
        completed.Should().Be(independentLeaseTask);
        await using var independentLease = await independentLeaseTask;
    }

    [Fact]
    public async Task CreationLock_CancelledWaiterIsCleanedAndDoesNotPoisonTheKey()
    {
        var firstCoordinator = new AutomaticDebitAdhesionCreationLock();
        var secondCoordinator = new AutomaticDebitAdhesionCreationLock();
        await using var firstLease = await firstCoordinator.AcquireAsync(JurisdictionId, AccountId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelledAcquire = () => secondCoordinator
            .AcquireAsync(JurisdictionId, AccountId, cancellation.Token)
            .AsTask();

        await cancelledAcquire.Should().ThrowAsync<OperationCanceledException>();
        await firstLease.DisposeAsync();
        var nextLeaseTask = secondCoordinator.AcquireAsync(JurisdictionId, AccountId).AsTask();
        var completed = await Task.WhenAny(nextLeaseTask, Task.Delay(TimeSpan.FromSeconds(1)));
        completed.Should().Be(nextLeaseTask);
        await using var nextLease = await nextLeaseTask;
    }

    [Fact]
    public async Task CreateAsync_WhenReservationUniqueRaceHasCurrentAdhesion_ReturnsConflictWithoutProviderCall()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        var options = CreateOptions(databaseName, root);
        await using var context = new FailingSaveGtwContext(
            options,
            failOnSaveNumber: 1,
            async cancellationToken =>
            {
                await using var competingContext = new gtwContext(options);
                var competing = ExistingAdhesion(AutomaticDebitProviderStates.Pending);
                competing.Id = 9001;
                competingContext.AutomaticDebitAdhesions.Add(competing);
                await competingContext.SaveChangesAsync(cancellationToken);
            });
        var fixture = CreateFixture(context);

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        result.Code.Should().Be(409);
        fixture.Client.Verify(x => x.CreateAdhesionAsync(
            It.IsAny<long>(),
            It.IsAny<AutomaticDebitProviderCreateRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
        await using var verificationContext = new gtwContext(options);
        (await verificationContext.AutomaticDebitAdhesions.SingleAsync()).Id.Should().Be(9001);
    }

    [Fact]
    public async Task CreateAsync_WhenReservationSaveFailsWithoutCurrentAdhesion_PropagatesDatabaseFailureBeforeProviderCall()
    {
        var options = CreateOptions(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot());
        await using var context = new FailingSaveGtwContext(options, failOnSaveNumber: 1);
        var fixture = CreateFixture(context);

        var action = () => fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        await action.Should().ThrowAsync<DbUpdateException>();
        fixture.Client.Verify(x => x.CreateAdhesionAsync(
            It.IsAny<long>(),
            It.IsAny<AutomaticDebitProviderCreateRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WhenProviderWasCreatedButLocalCompletionSaveFails_KeepsReservationForReconciliation()
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        var options = CreateOptions(databaseName, root);
        await using var context = new FailingSaveGtwContext(options, failOnSaveNumber: 2);
        var fixture = CreateFixture(context);
        fixture.Client
            .Setup(x => x.CreateAdhesionAsync(
                JurisdictionId,
                It.IsAny<AutomaticDebitProviderCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutomaticDebitProviderAdhesion(
                "ppt-created-before-local-failure",
                "https://form.example/local-failure",
                "pending"));

        var result = await fixture.Sut.CreateAsync(new(JurisdictionId, AccountId));

        result.Code.Should().Be(502);
        result.Message.Should().Contain("reconciliation");
        fixture.Client.Verify(x => x.CreateAdhesionAsync(
            JurisdictionId,
            It.IsAny<AutomaticDebitProviderCreateRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        await using var verificationContext = new gtwContext(options);
        var reservation = await verificationContext.AutomaticDebitAdhesions.SingleAsync();
        reservation.ProviderState.Should().Be(AutomaticDebitProviderStates.Pending);
        reservation.ProviderAdhesionId.Should().BeNull();
        reservation.FormUrl.Should().BeNull();
    }

    private static Fixture CreateFixture(
        gtwContext context,
        bool enabled = true,
        Mock<IAutomaticDebitPayPerTicClient>? client = null,
        IAutomaticDebitAdhesionCreationLock? creationLock = null)
    {
        var configuration = new Mock<IJurisdictionPayPerTicConfigurationProvider>();
        configuration.Setup(x => x.GetActiveAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Configuration());
        var reader = new Mock<ITributaryAccountReader>();
        reader.Setup(x => x.GetByAccountAsync(JurisdictionId, AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Account());
        if (client is null)
        {
            client = new Mock<IAutomaticDebitPayPerTicClient>();
            client.Setup(x => x.CreateAdhesionAsync(
                    It.IsAny<long>(),
                    It.IsAny<AutomaticDebitProviderCreateRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AutomaticDebitProviderAdhesion("ppt-default", "https://form.example/default", "pending"));
            client.Setup(x => x.GetAdhesionAsync(
                    It.IsAny<long>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((long _, string providerId, CancellationToken _) =>
                    new AutomaticDebitProviderAdhesion(providerId, "https://form.example/default", "active"));
        }
        var idGenerator = new Mock<IAutomaticDebitIdGenerator>();
        idGenerator.Setup(x => x.NextAdhesionIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7001);
        var sut = new AutomaticDebitAdhesionService(
            context,
            configuration.Object,
            reader.Object,
            client.Object,
            idGenerator.Object,
            creationLock ?? new AutomaticDebitAdhesionCreationLock(),
            Options.Create(new AutomaticDebitFeatureOptions { Enabled = enabled }),
            new FrozenTimeProvider(Now),
            Mock.Of<ILogger<AutomaticDebitAdhesionService>>());
        return new Fixture(sut, configuration, reader, client);
    }

    private static gtwContext CreateContext() =>
        CreateContext(Guid.NewGuid().ToString(), SharedDatabaseRoot);

    private static gtwContext CreateContext(string databaseName, InMemoryDatabaseRoot root)
    {
        return new gtwContext(CreateOptions(databaseName, root));
    }

    private static DbContextOptions<gtwContext> CreateOptions(
        string databaseName,
        InMemoryDatabaseRoot root) =>
        new DbContextOptionsBuilder<gtwContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;

    private static JurisdictionPayPerTicConfiguration Configuration() => new(
        JurisdictionId,
        "https://auth.example.test",
        "https://api.example.test",
        "user",
        "password",
        "client",
        "secret",
        "collector",
        "https://api.example.test/webhook",
        "https://portal.example.test/return",
        "https://portal.example.test/back");

    private static TributaryAccountReadModel Account() => new(
        JurisdictionId,
        AccountId,
        "MVGB318713",
        "6",
        "Inmueble",
        "00005199",
        "María Pérez",
        "maria@example.test",
        "ARG_DNI",
        "30111222",
        "ARG");

    private static AutomaticDebitAdhesion ExistingAdhesion(string state) => new()
    {
        Id = 1,
        JurisdictionId = JurisdictionId,
        TaxpayerAccountId = AccountId,
        PersonId = "MVGB318713",
        ProviderAdhesionId = "ppt-existing",
        ExternalReference = AccountId,
        ProviderState = state,
        ProcessingState = AutomaticDebitProcessingStates.Pending,
        RequestedAt = Now.AddDays(-1),
        CreatedBy = "test",
        CreatedAt = Now.AddDays(-1)
    };

    private sealed record Fixture(
        AutomaticDebitAdhesionService Sut,
        Mock<IJurisdictionPayPerTicConfigurationProvider> Configuration,
        Mock<ITributaryAccountReader> AccountReader,
        Mock<IAutomaticDebitPayPerTicClient> Client);

    private sealed class FrozenTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class FailingSaveGtwContext(
        DbContextOptions<gtwContext> options,
        int failOnSaveNumber,
        Func<CancellationToken, Task>? beforeFailure = null) : gtwContext(options)
    {
        private int _saveCount;

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCount++;
            if (_saveCount == failOnSaveNumber)
            {
                if (beforeFailure is not null)
                {
                    await beforeFailure(cancellationToken);
                }

                throw new DbUpdateException("Simulated persistence failure.");
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}

