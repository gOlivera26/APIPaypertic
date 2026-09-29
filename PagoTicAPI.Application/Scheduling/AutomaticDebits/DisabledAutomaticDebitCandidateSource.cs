namespace PagoTicAPI.Application.Scheduling.AutomaticDebits;

public sealed class DisabledAutomaticDebitCandidateSource : IAutomaticDebitCandidateSource
{
    public Task<IReadOnlyCollection<AutomaticDebitCandidate>> GetCandidatesAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<AutomaticDebitCandidate>>([]);
}
