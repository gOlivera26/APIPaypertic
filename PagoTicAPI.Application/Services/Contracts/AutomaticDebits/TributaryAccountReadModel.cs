namespace PagoTicAPI.Application.Services.Interfaces;

public sealed record TributaryAccountReadModel(long JurisdictionId, string TaxpayerAccountId, string PersonId, string ConceptId, string ConceptDescription, string AccountKey, string PayerName, string PayerEmail, string IdentificationType, string IdentificationNumber, string IdentificationCountry);
