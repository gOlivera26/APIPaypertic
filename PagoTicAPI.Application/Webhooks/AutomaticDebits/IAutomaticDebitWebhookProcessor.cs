namespace PagoTicAPI.Application.Webhooks.AutomaticDebits;

/// <summary>
/// Procesa notificaciones autoritativas de PayPerTIC para débitos automáticos.
/// </summary>
public interface IAutomaticDebitWebhookProcessor
{
    /// <summary>
    /// Procesa una notificación con su cuerpo original.
    /// </summary>
    Task<AutomaticDebitWebhookProcessingResult> ProcessAsync(
        long jurisdictionId,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken cancellationToken = default);
}
