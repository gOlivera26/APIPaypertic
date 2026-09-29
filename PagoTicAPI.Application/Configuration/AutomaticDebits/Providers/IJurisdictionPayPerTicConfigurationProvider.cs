namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

/// <summary>
/// Resuelve la configuración activa de PayPerTIC para una jurisdicción.
/// </summary>
public interface IJurisdictionPayPerTicConfigurationProvider
{
    /// <summary>
    /// Obtiene la configuración activa de PayPerTIC para la jurisdicción indicada.
    /// </summary>
    Task<JurisdictionPayPerTicConfiguration> GetActiveAsync(
        long jurisdictionId,
        CancellationToken cancellationToken = default);
}
