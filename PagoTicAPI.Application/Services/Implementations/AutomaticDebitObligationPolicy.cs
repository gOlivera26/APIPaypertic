namespace PagoTicAPI.Application.Services.Implementations;

public static class AutomaticDebitObligationPolicy
{
    public static AutomaticDebitObligationEligibility Evaluate(AutomaticDebitObligationSnapshot obligation)
    {
        if (obligation.IsDeactivated) return Reject("obligation_deactivated");
        if (!string.Equals(obligation.DebtState, "PP", StringComparison.OrdinalIgnoreCase)) return Reject("invalid_debt_state");
        if (!string.Equals(obligation.DebtSituation, "DN", StringComparison.OrdinalIgnoreCase)) return Reject("invalid_debt_situation");
        if (string.Equals(obligation.InstallmentNumber.Trim(), "000", StringComparison.Ordinal)) return Reject("installment_000_excluded");
        if (obligation.PartiallyPaidAmount > 0) return Reject("partial_payment_excluded");
        if (obligation.Amount <= 0) return Reject("non_positive_amount");
        return new AutomaticDebitObligationEligibility(true, null);
    }

    private static AutomaticDebitObligationEligibility Reject(string reason) => new(false, reason);
}

public sealed record AutomaticDebitObligationEligibility(bool IsEligible, string? RejectionReason);
