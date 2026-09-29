namespace PagoTicAPI.Domain.Models.AutomaticDebits;

public static class AutomaticDebitProviderStates
{
    public const string Pending = "PENDING";
    public const string Active = "ACTIVE";
    public const string Cancelled = "CANCELLED";
    public const string Issued = "ISSUED";
    public const string InProcess = "IN_PROCESS";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Overdue = "OVERDUE";
}

public enum AutomaticDebitProcessingStates
{
    Pending,
    Imputed,
    Failed
}

public enum AutomaticDebitInboxResults
{
    Received,
    Processed,
    Rejected
}
