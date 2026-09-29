using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using PagoTicAPI.API.AutomaticDebits.PublicEndpoints;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.RequestDto.OperationalAudit;
using PagoTicAPI.Application.ResponseDto.OperationalAudit;

namespace PagoTicAPI.API.Controllers;

/// <summary>Expone consultas paginadas de operación y auditoría para débitos automáticos y checkouts.</summary>
[ApiController]
[Route("api/operaciones")]
[AllowAnonymous] // TEST only: require an administrative policy before production.
[EnableRateLimiting(AutomaticDebitPublicEndpointOptions.RateLimitPolicy)]
public sealed class OperationalAuditController : BaseController
{
    private readonly IOperationalAuditService _service;

    public OperationalAuditController(IOperationalAuditService service) => _service = service;

    /// <summary>Lista operaciones de débito automático aplicando filtros y paginación.</summary>
    [HttpGet("debitos")]
    public async Task<IActionResult> GetAutomaticDebits(
        [FromQuery] AutomaticDebitAuditFilter filter,
        CancellationToken cancellationToken) => Return(await _service.GetAutomaticDebitsAsync(filter, cancellationToken));

    /// <summary>Obtiene el detalle operativo de un débito automático.</summary>
    [HttpGet("debitos/{id:long}")]
    public async Task<IActionResult> GetAutomaticDebit(long id, CancellationToken cancellationToken) =>
        Return(await _service.GetAutomaticDebitAsync(id, cancellationToken));

    /// <summary>Obtiene el historial consolidado de eventos y notificaciones de un débito automático.</summary>
    [HttpGet("debitos/{id:long}/historial")]
    public async Task<IActionResult> GetAutomaticDebitHistory(
        long id, [FromQuery] AuditHistoryFilter filter, CancellationToken cancellationToken) =>
        Return(await _service.GetAutomaticDebitHistoryAsync(id, filter, cancellationToken));

    /// <summary>Lista checkouts de pago aplicando filtros y paginación.</summary>
    [HttpGet("checkouts")]
    public async Task<IActionResult> GetCheckouts(
        [FromQuery] CheckoutAuditFilter filter,
        CancellationToken cancellationToken) => Return(await _service.GetCheckoutsAsync(filter, cancellationToken));

    /// <summary>Obtiene el detalle operativo de un checkout y sus conceptos tributarios.</summary>
    [HttpGet("checkouts/{id:int}")]
    public async Task<IActionResult> GetCheckout(int id, CancellationToken cancellationToken) =>
        Return(await _service.GetCheckoutAsync(id, cancellationToken));

    /// <summary>Obtiene el historial de notificaciones asociado a un checkout.</summary>
    [HttpGet("checkouts/{id:int}/historial")]
    public async Task<IActionResult> GetCheckoutHistory(
        int id, [FromQuery] AuditHistoryFilter filter, CancellationToken cancellationToken) =>
        Return(await _service.GetCheckoutHistoryAsync(id, filter, cancellationToken));

    /// <summary>Genera un resumen agregado de débitos, checkouts e inboxes para operación y monitoreo.</summary>
    [HttpGet("resumen")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] OperationalSummaryFilter filter,
        CancellationToken cancellationToken) => Return(await _service.GetSummaryAsync(filter, cancellationToken));
}
