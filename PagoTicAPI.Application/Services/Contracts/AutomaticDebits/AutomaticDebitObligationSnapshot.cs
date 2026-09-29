namespace PagoTicAPI.Application.Services.Interfaces;

public sealed record AutomaticDebitObligationSnapshot(string ObligationId, long JurisdictionId, string TaxpayerAccountId, string TaxTypeId, string Concept, string InstallmentNumber, string InstallmentYear, DateTime DueDate, string DebtState, string DebtSituation, bool IsDeactivated, decimal Amount, decimal PartiallyPaidAmount);
