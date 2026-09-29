namespace PagoTicAPI.Application.Scheduling.AutomaticDebits;

/// <summary>
/// Proporciona las obligaciones candidatas para la ejecución programada de débitos automáticos.
/// </summary>
public interface IAutomaticDebitCandidateSource
{
    /// <summary>Obtiene las obligaciones candidatas para el próximo ciclo del planificador.</summary>
    Task<IReadOnlyCollection<AutomaticDebitCandidate>> GetCandidatesAsync(
        CancellationToken cancellationToken);
}
