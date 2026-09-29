namespace PagoTicAPI.Application.Webhooks.AutomaticDebits;

internal static class AutomaticDebitAuthoritativeFingerprint
{
    public static string Create(AutomaticDebitProviderReadBack readBack)
    {
        var canonical = string.Concat(
            Segment(readBack.ObjectType.Trim().ToLowerInvariant()),
            Segment(readBack.Id.Trim()),
            Segment(readBack.Status.Trim().ToUpperInvariant()),
            Segment(readBack.CollectorId.Trim()),
            Segment(readBack.ExternalReference.Trim()),
            Segment(readBack.LastUpdateDate?.ToUniversalTime().ToString("O") ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Segment(string value) => $"{value.Length}:{value}";
}
