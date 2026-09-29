namespace PagoTicAPI.Application.Webhooks.AutomaticDebits;

/// <summary>Genera identificadores persistentes para inboxes y eventos de webhook.</summary>
public interface IAutomaticDebitWebhookIdGenerator
{
    /// <summary>Genera el siguiente identificador de inbox.</summary>
    Task<long> NextInboxIdAsync(CancellationToken cancellationToken = default);
    /// <summary>Genera el siguiente identificador de evento.</summary>
    Task<long> NextEventIdAsync(CancellationToken cancellationToken = default);
}
