using PagoTicAPI.Application.Configuration.AutomaticDebits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PagoTicAPI.API.AutomaticDebits.Scheduling;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;
using PagoTicAPI.Application.Scheduling.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public sealed class AutomaticDebitSchedulerTests
{
    [Fact]
    public async Task DisabledScheduler_DoesNotCreateScopeOrAcquireLease()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var sut = CreateService(scopeFactory.Object, featureEnabled: true, schedulerEnabled: false);

        await sut.StartAsync(CancellationToken.None);

        scopeFactory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DisabledFeature_DoesNotCreateScopeOrAcquireLease()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var sut = CreateService(scopeFactory.Object, featureEnabled: false, schedulerEnabled: true);

        await sut.StartAsync(CancellationToken.None);

        scopeFactory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cycle_AcquiresLeaseExecutesScopedOrchestratorAndReleasesLease()
    {
        var lease = new LeaseDouble(acquire: true);
        var orchestrator = new OrchestratorDouble();
        var sut = CreateCycleService(lease, orchestrator);

        await sut.RunOneCycleAsync(CancellationToken.None);

        orchestrator.Executions.Should().Be(1);
        lease.Acquisitions.Should().Be(1);
        lease.Releases.Should().Be(1);
    }

    [Fact]
    public async Task Cycle_WhenLeaseBelongsToAnotherInstance_DoesNotExecute()
    {
        var lease = new LeaseDouble(acquire: false);
        var orchestrator = new OrchestratorDouble();
        var sut = CreateCycleService(lease, orchestrator);

        await sut.RunOneCycleAsync(CancellationToken.None);

        orchestrator.Executions.Should().Be(0);
        lease.Releases.Should().Be(0);
    }

    [Fact]
    public async Task Cycle_WhenOrchestratorFails_ReleasesLeaseAndDoesNotEscapeLoop()
    {
        var lease = new LeaseDouble(acquire: true);
        var orchestrator = new OrchestratorDouble(new InvalidOperationException("test failure"));
        var sut = CreateCycleService(lease, orchestrator);

        var action = () => sut.RunOneCycleAsync(CancellationToken.None);

        await action.Should().NotThrowAsync();
        lease.Releases.Should().Be(1);
    }

    [Fact]
    public async Task Cycle_PropagatesCancellationAndStillReleasesLease()
    {
        using var cancellation = new CancellationTokenSource();
        var lease = new LeaseDouble(acquire: true);
        var orchestrator = new OrchestratorDouble(blockUntilCancelled: true);
        var sut = CreateCycleService(lease, orchestrator);
        var execution = sut.RunOneCycleAsync(cancellation.Token);
        await orchestrator.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();

        await FluentActions.Awaiting(() => execution).Should().ThrowAsync<OperationCanceledException>();
        lease.Releases.Should().Be(1);
    }

    [Fact]
    public async Task CurrentCandidateSourceAndOrchestrator_AreFailClosed()
    {
        var source = new DisabledAutomaticDebitCandidateSource();
        var orchestrator = new AutomaticDebitSchedulerOrchestrator(
            source, NullLogger<AutomaticDebitSchedulerOrchestrator>.Instance);

        var candidates = await source.GetCandidatesAsync(CancellationToken.None);
        await orchestrator.ExecuteAsync(CancellationToken.None);

        candidates.Should().BeEmpty();
    }

    private static TestScheduler CreateCycleService(
        IAutomaticDebitSchedulerLease lease,
        IAutomaticDebitSchedulerOrchestrator orchestrator)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => lease);
        services.AddScoped(_ => orchestrator);
        return CreateService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), true, true);
    }

    private static TestScheduler CreateService(
        IServiceScopeFactory scopeFactory,
        bool featureEnabled,
        bool schedulerEnabled) => new(
            scopeFactory,
            Options.Create(new AutomaticDebitFeatureOptions { Enabled = featureEnabled }),
            Options.Create(new AutomaticDebitSchedulerOptions
            {
                Enabled = schedulerEnabled,
                IntervalSeconds = 10,
                ExecutionTimeoutSeconds = 10,
                LeaseSeconds = 20
            }));

    private sealed class TestScheduler : AutomaticDebitSchedulerHostedService
    {
        public TestScheduler(
            IServiceScopeFactory scopeFactory,
            IOptions<AutomaticDebitFeatureOptions> featureOptions,
            IOptions<AutomaticDebitSchedulerOptions> schedulerOptions)
            : base(scopeFactory, featureOptions, schedulerOptions,
                NullLogger<AutomaticDebitSchedulerHostedService>.Instance)
        {
        }

        public Task RunOneCycleAsync(CancellationToken cancellationToken) =>
            RunCycleSafelyAsync(cancellationToken);
    }

    private sealed class LeaseDouble : IAutomaticDebitSchedulerLease
    {
        private readonly bool _acquire;
        public int Acquisitions { get; private set; }
        public int Releases { get; private set; }

        public LeaseDouble(bool acquire) => _acquire = acquire;

        public Task<bool> TryAcquireAsync(string leaseName, string owner, TimeSpan duration,
            CancellationToken cancellationToken)
        {
            Acquisitions++;
            return Task.FromResult(_acquire);
        }

        public Task ReleaseAsync(string leaseName, string owner, CancellationToken cancellationToken)
        {
            Releases++;
            return Task.CompletedTask;
        }
    }

    private sealed class OrchestratorDouble : IAutomaticDebitSchedulerOrchestrator
    {
        private readonly Exception? _exception;
        private readonly bool _blockUntilCancelled;
        public int Executions { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OrchestratorDouble(Exception? exception = null, bool blockUntilCancelled = false)
        {
            _exception = exception;
            _blockUntilCancelled = blockUntilCancelled;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Executions++;
            Started.TrySetResult();
            if (_exception is not null)
            {
                throw _exception;
            }

            if (_blockUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }
    }
}
