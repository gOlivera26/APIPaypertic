namespace PagoTicAPI.Application.Scheduling.AutomaticDebits;

/// <summary>
/// Coordina un ciclo de generación programada de débitos automáticos.
/// </summary>
public interface IAutomaticDebitSchedulerOrchestrator
{
    /// <summary>
    /// Ejecuta un ciclo completo del planificador.
    /// </summary>
    Task ExecuteAsync(CancellationToken cancellationToken);
}
