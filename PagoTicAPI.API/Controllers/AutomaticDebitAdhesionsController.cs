using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using PagoTicAPI.API.AutomaticDebits.PublicEndpoints;
using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;

namespace PagoTicAPI.API.Controllers;

/// <summary>Administra la creación, consulta y cancelación de adhesiones al débito automático.</summary>
[Route("api/debitos-automaticos/adhesiones")]
[AllowAnonymous]
[EnableRateLimiting(AutomaticDebitPublicEndpointOptions.RateLimitPolicy)]
public sealed class AutomaticDebitAdhesionsController : BaseController
{
    private readonly IAutomaticDebitAdhesionService _service;

    public AutomaticDebitAdhesionsController(IAutomaticDebitAdhesionService service)
    {
        _service = service;
    }

    /// <summary>Crea una adhesión para una cuenta tributaria y obtiene el formulario de PayPerTIC.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateAutomaticDebitAdhesionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return Return(result);
    }

    /// <summary>Consulta la adhesión vigente de una cuenta tributaria en una jurisdicción.</summary>
    [HttpGet("{idTributoContribuyente}")]
    public async Task<IActionResult> Get(
        [FromRoute, Required, StringLength(50, MinimumLength = 1), RegularExpression("^[A-Za-z0-9._-]+$")]
        string idTributoContribuyente,
        [FromQuery, Range(typeof(long), "1", "9223372036854775807")] long idJurisdiccion,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(idJurisdiccion, idTributoContribuyente, cancellationToken);
        return Return(result);
    }

    /// <summary>Cancela la adhesión vigente de una cuenta tributaria en PayPerTIC y localmente.</summary>
    [HttpDelete("{idTributoContribuyente}")]
    public async Task<IActionResult> Cancel(
        [FromRoute, Required, StringLength(50, MinimumLength = 1), RegularExpression("^[A-Za-z0-9._-]+$")]
        string idTributoContribuyente,
        [FromQuery, Range(typeof(long), "1", "9223372036854775807")] long idJurisdiccion,
        [FromBody] CancelAutomaticDebitAdhesionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(
            idJurisdiccion,
            idTributoContribuyente,
            request,
            cancellationToken);
        return Return(result);
    }
}
