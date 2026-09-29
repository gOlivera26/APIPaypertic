using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PagoTicAPI.Application.Configuration.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public sealed class PayPerTicTokenAcquirerTests
{
    [Fact]
    public async Task AcquireAsync_PostsJurisdictionCredentialsAndMapsToken()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"token-2419\",\"expires_in\":300}", Encoding.UTF8, "application/json")
        });
        var sut = new PayPerTicTokenAcquirer(
            new HttpClient(handler),
            NullLogger<PayPerTicTokenAcquirer>.Instance);

        var result = await sut.AcquireAsync(Configuration());

        result.Should().Be(new PayPerTicAccessToken("token-2419", 300));
        handler.Uri.Should().Be("https://auth.example.test/token");
        handler.Body.Should().Contain("grant_type=password");
        handler.Body.Should().Contain("username=jurisdiction-user");
        handler.Body.Should().Contain("client_id=jurisdiction-client");
    }

    [Fact]
    public async Task AcquireAsync_DoesNotLeakCredentialsOrProviderBodyOnFailure()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("provider-body-sensitive", Encoding.UTF8, "text/plain")
        });
        var sut = new PayPerTicTokenAcquirer(
            new HttpClient(handler),
            NullLogger<PayPerTicTokenAcquirer>.Instance);

        var act = () => sut.AcquireAsync(Configuration());

        var exception = await act.Should().ThrowAsync<AutomaticDebitTokenAcquisitionException>();
        exception.Which.Message.Should().NotContain("jurisdiction-password");
        exception.Which.Message.Should().NotContain("jurisdiction-secret");
        exception.Which.Message.Should().NotContain("provider-body-sensitive");
    }

    [Fact]
    public async Task AcquireAsync_TransportFailureHasNoPublicInnerException()
    {
        const string transportSecret = "client_secret=transport-secret-value";
        var sut = new PayPerTicTokenAcquirer(
            new HttpClient(new ThrowingHandler(new HttpRequestException(transportSecret))),
            NullLogger<PayPerTicTokenAcquirer>.Instance);

        var act = () => sut.AcquireAsync(Configuration());

        var exception = await act.Should().ThrowAsync<AutomaticDebitTokenAcquisitionException>();
        exception.Which.InnerException.Should().BeNull();
        exception.Which.ToString().Should().NotContain(transportSecret);
        exception.Which.ToString().Should().NotContain("transport-secret-value");
    }

    [Fact]
    public async Task AcquireAsync_CallerCancellationPreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sut = new PayPerTicTokenAcquirer(
            new HttpClient(new CancellingHandler()),
            NullLogger<PayPerTicTokenAcquirer>.Instance);

        var act = () => sut.AcquireAsync(Configuration(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
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

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public string? Uri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri = request.RequestUri?.ToString();
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }
}
