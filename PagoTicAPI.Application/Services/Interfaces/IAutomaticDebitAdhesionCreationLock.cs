namespace PagoTicAPI.Application.Services.Interfaces;

/// <summary>
/// Serializa la creación de adhesiones para una misma cuenta tributaria dentro del proceso.
/// </summary>
public interface IAutomaticDebitAdhesionCreationLock
{
    /// <summary>
    /// Adquiere la exclusión local para crear una adhesión de la cuenta indicada.
    /// </summary>
    ValueTask<IAsyncDisposable> AcquireAsync(
        long jurisdictionId,
        string taxpayerAccountId,
        CancellationToken cancellationToken = default);
}
