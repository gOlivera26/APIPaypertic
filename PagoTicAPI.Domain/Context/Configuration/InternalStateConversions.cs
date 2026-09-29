using PagoTicAPI.Domain.Models;
using PagoTicAPI.Domain.Models.AutomaticDebits;

namespace PagoTicAPI.Domain.Context.Configuration;

internal static class InternalStateConversions
{
    public static string ToDatabase(AutomaticDebitProcessingStates value) => value switch
    {
        AutomaticDebitProcessingStates.Pending => "PENDING",
        AutomaticDebitProcessingStates.Imputed => "IMPUTED",
        AutomaticDebitProcessingStates.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported processing state.")
    };

    public static AutomaticDebitProcessingStates ToProcessingState(string value) => value switch
    {
        "PENDING" => AutomaticDebitProcessingStates.Pending,
        "IMPUTED" => AutomaticDebitProcessingStates.Imputed,
        "FAILED" => AutomaticDebitProcessingStates.Failed,
        _ => throw new InvalidOperationException($"Unsupported persisted processing state '{value}'.")
    };

    public static string ToDatabase(AutomaticDebitInboxResults value) => value switch
    {
        AutomaticDebitInboxResults.Received => "RECEIVED",
        AutomaticDebitInboxResults.Processed => "PROCESSED",
        AutomaticDebitInboxResults.Rejected => "REJECTED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported automatic-debit inbox result.")
    };

    public static AutomaticDebitInboxResults ToAutomaticDebitInboxResult(string value) => value switch
    {
        "RECEIVED" => AutomaticDebitInboxResults.Received,
        "PROCESSED" => AutomaticDebitInboxResults.Processed,
        "REJECTED" => AutomaticDebitInboxResults.Rejected,
        _ => throw new InvalidOperationException($"Unsupported persisted automatic-debit inbox result '{value}'.")
    };

    public static string ToDatabase(PayPerTicPaymentWebhookInboxResults value) => value switch
    {
        PayPerTicPaymentWebhookInboxResults.Received => "RECEIVED",
        PayPerTicPaymentWebhookInboxResults.Processed => "PROCESSED",
        PayPerTicPaymentWebhookInboxResults.Rejected => "REJECTED",
        PayPerTicPaymentWebhookInboxResults.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported payment inbox result.")
    };

    public static PayPerTicPaymentWebhookInboxResults ToPaymentInboxResult(string value) => value switch
    {
        "RECEIVED" => PayPerTicPaymentWebhookInboxResults.Received,
        "PROCESSED" => PayPerTicPaymentWebhookInboxResults.Processed,
        "REJECTED" => PayPerTicPaymentWebhookInboxResults.Rejected,
        "FAILED" => PayPerTicPaymentWebhookInboxResults.Failed,
        _ => throw new InvalidOperationException($"Unsupported persisted payment inbox result '{value}'.")
    };
}
