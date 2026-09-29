using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PagoTicAPI.Application.Clients.AutomaticDebits;
using PagoTicAPI.Application.Configuration.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public class AutomaticDebitPayPerTicClientTests
{
    private const long JurisdictionId = 2419;
    private const string AccessToken = "jurisdiction-2419-token";
    private const string ClientSecret = "jurisdiction-2419-secret";

    [Fact]
    public async Task FindDebitByExternalIdAsync_SendsContractualFiltersAndMapsMinimalSingleMatch()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"page":1,"limit":2,"size":1,"rows":[{"id":"pay-1","external_transaction_id":"ext-1","collector_id":"collector-2419","status":"issued","request_date":"2026-09-08T10:00:00-0300"}]}"""));
        var (sut, _, _) = CreateClient(handler);

        var result = await sut.FindDebitByExternalIdAsync(JurisdictionId, "ext-1", new DateTime(2026, 9, 8, 1, 0, 0, DateTimeKind.Utc), "collector-2419");

        result.Should().Be(new AutomaticDebitProviderPaymentMatch("pay-1", "ext-1", "collector-2419"));
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Body.Should().BeNull();
        request.AuthorizationParameter.Should().Be(AccessToken);
        var query = Uri.UnescapeDataString(new Uri(request.Uri).Query);
        query.Should().Contain("page=1&limit=2");
        query.Should().Contain("filters[0][field]=external_transaction_id&filters[0][operation]=EQUAL&filters[0][value]=ext-1");
        query.Should().Contain("filters[1][field]=collector_id&filters[1][operation]=EQUAL&filters[1][value]=collector-2419");
        query.Should().Contain("filters[2][field]=request_date&filters[2][operation]=GREATER_THAN_OR_EQUAL_TO");
        query.Should().Contain("filters[2][value]=20260907T000000000-0300");
        query.Should().Contain("filters[3][field]=request_date&filters[3][operation]=LESS_THAN");
    }

    [Fact]
    public async Task FindDebitByExternalIdAsync_UsesHistoricalCollectorWithActiveUrlAndToken()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"page":1,"limit":2,"size":1,"rows":[{"id":"pay-old","external_transaction_id":"ext-1","collector_id":"historical"}]}"""));
        var (sut, _, _) = CreateClient(handler);

        var result = await sut.FindDebitByExternalIdAsync(JurisdictionId, "ext-1", DateTime.UtcNow, "historical");

        result!.CollectorId.Should().Be("historical");
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Uri.Should().StartWith("https://api.example.test/root/pagos?");
        request.AuthorizationParameter.Should().Be(AccessToken);
        Uri.UnescapeDataString(new Uri(request.Uri).Query).Should().Contain("filters[1][value]=historical");
    }

    [Theory]
    [InlineData("{\"page\":1,\"limit\":2,\"size\":0,\"rows\":[]}", false)]
    [InlineData("{\"page\":1,\"limit\":2,\"size\":2,\"rows\":[{},{}]}", true)]
    [InlineData("{\"page\":1,\"limit\":2,\"size\":2,\"rows\":[{}]}", true)]
    public async Task FindDebitByExternalIdAsync_HandlesZeroAndMultipleMatches(string body, bool ambiguous)
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var (sut, _, _) = CreateClient(handler);
        var action = () => sut.FindDebitByExternalIdAsync(JurisdictionId, "ext-1", DateTime.UtcNow, "collector-2419");
        if (ambiguous)
            await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        else
            (await action()).Should().BeNull();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task FindDebitByExternalIdAsync_TransportFailureIsUncertain()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("transport"));
        var (sut, _, _) = CreateClient(handler);
        var action = () => sut.FindDebitByExternalIdAsync(JurisdictionId, "ext-1", DateTime.UtcNow, "collector-2419");
        var error = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        error.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Fact]
    public async Task FindDebitByExternalIdAsync_InternalTimeoutIsUncertain()
    {
        var handler = new RecordingHandler(_ => throw new TaskCanceledException("Simulated timeout."));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.FindDebitByExternalIdAsync(JurisdictionId, "ext-1", DateTime.UtcNow, "collector-2419");

        var error = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        error.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Theory]
    [InlineData("other", "collector-2419")]
    [InlineData("ext-1", "other")]
    public async Task FindDebitByExternalIdAsync_RejectsMismatchedIdentity(string externalId, string collectorId)
    {
        var body = $$"""{"page":1,"limit":2,"size":1,"rows":[{"id":"pay-1","external_transaction_id":"{{externalId}}","collector_id":"{{collectorId}}"}]}""";
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, body));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.FindDebitByExternalIdAsync(JurisdictionId, "ext-1", DateTime.UtcNow, "collector-2419");

        var error = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        error.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Fact]
    public async Task FindDebitByExternalIdAsync_Http5xxIsUncertain()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.ServiceUnavailable, "{}"));
        var (sut, _, _) = CreateClient(handler);
        var action = () => sut.FindDebitByExternalIdAsync(JurisdictionId, "ext-1", DateTime.UtcNow, "collector-2419");
        var error = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        error.Which.StatusCode.Should().Be(503);
        error.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Fact]
    public async Task GetPaymentReadBackAsync_MapsFullPaymentDetailSeparatelyFromExternalTransactionId()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"id":"pay-1","status":"issued","collector_id":"collector-2419","external_transaction_id":"ext-1","type":"debit","currency_id":"ARS","details":[{"amount":36469.09,"external_reference":"ob-1","concept_id":"6","concept_description":"Tax"}]}"""));
        var (sut, _, _) = CreateClient(handler);

        var result = await sut.GetPaymentReadBackAsync(JurisdictionId, "pay-1");

        result.ExternalReference.Should().Be("ext-1");
        result.PaymentType.Should().Be("debit");
        result.PaymentCurrencyId.Should().Be("ARS");
        result.DetailAmount.Should().Be(36469.09m);
        result.DetailExternalReference.Should().Be("ob-1");
        result.DetailConceptId.Should().Be("6");
        result.DetailConceptDescription.Should().Be("Tax");
    }

    [Fact]
    public async Task CreateAdhesionAsync_SendsDocumentedContractAndMapsResponse()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"id":"ppt-adh-1","form_url":"https://forms.example.test/1","status":"pending"}"""));
        var (sut, configuration, tokenProvider) = CreateClient(handler);

        var result = await sut.CreateAdhesionAsync(JurisdictionId, ProviderRequest());

        result.Should().Be(new AutomaticDebitProviderAdhesion(
            "ppt-adh-1",
            "https://forms.example.test/1",
            "pending"));
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be("https://api.example.test/root/suscripciones");
        request.AuthorizationScheme.Should().Be("Bearer");
        request.AuthorizationParameter.Should().Be(AccessToken);

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        root.GetProperty("type").GetString().Should().Be("adhesion");
        root.GetProperty("collector_id").GetString().Should().Be("collector-2419");
        root.GetProperty("currency_id").GetString().Should().Be("ARS");
        root.GetProperty("notification_url").GetString().Should().Be("https://callbacks.example.test/automatic-debits");
        root.GetProperty("return_url").GetString().Should().Be("https://portal.example.test/return");
        root.GetProperty("back_url").GetString().Should().Be("https://portal.example.test/back");

        var detail = root.GetProperty("detail");
        detail.GetProperty("external_reference").GetString().Should().Be("VGBIN85447");
        detail.GetProperty("concept_id").GetString().Should().Be("6");
        detail.GetProperty("concept_description").GetString().Should().Be("Inmueble");

        var payer = root.GetProperty("payer");
        payer.GetProperty("name").GetString().Should().Be("Maria Perez");
        payer.GetProperty("email").GetString().Should().Be("maria@example.test");
        payer.GetProperty("external_reference").GetString().Should().Be("MVGB318713");
        var identification = payer.GetProperty("identification");
        identification.GetProperty("type").GetString().Should().Be("DNI_ARG");
        identification.GetProperty("number").GetString().Should().Be("30111222");
        identification.GetProperty("country").GetString().Should().Be("ARG");
        configuration.Verify(x => x.GetActiveAsync(JurisdictionId, It.IsAny<CancellationToken>()), Times.Once);
        tokenProvider.Verify(x => x.GetTokenAsync(JurisdictionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAdhesionAsync_InternalTimeout_IsUncertain()
    {
        var handler = new RecordingHandler(_ => throw new TaskCanceledException("Simulated HTTP timeout."));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.CreateAdhesionAsync(JurisdictionId, ProviderRequest());

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Fact]
    public async Task CreateAdhesionAsync_HttpRequestException_IsUncertain()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("Simulated transport failure."));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.CreateAdhesionAsync(JurisdictionId, ProviderRequest());

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Fact]
    public async Task CreateAdhesionAsync_Http5xx_IsUncertain()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.InternalServerError,
            "{\"code\":\"provider_failure\"}"));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.CreateAdhesionAsync(JurisdictionId, ProviderRequest());

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.StatusCode.Should().Be(500);
        exception.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task CreateAdhesionAsync_Http4xx_RemainsUncertain(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => JsonResponse(statusCode, "{\"code\":\"provider_rejection\"}"));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.CreateAdhesionAsync(JurisdictionId, ProviderRequest());

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.StatusCode.Should().Be((int)statusCode);
        exception.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Fact]
    public async Task CreateAdhesionAsync_InvalidJson_IsUncertain()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, "not-json"));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.CreateAdhesionAsync(JurisdictionId, ProviderRequest());

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Theory]
    [InlineData("{\"status\":\"pending\"}")]
    [InlineData("{\"id\":\"ppt-adh-incomplete\"}")]
    public async Task CreateAdhesionAsync_IncompleteResponse_IsUncertain(string responseBody)
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, responseBody));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.CreateAdhesionAsync(JurisdictionId, ProviderRequest());

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Fact]
    public async Task CreateAdhesionAsync_CallerCancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler(_ =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("Caller cancelled.", null, cancellation.Token);
        });
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.CreateAdhesionAsync(
            JurisdictionId,
            ProviderRequest(),
            cancellation.Token);

        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.Should().NotBeOfType<AutomaticDebitProviderException>();
        cancellation.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task GetAdhesionAsync_UsesEscapedIdentifierAndJurisdictionBearerToken()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"id":"adh/with space","form_url":null,"status":"active"}"""));
        var (sut, _, tokenProvider) = CreateClient(handler);

        var result = await sut.GetAdhesionAsync(JurisdictionId, "adh/with space");

        result.Should().Be(new AutomaticDebitProviderAdhesion("adh/with space", null, "active"));
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Uri.Should().Be("https://api.example.test/root/suscripciones/adh%2Fwith%20space");
        request.Body.Should().BeNull();
        request.AuthorizationParameter.Should().Be(AccessToken);
        tokenProvider.Verify(x => x.GetTokenAsync(JurisdictionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPaymentReadBackAsync_UsesDocumentedEndpointAndMapsAuthoritativeFields()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"id":"pay/1","status":"approved","collector_id":"collector-2419","external_transaction_id":"ext-1","last_update_date":"2026-09-01T10:00:00Z"}"""));
        var (sut, configuration, tokenProvider) = CreateClient(handler);

        var result = await sut.GetPaymentReadBackAsync(JurisdictionId, "pay/1");

        result.Should().Be(new AutomaticDebitProviderReadBack(
            "payment",
            "pay/1",
            "approved",
            "collector-2419",
            "ext-1",
            DateTimeOffset.Parse("2026-09-01T10:00:00Z")));
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Uri.Should().Be("https://api.example.test/root/pagos/pay%2F1");
        request.AuthorizationParameter.Should().Be(AccessToken);
        configuration.Verify(x => x.GetActiveAsync(JurisdictionId, It.IsAny<CancellationToken>()), Times.Once);
        tokenProvider.Verify(x => x.GetTokenAsync(JurisdictionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAdhesionReadBackAsync_MapsNestedExternalReferenceWithoutTimestamp()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"type":"adhesion","id":"adh-1","status":"active","collector_id":"collector-2419","detail":{"external_reference":"VGBIN85447"}}"""));
        var (sut, _, _) = CreateClient(handler);

        var result = await sut.GetAdhesionReadBackAsync(JurisdictionId, "adh-1");

        result.Should().Be(new AutomaticDebitProviderReadBack(
            "subscription",
            "adh-1",
            "active",
            "collector-2419",
            "VGBIN85447",
            null));
        handler.Requests.Should().ContainSingle().Subject.Uri
            .Should().Be("https://api.example.test/root/suscripciones/adh-1");
    }

    [Fact]
    public async Task ReadBackNonSuccess_DoesNotExposeCredentialsOrProviderBody()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.InternalServerError,
            $"{{\"error\":\"{AccessToken} {ClientSecret}\"}}"));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.GetPaymentReadBackAsync(JurisdictionId, "pay-1");

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.StatusCode.Should().Be(500);
        exception.Which.ToString().Should().NotContain(AccessToken).And.NotContain(ClientSecret);
        exception.Which.Message.Should().Be("PayPerTIC automatic-debit read-back failed.");
    }

    [Fact]
    public async Task GetPaymentReadBackAsync_DoesNotConfusePaymentSubtypeWithWebhookObjectType()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"type":"debit","id":"pay-1","status":"approved","collector_id":"collector-2419","external_transaction_id":"ext-1"}"""));
        var (sut, _, _) = CreateClient(handler);

        var result = await sut.GetPaymentReadBackAsync(JurisdictionId, "pay-1");

        result.ObjectType.Should().Be("payment");
    }

    [Fact]
    public async Task GetPaymentReadBackAsync_MapsDocumentedPaymentTimestampSpelling()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"type":"debit","id":"pay-1","status":"approved","collector_id":"collector-2419","external_transaction_id":"ext-1","last_updated_date":"2026-09-01T11:00:00Z"}"""));
        var (sut, _, _) = CreateClient(handler);

        var result = await sut.GetPaymentReadBackAsync(JurisdictionId, "pay-1");

        result.LastUpdateDate.Should().Be(DateTimeOffset.Parse("2026-09-01T11:00:00Z"));
    }

    [Fact]
    public async Task GetPaymentReadBackAsync_InvalidLastUpdateDateIsRejected()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"id":"pay-1","status":"approved","collector_id":"collector-2419","external_transaction_id":"ext-1","last_update_date":"not-a-date"}"""));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.GetPaymentReadBackAsync(JurisdictionId, "pay-1");

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.Message.Should().Be("PayPerTIC returned an invalid read-back response.");
    }

    [Fact]
    public async Task CancelAdhesionAsync_SendsStatusDetailAndMapsCancelledResponse()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"id":"ppt-adh-2","form_url":null,"status":"cancelled"}"""));
        var (sut, _, _) = CreateClient(handler);

        var result = await sut.CancelAdhesionAsync(JurisdictionId, "ppt-adh-2", "Requested by taxpayer");

        result.Should().Be(new AutomaticDebitProviderAdhesion("ppt-adh-2", null, "cancelled"));
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be("https://api.example.test/root/suscripciones/cancelar/ppt-adh-2");
        request.AuthorizationParameter.Should().Be(AccessToken);
        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("status_detail").GetString().Should().Be("Requested by taxpayer");
    }

    [Fact]
    public async Task CancelPaymentAsync_UsesDocumentedIssuedPaymentEndpointAndReason()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        var (sut, _, _) = CreateClient(handler);

        await sut.CancelPaymentAsync(JurisdictionId, "pay/2", "Controlled sandbox cancellation");

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.Should().Be("https://api.example.test/root/pagos/cancelar/pay%2F2");
        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("status_detail").GetString()
            .Should().Be("Controlled sandbox cancellation");
    }

    [Fact]
    public async Task CreateDebitAsync_SendsDocumentedAdhesionPaymentContract()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"id":"ppt-debit-1","status":"issued"}"""));
        var (sut, _, _) = CreateClient(handler);
        var dueDate = DateTimeOffset.Parse("2026-12-15T00:00:00-03:00");

        var result = await sut.CreateDebitAsync(
            JurisdictionId,
            "adh/1",
            new AutomaticDebitProviderPaymentRequest(
                "PPT-DA-2419-6012353270-1", dueDate, "6012353270", "6", "Tasa por Servicio a la Propiedad", 36469.09m));

        result.Should().Be(new AutomaticDebitProviderPayment("ppt-debit-1", "issued"));
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Uri.Should().Be("https://api.example.test/root/suscripciones/adhesion/adh%2F1/pago");
        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("collector_id").GetString().Should().Be("collector-2419");
        body.RootElement.GetProperty("external_transaction_id").GetString().Should().Be("PPT-DA-2419-6012353270-1");
        body.RootElement.GetProperty("due_date").GetString().Should().Be("2026-12-15T00:00:00-0300");
        var detail = body.RootElement.GetProperty("details")[0];
        detail.GetProperty("external_reference").GetString().Should().Be("6012353270");
        detail.GetProperty("amount").GetDecimal().Should().Be(36469.09m);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData((HttpStatusCode)425)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task CreateDebitAsync_NonSuccessHttp_IsUncertain(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => JsonResponse(statusCode, "{\"code\":\"failure\"}"));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.CreateDebitAsync(
            JurisdictionId,
            "adh-1",
            new AutomaticDebitProviderPaymentRequest(
                "ext-1", DateTimeOffset.Parse("2026-12-15T00:00:00-03:00"),
                "6012353270", "6", "Concept", 10m));

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.StatusCode.Should().Be((int)statusCode);
        exception.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("transport")]
    [InlineData("invalid_json")]
    public async Task CreateDebitAsync_IndeterminateResponse_IsUncertain(string scenario)
    {
        var handler = new RecordingHandler(_ => scenario switch
        {
            "timeout" => throw new TaskCanceledException("Provider timed out."),
            "transport" => throw new HttpRequestException("Connection failed."),
            _ => JsonResponse(HttpStatusCode.OK, "not-json")
        });
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.CreateDebitAsync(
            JurisdictionId, "adh-1",
            new AutomaticDebitProviderPaymentRequest(
                "ext-1", DateTimeOffset.Parse("2026-12-15T00:00:00-03:00"),
                "6012353270", "6", "Concept", 10m));

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.FailureKind.Should().Be(AutomaticDebitProviderFailureKind.Uncertain);
        exception.Which.StatusCode.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ProviderNonSuccess_IsNormalizedWithoutResponseOrCredentialDisclosure(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            statusCode,
            $"{{\"error\":\"{AccessToken} {ClientSecret}\"}}"));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.GetAdhesionAsync(JurisdictionId, "ppt-adh-error");

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.StatusCode.Should().Be((int)statusCode);
        exception.Which.ToString().Should().NotContain(AccessToken).And.NotContain(ClientSecret);
        exception.Which.Message.Should().Be("PayPerTIC adhesion request failed.");
    }

    [Fact]
    public async Task ProviderNonSuccess_LogsAllowlistedDiagnosticWithoutSensitiveFields()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.BadRequest,
            $"{{\"code\":400,\"extended_code\":4007,\"error\":\"Invalid request.\",\"message\":\"Invalid return URL.\",\"access_token\":\"{AccessToken}\",\"client_secret\":\"{ClientSecret}\"}}"));
        var logger = new Mock<ILogger<AutomaticDebitPayPerTicClient>>();
        var (sut, _, _) = CreateClient(handler, logger.Object);

        var action = () => sut.GetAdhesionAsync(JurisdictionId, "ppt-adh-error");

        await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    value.ToString()!.Contains("4007") &&
                    value.ToString()!.Contains("Invalid request.") &&
                    value.ToString()!.Contains("Invalid return URL.") &&
                    !value.ToString()!.Contains(AccessToken) &&
                    !value.ToString()!.Contains(ClientSecret)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"\",\"status\":\"pending\"}")]
    [InlineData("{\"id\":\"ppt-adh-3\",\"status\":\"\"}")]
    public async Task MalformedProviderResponse_IsNormalizedWithoutCredentialDisclosure(string responseBody)
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, responseBody));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.GetAdhesionAsync(JurisdictionId, "ppt-adh-malformed");

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.ToString().Should().NotContain(AccessToken).And.NotContain(ClientSecret);
        exception.Which.Message.Should().NotContain(responseBody);
    }

    [Fact]
    public async Task TransportException_DoesNotExposeTokenOrSecretThroughExceptionChain()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException(
            $"Transport diagnostic contained {AccessToken} and {ClientSecret}."));
        var (sut, _, _) = CreateClient(handler);

        var action = () => sut.GetAdhesionAsync(JurisdictionId, "ppt-adh-transport");

        var exception = await action.Should().ThrowAsync<AutomaticDebitProviderException>();
        exception.Which.Message.Should().Be("PayPerTIC adhesion request failed.");
        exception.Which.ToString().Should().NotContain(AccessToken).And.NotContain(ClientSecret);
    }

    private static (
        AutomaticDebitPayPerTicClient Sut,
        Mock<IJurisdictionPayPerTicConfigurationProvider> Configuration,
        Mock<IPayPerTicJurisdictionTokenProvider> TokenProvider) CreateClient(
            HttpMessageHandler handler,
            ILogger<AutomaticDebitPayPerTicClient>? logger = null)
    {
        var configuration = new Mock<IJurisdictionPayPerTicConfigurationProvider>();
        configuration
            .Setup(x => x.GetActiveAsync(JurisdictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Configuration());
        var tokenProvider = new Mock<IPayPerTicJurisdictionTokenProvider>();
        tokenProvider
            .Setup(x => x.GetTokenAsync(JurisdictionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessToken);
        var sut = new AutomaticDebitPayPerTicClient(
            new HttpClient(handler),
            configuration.Object,
            tokenProvider.Object,
            logger ?? NullLogger<AutomaticDebitPayPerTicClient>.Instance);
        return (sut, configuration, tokenProvider);
    }

    private static AutomaticDebitProviderCreateRequest ProviderRequest() => new(
        "VGBIN85447",
        "6",
        "Inmueble",
        new AutomaticDebitProviderPayer(
            "Maria Perez",
            "maria@example.test",
            "MVGB318713",
            "DNI_ARG",
            "30111222",
            "ARG"));

    private static JurisdictionPayPerTicConfiguration Configuration() => new(
        JurisdictionId,
        "https://auth.example.test/token",
        "https://api.example.test/root/",
        "user-2419",
        "password-2419",
        "client-2419",
        ClientSecret,
        "collector-2419",
        "https://callbacks.example.test/automatic-debits",
        "https://portal.example.test/return",
        "https://portal.example.test/back");

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                body));
            return responseFactory(request);
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        string Uri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body);
}
