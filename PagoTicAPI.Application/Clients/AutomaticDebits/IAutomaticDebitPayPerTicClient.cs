namespace PagoTicAPI.Application.Clients.AutomaticDebits;

/// <summary>
/// Define las operaciones disponibles contra PayPerTIC para adhesiones y débitos automáticos.
/// </summary>
public interface IAutomaticDebitPayPerTicClient
{
    /// <summary>
    /// Crea una adhesión en PayPerTIC.
    /// </summary>
    Task<AutomaticDebitProviderAdhesion> CreateAdhesionAsync(
        long jurisdictionId,
        AutomaticDebitProviderCreateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta una adhesión en PayPerTIC.
    /// </summary>
    Task<AutomaticDebitProviderAdhesion> GetAdhesionAsync(
        long jurisdictionId,
        string providerAdhesionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela una adhesión en PayPerTIC.
    /// </summary>
    Task<AutomaticDebitProviderAdhesion> CancelAdhesionAsync(
        long jurisdictionId,
        string providerAdhesionId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emite un débito mediante una adhesión activa.
    /// </summary>
    Task<AutomaticDebitProviderPayment> CreateDebitAsync(
        long jurisdictionId,
        string providerAdhesionId,
        AutomaticDebitProviderPaymentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a payment without creating or changing provider state; null means no match.</summary>
    Task<AutomaticDebitProviderPaymentMatch?> FindDebitByExternalIdAsync(
        long jurisdictionId,
        string externalTransactionId,
        DateTime requestedAt,
        string collectorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el estado autoritativo de un débito.
    /// </summary>
    Task<AutomaticDebitProviderReadBack> GetPaymentReadBackAsync(
        long jurisdictionId,
        string providerPaymentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela un débito pendiente en PayPerTIC.
    /// </summary>
    Task CancelPaymentAsync(
        long jurisdictionId,
        string providerPaymentId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el estado autoritativo de una adhesión.
    /// </summary>
    Task<AutomaticDebitProviderReadBack> GetAdhesionReadBackAsync(
        long jurisdictionId,
        string providerAdhesionId,
        CancellationToken cancellationToken = default);
}
