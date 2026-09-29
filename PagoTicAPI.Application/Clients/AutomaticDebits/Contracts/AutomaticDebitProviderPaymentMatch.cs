namespace PagoTicAPI.Application.Clients.AutomaticDebits;

public sealed record AutomaticDebitProviderPaymentMatch(
    string Id,
    string ExternalTransactionId,
    string CollectorId);
