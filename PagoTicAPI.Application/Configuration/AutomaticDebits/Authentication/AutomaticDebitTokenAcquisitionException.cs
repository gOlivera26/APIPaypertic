namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

public sealed class AutomaticDebitTokenAcquisitionException : InvalidOperationException
{
    public AutomaticDebitTokenAcquisitionException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException) => StatusCode = statusCode;

    public int? StatusCode { get; }
}
