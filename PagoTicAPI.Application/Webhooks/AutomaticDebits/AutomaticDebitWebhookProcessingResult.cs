namespace PagoTicAPI.Application.Webhooks.AutomaticDebits;

public sealed record AutomaticDebitWebhookProcessingResult(int StatusCode, string Outcome, bool Duplicate)
{
    public static AutomaticDebitWebhookProcessingResult Accepted(bool duplicate = false) => new(200, duplicate ? "duplicate" : "accepted", duplicate);
    public static AutomaticDebitWebhookProcessingResult Rejected(int statusCode, string outcome) => new(statusCode, outcome, false);
}
