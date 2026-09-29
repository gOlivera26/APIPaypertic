namespace PagoTicAPI.Application.Services.Interfaces;

/// <summary>
/// Consulta los datos tributarios y del pagador asociados a una cuenta.
/// </summary>
public interface ITributaryAccountReader
{
    /// <summary>
    /// Consulta una cuenta tributaria y los datos de su pagador.
    /// </summary>
    Task<TributaryAccountReadModel?> GetByAccountAsync(
        long jurisdictionId,
        string taxpayerAccountId,
        CancellationToken cancellationToken = default);
}
