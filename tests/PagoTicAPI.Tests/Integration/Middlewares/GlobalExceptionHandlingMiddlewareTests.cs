using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PagoTicAPI.API.Middlewares;
using PagoTicAPI.Application.Configuration.AutomaticDebits;

namespace PagoTicAPI.Tests.Integration.Middlewares;

public sealed class GlobalExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task Invoke_TokenTransportFailure_DoesNotExposeTransportDetails()
    {
        const string transportSecret = "client_secret=transport-secret-value";
        var acquirer = new PayPerTicTokenAcquirer(
            new HttpClient(new ThrowingHandler(new HttpRequestException(transportSecret))),
            NullLogger<PayPerTicTokenAcquirer>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlingMiddleware(
            async _ => await acquirer.AcquireAsync(Configuration()),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        await middleware.Invoke(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().NotContain("PayPerTIC token acquisition failed.");
        body.Should().NotContain(transportSecret);
        body.Should().NotContain("transport-secret-value");
        body.Should().NotContain("StackTrace");
        body.Should().Contain("\"Exception\":null");
        body.Should().Contain("\"ExceptionDetails\":null");
    }

    private static JurisdictionPayPerTicConfiguration Configuration() => new(
        2419,
        "https://auth.example.test/token",
        "https://api.example.test",
        "jurisdiction-user",
        "jurisdiction-password",
        "jurisdiction-client",
        "jurisdiction-secret",
        "collector-2419",
        null,
        null,
        null);

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }
}
