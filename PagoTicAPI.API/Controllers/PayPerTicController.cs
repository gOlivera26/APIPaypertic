using Microsoft.AspNetCore.RateLimiting;
using PagoTicAPI.API.AutomaticDebits.PublicEndpoints;

namespace PagoTicAPI.API.Controllers;

/// <summary>
/// Endpoints para la integración con la pasarela de pagos PayPerTIC.
/// </summary>
[Route("api/pagostic")]
public sealed class PayPerTicController : BaseController
{
    private readonly IPayPerTicService _service;

    public PayPerTicController(IPayPerTicService service)
    {
        _service = service;
    }

    /// <summary>
    /// Inicia un checkout con PayPerTIC.
    /// Persiste el pago en BD, llama a PayPerTIC y retorna la form_url
    /// para redirigir al pagador.
    /// </summary>
    [HttpPost("checkout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [AllowAnonymous]
    [RequestSizeLimit(65_536)]
    [EnableRateLimiting(AutomaticDebitPublicEndpointOptions.RateLimitPolicy)]
    public async Task<IActionResult> CrearCheckout([FromBody] CreatePayPerTicCheckoutDto request)
    {
        var result = await _service.CrearCheckoutAsync(request);
        return Return(result);
    }

    /// <summary>
    /// Endpoint receptor del webhook de PayPerTIC.
    /// PayPerTIC llama a esta URL cuando el estado de un pago cambia.
    /// No requiere autenticación (la fuente es PayPerTIC, no el usuario).
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    [RequestSizeLimit(65_536)]
    [EnableRateLimiting(AutomaticDebitPublicEndpointOptions.RateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RecibirWebhook([FromBody] PayPerTicWebhookNotification notification)
    {
        var result = await _service.RecibirWebhookAsync(notification);
        return Return(result);
    }

    /// <summary>
    /// Cancela un pago en estado "pending" tanto en PayPerTIC como en la BD local.
    /// Body: { "status_detail": "motivo de la cancelación" }
    /// </summary>
    [HttpPost("cancelar/{pagIdExterno}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CancelarPago(string pagIdExterno, [FromBody] PayPerTicCancelRequest request)
    {
        var result = await _service.CancelarPagoAsync(pagIdExterno, request?.StatusDetail ?? string.Empty);
        return Return(result);
    }

    /// <summary>
    /// Solicita la devolución (refund) de un pago aprobado.
    /// </summary>
    [HttpPost("devolucion/{pagIdExterno}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DevolverPago(
        string pagIdExterno, [FromBody] PayPerTicRefundRequest request)
    {
        var result = await _service.DevolverPagoAsync(pagIdExterno, request);
        return Return(result);
    }
}
