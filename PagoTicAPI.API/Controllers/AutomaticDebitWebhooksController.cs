using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PagoTicAPI.API.AutomaticDebits.Webhooks;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;

namespace PagoTicAPI.API.Controllers;

/// <summary>Recibe y procesa notificaciones de PayPerTIC sobre adhesiones y débitos automáticos.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(AutomaticDebitWebhookEndpointOptions.RateLimitPolicy)]
[Route("api/debitos-automaticos/notificaciones")]
public sealed class AutomaticDebitWebhooksController : ControllerBase
{
    private readonly IAutomaticDebitWebhookProcessor _processor;
    private readonly AutomaticDebitWebhookEndpointOptions _options;

    public AutomaticDebitWebhooksController(
        IAutomaticDebitWebhookProcessor processor,
        IOptions<AutomaticDebitWebhookEndpointOptions> options)
    {
        _processor = processor;
        _options = options.Value;
    }

    /// <summary>Valida el tamaño, preserva el cuerpo original y procesa una notificación de PayPerTIC.</summary>
    [HttpPost("{idJurisdiccion:long}")]
    public async Task<IActionResult> Notify(long idJurisdiccion, CancellationToken cancellationToken)
    {
        if (Request.ContentLength > _options.MaxBodySizeBytes)
        {
            return StatusCode(
                StatusCodes.Status413PayloadTooLarge,
                AutomaticDebitWebhookProcessingResult.Rejected(413, "payload_too_large"));
        }

        await using var buffer = new MemoryStream(Math.Min(
            _options.MaxBodySizeBytes,
            Request.ContentLength is > 0 ? (int)Request.ContentLength.Value : 0));
        var chunk = new byte[8192];
        while (true)
        {
            var read = await Request.Body.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > _options.MaxBodySizeBytes)
            {
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    AutomaticDebitWebhookProcessingResult.Rejected(413, "payload_too_large"));
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var result = await _processor.ProcessAsync(idJurisdiccion, buffer.ToArray(), cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
