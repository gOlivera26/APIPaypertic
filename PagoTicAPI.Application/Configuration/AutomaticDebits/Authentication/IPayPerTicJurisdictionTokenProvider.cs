namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

/// <summary>
/// Proporciona tokens de PayPerTIC aislados por jurisdicción.
/// </summary>
public interface IPayPerTicJurisdictionTokenProvider
{
    /// <summary>
    /// Obtiene un token válido para la jurisdicción.
    /// </summary>
    Task<string> GetTokenAsync(long jurisdictionId, CancellationToken cancellationToken = default);
}
