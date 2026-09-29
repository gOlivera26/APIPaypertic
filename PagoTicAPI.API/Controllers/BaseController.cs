namespace PagoTicAPI.API.Controllers;

/// <summary>
/// Proporciona la conversión común de respuestas de aplicación a respuestas HTTP.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class BaseController : ControllerBase
{
    /// <summary>
    /// Convierte una respuesta de operación en el código y resultado HTTP correspondientes.
    /// </summary>
    protected IActionResult Return<T>(OperationResponse<T> response)
    {
        var code = response.Code ?? (response.Success == true ? 200 : 500);

        return code switch
        {
            200 => Ok(response),
            201 => Created("", response),
            400 => BadRequest(response),
            401 => Unauthorized(response),
            403 => StatusCode(StatusCodes.Status403Forbidden, response),
            404 => NotFound(response),
            500 => StatusCode(500, response),
            _ => StatusCode(code, response)
        };
    }
}
