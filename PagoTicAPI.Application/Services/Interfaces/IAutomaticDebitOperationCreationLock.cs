namespace PagoTicAPI.Application.Services.Interfaces;

/// <summary>
/// Serializa la creación de operaciones para una misma obligación dentro del proceso.
/// </summary>
public interface IAutomaticDebitOperationCreationLock
{
    /// <summary>
    /// Adquiere la exclusión local para emitir un débito sobre la obligación indicada.
    /// </summary>
    ValueTask<IAsyncDisposable> AcquireAsync(
        long jurisdictionId,
        string obligationId,
        CancellationToken cancellationToken = default);
}
