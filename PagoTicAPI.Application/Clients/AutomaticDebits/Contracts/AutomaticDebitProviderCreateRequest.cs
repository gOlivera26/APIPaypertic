namespace PagoTicAPI.Application.Clients.AutomaticDebits;

public sealed record AutomaticDebitProviderCreateRequest(string ExternalReference, string ConceptId, string ConceptDescription, AutomaticDebitProviderPayer Payer);
