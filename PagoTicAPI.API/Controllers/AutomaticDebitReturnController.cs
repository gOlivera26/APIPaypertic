using Microsoft.AspNetCore.Authorization;
using PagoTicAPI.Application.Configuration.AutomaticDebits;

namespace PagoTicAPI.API.Controllers;

/// <summary>Procesa el retorno del navegador desde el formulario de adhesión de PayPerTIC.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/debitos-automaticos/retorno")]
public sealed class AutomaticDebitReturnController : ControllerBase
{
    private readonly IJurisdictionPayPerTicConfigurationProvider _configurationProvider;

    public AutomaticDebitReturnController(
        IJurisdictionPayPerTicConfigurationProvider configurationProvider)
    {
        _configurationProvider = configurationProvider;
    }

    /// <summary>Redirige al portal configurado para la jurisdicción luego de finalizar la adhesión.</summary>
    [AcceptVerbs("GET", "POST")]
    [Route("{idJurisdiccion:long}")]
    public async Task<IActionResult> Return(
        long idJurisdiccion,
        CancellationToken cancellationToken)
    {
        JurisdictionPayPerTicConfiguration configuration;
        try
        {
            configuration = await _configurationProvider.GetActiveAsync(
                idJurisdiccion,
                cancellationToken);
        }
        catch (PayPerTicConfigurationNotFoundException)
        {
            return NotFound();
        }

        if (!Uri.TryCreate(configuration.BackUrl, UriKind.Absolute, out var destination) ||
            destination.Scheme != Uri.UriSchemeHttps)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                detail: "The automatic-debit return URL is not configured correctly.");
        }

        Response.Headers.Location = destination.AbsoluteUri;
        return StatusCode(StatusCodes.Status303SeeOther);
    }
}
