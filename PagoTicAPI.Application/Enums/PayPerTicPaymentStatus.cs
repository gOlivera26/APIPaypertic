namespace PagoTicAPI.Application.Enums;

public enum PayPerTicPaymentStatus
{
    Pending,
    Issued,
    InProcess,
    Approved,
    Rejected,
    Error,
    Cancelled,
    Refunded,
    ChargedBack,
    InMediation
}
