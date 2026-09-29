using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;

namespace PagoTicAPI.Tests.Integration.Controllers;

[Collection("IntegrationTests")]
public class AutomaticDebitWebhookControllerIntegrationTests(IntegrationTestBase factory)
{
    private const int MaximumBodySizeBytes = 64 * 1024;

    [Fact]
    public async Task Notification_IsAnonymousAndForwardsExactRawBody()
    {
        byte[]? captured = null;
        factory.MockAutomaticDebitWebhookProcessor
            .Setup(x => x.ProcessAsync(2419, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<long, ReadOnlyMemory<byte>, CancellationToken>((_, body, _) => captured = body.ToArray())
            .ReturnsAsync(AutomaticDebitWebhookProcessingResult.Accepted());
        var client = factory.CreateClient();
        const string body = "{\n  \"type\": \"debit\", \"id\": \"pay-1\"\n}";

        var response = await client.PostAsync(
            "/api/debitos-automaticos/notificaciones/2419",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Encoding.UTF8.GetString(captured!).Should().Be(body);
    }

    [Fact]
    public async Task Notification_PropagatesFailClosedStatusWithoutJwtChallenge()
    {
        factory.MockAutomaticDebitWebhookProcessor
            .Setup(x => x.ProcessAsync(2419, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AutomaticDebitWebhookProcessingResult.Rejected(401, "authoritative_mismatch"));
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/debitos-automaticos/notificaciones/2419",
            new StringContent("{\"type\":\"debit\",\"id\":\"forged\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().BeEmpty();
    }

    [Fact]
    public async Task Notification_WhenChunkedBodyExceedsLimit_ReturnsPayloadTooLargeBeforeProcessor()
    {
        factory.MockAutomaticDebitWebhookProcessor.Invocations.Clear();
        using var client = factory.CreateClient();
        using var content = new UnknownLengthContent(new byte[MaximumBodySizeBytes + 1]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.PostAsync(
            "/api/debitos-automaticos/notificaciones/2419",
            content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        factory.MockAutomaticDebitWebhookProcessor.Verify(
            x => x.ProcessAsync(
                It.IsAny<long>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Notification_WhenEndpointRateLimitIsExceeded_ReturnsTooManyRequests()
    {
        const int permitLimit = 2;
        factory.MockAutomaticDebitWebhookProcessor.Reset();
        factory.MockAutomaticDebitWebhookProcessor
            .Setup(x => x.ProcessAsync(2419, It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AutomaticDebitWebhookProcessingResult.Accepted());
        using var rateLimitedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AutomaticDebitWebhook:MaxBodySizeBytes"] = MaximumBodySizeBytes.ToString(),
                    ["AutomaticDebitWebhook:RateLimit:PermitLimit"] = permitLimit.ToString(),
                    ["AutomaticDebitWebhook:RateLimit:WindowSeconds"] = "60"
                })));
        using var client = rateLimitedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var responses = new List<HttpResponseMessage>();
        for (var index = 0; index < permitLimit + 1; index++)
        {
            responses.Add(await client.PostAsync(
                "/api/debitos-automaticos/notificaciones/2419",
                new StringContent(
                    $"{{\"type\":\"debit\",\"id\":\"pay-{index}\"}}",
                    Encoding.UTF8,
                    "application/json")));
        }

        responses.Take(permitLimit).Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        responses.Last().StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private sealed class UnknownLengthContent(byte[] payload) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
