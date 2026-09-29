namespace PagoTicAPI.API.AutomaticDebits.Scheduling;

/// <summary>
/// Coordina la exclusión distribuida de ciclos del planificador.
/// </summary>
public interface IAutomaticDebitSchedulerLease
{
    /// <summary>
    /// Intenta adquirir el lease distribuido para ejecutar un único ciclo del planificador.
    /// </summary>
    Task<bool> TryAcquireAsync(
        string leaseName,
        string owner,
        TimeSpan duration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Libera el lease distribuido que pertenece a la instancia indicada.
    /// </summary>
    Task ReleaseAsync(
        string leaseName,
        string owner,
        CancellationToken cancellationToken);
}
