namespace PagoTicAPI.Application.Services.Implementations;

internal static class AutomaticDebitProviderStateNormalizer
{
    public static string NormalizeAdhesionState(string providerState)
    {
        var normalized = providerState.Trim().ToUpperInvariant();
        return normalized switch
        {
            AutomaticDebitProviderStates.Pending => AutomaticDebitProviderStates.Pending,
            AutomaticDebitProviderStates.Active => AutomaticDebitProviderStates.Active,
            AutomaticDebitProviderStates.Cancelled => AutomaticDebitProviderStates.Cancelled,
            _ => throw new AutomaticDebitProviderException("PayPerTIC returned an unsupported adhesion state.")
        };
    }
}
