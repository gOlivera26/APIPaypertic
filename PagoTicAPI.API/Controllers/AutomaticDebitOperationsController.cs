using Microsoft.AspNetCore.Authorization;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;

namespace PagoTicAPI.API.Controllers;

/// <summary>Administra la emisión, consulta, cancelación y reconciliación de débitos automáticos.</summary>
[Route("api/debitos-automaticos/debitos")]
[AllowAnonymous]
public sealed class AutomaticDebitOperationsController : BaseController
{
    private readonly IAutomaticDebitGenerationService _service;

    public AutomaticDebitOperationsController(IAutomaticDebitGenerationService service)
    {
        _service = service;
    }

    /// <summary>Genera un débito automático para una obligación tributaria elegible.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateAutomaticDebitRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return Return(result);
    }

    /// <summary>Consulta una operación de débito automático por su identificador local.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(id, cancellationToken);
        return Return(result);
    }

    /// <summary>Cancela una operación de débito automático pendiente.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Cancel(
        long id,
        [FromBody] CancelAutomaticDebitRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(id, request, cancellationToken);
        return Return(result);
    }

    /// <summary>Reconsulta PayPerTIC y sincroniza el estado autoritativo de una operación incierta.</summary>
    [HttpPost("{id:long}/reconciliar")]
    public async Task<IActionResult> Reconcile(long id, CancellationToken cancellationToken)
    {
        var result = await _service.ReconcileAsync(id, cancellationToken);
        return Return(result);
    }
}
