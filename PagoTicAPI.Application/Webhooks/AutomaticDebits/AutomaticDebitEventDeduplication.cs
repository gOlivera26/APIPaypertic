using Oracle.ManagedDataAccess.Client;

namespace PagoTicAPI.Application.Webhooks.AutomaticDebits;

internal static class AutomaticDebitEventDeduplication
{
    public static bool IsExpectedCollision(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OracleException oracle && oracle.Number == 1 &&
                System.Text.RegularExpressions.Regex.IsMatch(
                    oracle.Message,
                    @"(?<![A-Z0-9_])UK_DEB_AUT_EVENTO_DEDUP(?![A-Z0-9_])",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                return true;
        }

        return false;
    }

    public static bool IsInboxCollision(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OracleException oracle && oracle.Number == 1 &&
                System.Text.RegularExpressions.Regex.IsMatch(
                    oracle.Message,
                    @"(?<![A-Z0-9_])UK_DEB_AUT_NOTIF_DEDUP(?![A-Z0-9_])",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                return true;
        }

        return false;
    }

    public static async Task<bool> ExistsMatchingAsync(
        gtwContext context,
        AutomaticDebitEvent expected,
        CancellationToken cancellationToken)
    {
        var existing = await context.AutomaticDebitEvents.AsNoTracking().FirstOrDefaultAsync(
            x => x.JurisdictionId == expected.JurisdictionId &&
                 x.DeduplicationKey == expected.DeduplicationKey,
            cancellationToken);
        if (existing is null)
            return false;

        if (existing.OperationId != expected.OperationId ||
            existing.AdhesionId != expected.AdhesionId ||
            !string.Equals(existing.ProviderObjectId, expected.ProviderObjectId, StringComparison.Ordinal) ||
            !string.Equals(existing.EventType, expected.EventType, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existing.ProviderState, expected.ProviderState, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(existing.AuthoritativeHash, expected.AuthoritativeHash, StringComparison.Ordinal) ||
            existing.AuthoritativeUpdatedAt != expected.AuthoritativeUpdatedAt)
            throw new InvalidOperationException("The existing automatic-debit event does not match the authoritative event.");

        return true;
    }
}
