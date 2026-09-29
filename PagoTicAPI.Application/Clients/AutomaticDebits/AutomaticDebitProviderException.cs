namespace PagoTicAPI.Application.Clients.AutomaticDebits;

public sealed class AutomaticDebitProviderException : Exception
{
    public AutomaticDebitProviderException(string message, int? statusCode = null, Exception? innerException = null, AutomaticDebitProviderFailureKind failureKind = AutomaticDebitProviderFailureKind.Uncertain)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        FailureKind = failureKind;
    }

    public int? StatusCode { get; }
    public AutomaticDebitProviderFailureKind FailureKind { get; }
    public bool IsDefinitiveRejection => FailureKind == AutomaticDebitProviderFailureKind.DefinitiveRejection;
}
