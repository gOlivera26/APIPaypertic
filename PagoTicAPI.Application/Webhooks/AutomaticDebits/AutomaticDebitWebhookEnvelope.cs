namespace PagoTicAPI.Application.Webhooks.AutomaticDebits;

public sealed record AutomaticDebitWebhookHint(
    string ObjectType,
    string ProviderId,
    string NotificationType);

public sealed class AutomaticDebitWebhookEnvelope
{
    private AutomaticDebitWebhookEnvelope(byte[] rawBytes, string payloadHash, AutomaticDebitWebhookHint? hint)
    {
        RawBytes = rawBytes;
        PayloadHash = payloadHash;
        Hint = hint;
    }

    public byte[] RawBytes { get; }
    public string PayloadHash { get; }
    public AutomaticDebitWebhookHint? Hint { get; }

    public static AutomaticDebitWebhookEnvelope Create(ReadOnlyMemory<byte> rawBody)
    {
        var exactBytes = rawBody.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(exactBytes)).ToLowerInvariant();
        return new AutomaticDebitWebhookEnvelope(exactBytes, hash, TryParseHint(exactBytes));
    }

    private static AutomaticDebitWebhookHint? TryParseHint(ReadOnlyMemory<byte> rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                !root.TryGetProperty("id", out var idElement))
            {
                return null;
            }

            var objectType = typeElement.GetString()?.Trim().ToLowerInvariant();
            var providerId = idElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            var normalizedObjectType = objectType switch
            {
                AutomaticDebitWebhookNotificationTypes.Adhesion or
                AutomaticDebitWebhookNotificationTypes.Subscription =>
                    AutomaticDebitWebhookObjectTypes.Subscription,
                AutomaticDebitWebhookNotificationTypes.Debit or
                AutomaticDebitWebhookNotificationTypes.Online or
                AutomaticDebitWebhookNotificationTypes.Transfer or
                AutomaticDebitWebhookNotificationTypes.Debin or
                AutomaticDebitWebhookNotificationTypes.Coupon =>
                    AutomaticDebitWebhookObjectTypes.Payment,
                _ => null
            };
            return normalizedObjectType is null || objectType is null
                ? null
                : new AutomaticDebitWebhookHint(normalizedObjectType, providerId, objectType);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class AutomaticDebitWebhookObjectTypes
{
    public const string Payment = "payment";
    public const string Subscription = "subscription";
}

public static class AutomaticDebitWebhookNotificationTypes
{
    public const string Adhesion = "adhesion";
    public const string Subscription = "subscription";
    public const string Debit = "debit";
    public const string Online = "online";
    public const string Transfer = "transfer";
    public const string Debin = "debin";
    public const string Coupon = "coupon";
}
