using PagoTicAPI.Application.Configuration.AutomaticDebits;
using Microsoft.Extensions.Options;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;

namespace PagoTicAPI.API.Middlewares;

public sealed class AutomaticDebitFeatureGateMiddleware
{
    private static readonly PathString RoutePrefix = new("/api/debitos-automaticos");
    private readonly RequestDelegate _next;

    public AutomaticDebitFeatureGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptionsMonitor<AutomaticDebitFeatureOptions> options)
    {
        if (context.Request.Path.StartsWithSegments(RoutePrefix) && !options.CurrentValue.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }
}
