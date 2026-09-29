namespace PagoTicAPI.Application.Clients.AutomaticDebits;

public sealed record AutomaticDebitProviderPayer(string Name, string Email, string ExternalReference, string IdentificationType, string IdentificationNumber, string IdentificationCountry);
