namespace PagoTicAPI.Application.Scheduling.AutomaticDebits;

public sealed class AutomaticDebitSchedulerOrchestrator : IAutomaticDebitSchedulerOrchestrator
{
    private readonly IAutomaticDebitCandidateSource _candidateSource;
    private readonly ILogger<AutomaticDebitSchedulerOrchestrator> _logger;

    public AutomaticDebitSchedulerOrchestrator(
        IAutomaticDebitCandidateSource candidateSource,
        ILogger<AutomaticDebitSchedulerOrchestrator> logger)
    {
        _candidateSource = candidateSource;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var candidates = await _candidateSource.GetCandidatesAsync(cancellationToken);
        if (candidates.Count != 0)
        {
            throw new InvalidOperationException(
                "El procesamiento de candidatos para débito automático no está configurado.");
        }

        _logger.LogDebug(
            "El planificador de débitos automáticos finalizó correctamente sin candidatos tributarios.");
    }
}
