namespace PagoTicAPI.Application.Scheduling.AutomaticDebits;

public sealed record AutomaticDebitCandidate(long JurisdictionId, string TaxpayerAccountId, string ObligationId);
