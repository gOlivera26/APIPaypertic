namespace PagoTicAPI.Application.Clients.AutomaticDebits;

public sealed record AutomaticDebitProviderPaymentRequest(string ExternalTransactionId, DateTimeOffset DueDate, string ExternalReference, string ConceptId, string ConceptDescription, decimal Amount);
