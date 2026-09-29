using PagoTicAPI.Application.Configuration.AutomaticDebits;
using Microsoft.Extensions.Options;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;
using PagoTicAPI.Application.Scheduling.AutomaticDebits;

namespace PagoTicAPI.API.AutomaticDebits.Scheduling;

public class AutomaticDebitSchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AutomaticDebitFeatureOptions _featureOptions;
    private readonly AutomaticDebitSchedulerOptions _options;
    private readonly ILogger<AutomaticDebitSchedulerHostedService> _logger;
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public AutomaticDebitSchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<AutomaticDebitFeatureOptions> featureOptions,
        IOptions<AutomaticDebitSchedulerOptions> options,
        ILogger<AutomaticDebitSchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _featureOptions = featureOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_featureOptions.Enabled || !_options.Enabled)
        {
            _logger.LogInformation(
                "El planificador de débitos automáticos está deshabilitado. FeatureEnabled={FeatureEnabled}, SchedulerEnabled={SchedulerEnabled}.",
                _featureOptions.Enabled, _options.Enabled);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycleSafelyAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
        }
    }

    protected internal async Task RunCycleSafelyAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var lease = scope.ServiceProvider.GetRequiredService<IAutomaticDebitSchedulerLease>();
        var acquired = false;

        try
        {
            acquired = await lease.TryAcquireAsync(
                _options.LeaseName,
                _owner,
                TimeSpan.FromSeconds(_options.LeaseSeconds),
                cancellationToken);
            if (!acquired)
            {
                _logger.LogDebug("Se omitió el ciclo del planificador de débitos automáticos porque otra instancia posee el lease.");
                return;
            }

            var orchestrator = scope.ServiceProvider.GetRequiredService<IAutomaticDebitSchedulerOrchestrator>();
            using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            executionCancellation.CancelAfter(TimeSpan.FromSeconds(_options.ExecutionTimeoutSeconds));
            await orchestrator.ExecuteAsync(executionCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falló el ciclo del planificador de débitos automáticos.");
        }
        finally
        {
            if (acquired)
            {
                try
                {
                    await lease.ReleaseAsync(_options.LeaseName, _owner, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Falló la liberación del lease del planificador de débitos automáticos; su vencimiento permitirá recuperarlo.");
                }
            }
        }
    }
}
