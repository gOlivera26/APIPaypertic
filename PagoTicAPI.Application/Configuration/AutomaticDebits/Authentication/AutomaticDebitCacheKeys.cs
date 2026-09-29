namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

public static class AutomaticDebitCacheKeys
{
    public static string PayPerTicToken(long jurisdictionId, string configurationFingerprint) =>
        $"ppt:auto-debit:token:{jurisdictionId}:{configurationFingerprint}";
}
