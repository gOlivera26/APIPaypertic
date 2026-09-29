namespace PagoTicAPI.Application.Services.Implementations;

internal static class AutomaticDebitProviderStateTransitionPolicy
{
    public static bool CanApplyPayment(string currentState, string reportedState)
    {
        if (string.Equals(currentState, reportedState, StringComparison.Ordinal) ||
            IsTerminalPayment(currentState))
        {
            return false;
        }

        return PaymentRank(reportedState) > PaymentRank(currentState);
    }

    public static bool CanApplyAdhesion(string currentState, string reportedState)
    {
        if (string.Equals(currentState, reportedState, StringComparison.Ordinal) ||
            currentState == AutomaticDebitProviderStates.Cancelled)
        {
            return false;
        }

        return AdhesionRank(reportedState) > AdhesionRank(currentState);
    }

    private static bool IsTerminalPayment(string state) =>
        state is AutomaticDebitProviderStates.Approved or
            AutomaticDebitProviderStates.Rejected or
            AutomaticDebitProviderStates.Cancelled or
            AutomaticDebitProviderStates.Overdue;

    private static int PaymentRank(string state) => state switch
    {
        AutomaticDebitProviderStates.Pending => 0,
        AutomaticDebitProviderStates.Issued => 1,
        AutomaticDebitProviderStates.InProcess => 2,
        AutomaticDebitProviderStates.Approved or
            AutomaticDebitProviderStates.Rejected or
            AutomaticDebitProviderStates.Cancelled or
            AutomaticDebitProviderStates.Overdue => 3,
        _ => -1
    };

    private static int AdhesionRank(string state) => state switch
    {
        AutomaticDebitProviderStates.Pending => 0,
        AutomaticDebitProviderStates.Active => 1,
        AutomaticDebitProviderStates.Cancelled => 2,
        _ => -1
    };
}
