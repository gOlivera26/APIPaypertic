namespace PagoTicAPI.Application.Services.Interfaces;

/// <summary>
/// Genera identificadores locales para adhesiones y operaciones de débito automático.
/// </summary>
public interface IAutomaticDebitIdGenerator
{
    /// <summary>
    /// Genera el siguiente identificador de adhesión.
    /// </summary>
    Task<long> NextAdhesionIdAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Genera el siguiente identificador de operación.
    /// </summary>
    Task<long> NextOperationIdAsync(CancellationToken cancellationToken = default);
}
