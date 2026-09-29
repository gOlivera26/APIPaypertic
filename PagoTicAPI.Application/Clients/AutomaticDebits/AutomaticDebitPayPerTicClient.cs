namespace PagoTicAPI.Application.Clients.AutomaticDebits;

public sealed class AutomaticDebitPayPerTicClient : IAutomaticDebitPayPerTicClient
{
    private const int MaximumProviderDiagnosticLength = 2048;
    private static readonly HashSet<string> ProviderDiagnosticProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "code",
        "extended_code",
        "error",
        "message",
        "description",
        "status",
        "status_detail"
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IJurisdictionPayPerTicConfigurationProvider _configurationProvider;
    private readonly IPayPerTicJurisdictionTokenProvider _tokenProvider;
    private readonly ILogger<AutomaticDebitPayPerTicClient> _logger;

    public AutomaticDebitPayPerTicClient(
        HttpClient httpClient,
        IJurisdictionPayPerTicConfigurationProvider configurationProvider,
        IPayPerTicJurisdictionTokenProvider tokenProvider,
        ILogger<AutomaticDebitPayPerTicClient> logger)
    {
        _httpClient = httpClient;
        _configurationProvider = configurationProvider;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<AutomaticDebitProviderAdhesion> CreateAdhesionAsync(
        long jurisdictionId,
        AutomaticDebitProviderCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        var payload = new CreateAdhesionPayload(
            "adhesion",
            configuration.CollectorId,
            "ARS",
            configuration.NotificationUrl,
            configuration.ReturnUrl,
            configuration.BackUrl,
            new AdhesionDetail(request.ExternalReference, request.ConceptId, request.ConceptDescription),
            new AdhesionPayer(
                request.Payer.Name,
                request.Payer.Email,
                request.Payer.ExternalReference,
                new PayerIdentification(
                    request.Payer.IdentificationType,
                    request.Payer.IdentificationNumber,
                    request.Payer.IdentificationCountry)));

        return await SendAsync(
            jurisdictionId,
            HttpMethod.Post,
            BuildUri(configuration.ApiUrl, "suscripciones"),
            payload,
            cancellationToken);
    }

    public async Task<AutomaticDebitProviderAdhesion> GetAdhesionAsync(
        long jurisdictionId,
        string providerAdhesionId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        return await SendAsync<object?>(
            jurisdictionId,
            HttpMethod.Get,
            BuildUri(configuration.ApiUrl, $"suscripciones/{Uri.EscapeDataString(providerAdhesionId)}"),
            null,
            cancellationToken);
    }

    public async Task<AutomaticDebitProviderAdhesion> CancelAdhesionAsync(
        long jurisdictionId,
        string providerAdhesionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        return await SendAsync(
            jurisdictionId,
            HttpMethod.Post,
            BuildUri(configuration.ApiUrl, $"suscripciones/cancelar/{Uri.EscapeDataString(providerAdhesionId)}"),
            new CancelAdhesionPayload(reason),
            cancellationToken);
    }

    public async Task<AutomaticDebitProviderPayment> CreateDebitAsync(
        long jurisdictionId,
        string providerAdhesionId,
        AutomaticDebitProviderPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        var payload = new CreateDebitPayload(
            "ARS",
            configuration.CollectorId,
            request.ExternalTransactionId,
            FormatProviderDate(request.DueDate),
            configuration.NotificationUrl,
            [new DebitDetail(request.ExternalReference, request.ConceptId, request.ConceptDescription, request.Amount)]);
        var token = await _tokenProvider.GetTokenAsync(jurisdictionId, cancellationToken);
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(configuration.ApiUrl, $"suscripciones/adhesion/{Uri.EscapeDataString(providerAdhesionId)}/pago"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = JsonContent.Create(payload, options: JsonOptions);

        try
        {
            using var response = await _httpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var diagnostic = await ReadProviderDiagnosticAsync(response, cancellationToken);
                _logger.LogWarning(
                    "La solicitud de débito a PayPerTIC falló para la jurisdicción {JurisdictionId} con estado {StatusCode}. Diagnóstico del proveedor: {ProviderDiagnostic}",
                    jurisdictionId, (int)response.StatusCode, diagnostic);
                throw new AutomaticDebitProviderException(
                    "PayPerTIC debit request failed.",
                    (int)response.StatusCode,
                    failureKind: AutomaticDebitProviderFailureKind.Uncertain);
            }

            var provider = await response.Content.ReadFromJsonAsync<ProviderPaymentResponse>(JsonOptions, cancellationToken);
            if (provider is null || string.IsNullOrWhiteSpace(provider.Id) || string.IsNullOrWhiteSpace(provider.Status))
            {
                throw new AutomaticDebitProviderException("PayPerTIC returned an invalid debit response.");
            }

            return new AutomaticDebitProviderPayment(provider.Id, provider.Status);
        }
        catch (AutomaticDebitProviderException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Falló el transporte de la solicitud de débito a PayPerTIC para la jurisdicción {JurisdictionId}.", jurisdictionId);
            throw new AutomaticDebitProviderException("PayPerTIC debit request failed.");
        }
    }

    public async Task<AutomaticDebitProviderPaymentMatch?> FindDebitByExternalIdAsync(
        long jurisdictionId,
        string externalTransactionId,
        DateTime requestedAt,
        string collectorId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collectorId))
            throw new AutomaticDebitProviderException("A historical collector is required for payment lookup.");
        var configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        var token = await _tokenProvider.GetTokenAsync(jurisdictionId, cancellationToken);
        // Include the entire request day and all subsequent days: a delayed provider write
        // must not disappear merely because reconciliation runs after midnight.
        var providerOffset = TimeSpan.FromHours(-3);
        var requestedProviderDate = new DateTimeOffset(DateTime.SpecifyKind(requestedAt, DateTimeKind.Utc))
            .ToOffset(providerOffset).Date;
        var start = new DateTimeOffset(requestedProviderDate, providerOffset);
        var end = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).Date.AddDays(1);
        var filters = new (string Field, string Operation, string Value)[]
        {
            ("external_transaction_id", "EQUAL", externalTransactionId),
            ("collector_id", "EQUAL", collectorId),
            ("request_date", "GREATER_THAN_OR_EQUAL_TO", FormatFilterDate(start)),
            ("request_date", "LESS_THAN", FormatFilterDate(new DateTimeOffset(end, TimeSpan.FromHours(-3))))
        };
        var parameters = new List<string> { "page=1", "limit=2" };
        for (var index = 0; index < filters.Length; index++)
        {
            var filter = filters[index];
            parameters.Add($"filters[{index}][field]={Uri.EscapeDataString(filter.Field)}");
            parameters.Add($"filters[{index}][operation]={Uri.EscapeDataString(filter.Operation)}");
            parameters.Add($"filters[{index}][value]={Uri.EscapeDataString(filter.Value)}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get,
            BuildUri(configuration.ApiUrl, $"pagos?{string.Join("&", parameters)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new AutomaticDebitProviderException("PayPerTIC payment lookup failed.", (int)response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("page", out var page) || page.GetInt32() != 1 ||
                !root.TryGetProperty("limit", out var limit) || limit.GetInt32() != 2 ||
                !root.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number)
                throw new AutomaticDebitProviderException("PayPerTIC returned an invalid payment lookup.");

            // size may mean total or page size depending on provider version. Either way,
            // a value above one cannot establish a unique match.
            if (rows.GetArrayLength() > 1 || size.GetInt32() > 1)
                throw new AutomaticDebitProviderException("PayPerTIC returned multiple payment matches.");
            if (rows.GetArrayLength() == 0)
            {
                if (size.GetInt32() != 0)
                    throw new AutomaticDebitProviderException("PayPerTIC returned inconsistent payment pagination.");
                return null;
            }
            if (size.GetInt32() != 1)
                throw new AutomaticDebitProviderException("PayPerTIC returned inconsistent payment pagination.");

            var row = rows[0];
            var collector = RequiredString(row, "collector_id");
            if (!string.Equals(collector, collectorId, StringComparison.Ordinal))
                throw new AutomaticDebitProviderException("PayPerTIC payment collector does not match the lookup.");
            if (!string.Equals(RequiredString(row, "external_transaction_id"), externalTransactionId, StringComparison.Ordinal))
                throw new AutomaticDebitProviderException("PayPerTIC payment external ID does not match the lookup.");
            return new AutomaticDebitProviderPaymentMatch(
                RequiredString(row, "id"), externalTransactionId, collector);
        }
        catch (AutomaticDebitProviderException) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new AutomaticDebitProviderException("PayPerTIC payment lookup failed.");
        }
    }

    private static string FormatFilterDate(DateTimeOffset value) =>
        value.ToString("yyyyMMdd'T'HHmmssfffzzz", CultureInfo.InvariantCulture).Remove(21, 1);

    public async Task<AutomaticDebitProviderReadBack> GetPaymentReadBackAsync(
        long jurisdictionId,
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        return await SendReadBackAsync(
            jurisdictionId,
            BuildUri(configuration.ApiUrl, $"pagos/{Uri.EscapeDataString(providerPaymentId)}"),
            AutomaticDebitWebhookObjectTypes.Payment,
            "external_transaction_id",
            cancellationToken);
    }

    public async Task CancelPaymentAsync(
        long jurisdictionId,
        string providerPaymentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        var token = await _tokenProvider.GetTokenAsync(jurisdictionId, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(configuration.ApiUrl, $"pagos/cancelar/{Uri.EscapeDataString(providerPaymentId)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CancelAdhesionPayload(reason), options: JsonOptions);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var diagnostic = await ReadProviderDiagnosticAsync(response, cancellationToken);
                _logger.LogWarning(
                    "La cancelación del pago en PayPerTIC falló para la jurisdicción {JurisdictionId} con estado {StatusCode}. Diagnóstico del proveedor: {ProviderDiagnostic}",
                    jurisdictionId,
                    (int)response.StatusCode,
                    diagnostic);
                throw new AutomaticDebitProviderException(
                    "PayPerTIC payment cancellation failed.",
                    (int)response.StatusCode);
            }
        }
        catch (AutomaticDebitProviderException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new AutomaticDebitProviderException("PayPerTIC payment cancellation failed.");
        }
    }

    public async Task<AutomaticDebitProviderReadBack> GetAdhesionReadBackAsync(
        long jurisdictionId,
        string providerAdhesionId,
        CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationProvider.GetActiveAsync(jurisdictionId, cancellationToken);
        return await SendReadBackAsync(
            jurisdictionId,
            BuildUri(configuration.ApiUrl, $"suscripciones/{Uri.EscapeDataString(providerAdhesionId)}"),
            AutomaticDebitWebhookObjectTypes.Subscription,
            "external_reference",
            cancellationToken);
    }

    private async Task<AutomaticDebitProviderAdhesion> SendAsync<TPayload>(
        long jurisdictionId,
        HttpMethod method,
        Uri uri,
        TPayload? payload,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(jurisdictionId, cancellationToken);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var providerDiagnostic = await ReadProviderDiagnosticAsync(response, cancellationToken);
                _logger.LogWarning(
                    "La solicitud de adhesión a PayPerTIC falló para la jurisdicción {JurisdictionId} con estado {StatusCode}. Diagnóstico del proveedor: {ProviderDiagnostic}",
                    jurisdictionId,
                    (int)response.StatusCode,
                    providerDiagnostic);
                throw new AutomaticDebitProviderException(
                    "PayPerTIC adhesion request failed.",
                    (int)response.StatusCode);
            }

            var providerResponse = await response.Content.ReadFromJsonAsync<ProviderAdhesionResponse>(
                JsonOptions,
                cancellationToken);
            if (providerResponse is null || string.IsNullOrWhiteSpace(providerResponse.Id) ||
                string.IsNullOrWhiteSpace(providerResponse.Status))
            {
                throw new AutomaticDebitProviderException("PayPerTIC returned an invalid adhesion response.");
            }

            return new AutomaticDebitProviderAdhesion(
                providerResponse.Id,
                providerResponse.FormUrl,
                providerResponse.Status);
        }
        catch (AutomaticDebitProviderException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Falló el transporte de la adhesión a PayPerTIC para la jurisdicción {JurisdictionId}.",
                jurisdictionId);
            throw new AutomaticDebitProviderException(
                "PayPerTIC adhesion request failed.");
        }
    }

    private async Task<AutomaticDebitProviderReadBack> SendReadBackAsync(
        long jurisdictionId,
        Uri uri,
        string objectType,
        string externalReferenceProperty,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(jurisdictionId, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "La consulta autoritativa del débito automático en PayPerTIC falló para la jurisdicción {JurisdictionId} con estado {StatusCode}.",
                    jurisdictionId,
                    (int)response.StatusCode);
                throw new AutomaticDebitProviderException(
                    "PayPerTIC automatic-debit read-back failed.",
                    (int)response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var id = RequiredString(root, "id");
            var status = RequiredString(root, "status");
            var collectorId = RequiredString(root, "collector_id");
            var externalReference = OptionalString(root, externalReferenceProperty);
            if (string.IsNullOrWhiteSpace(externalReference) && objectType == AutomaticDebitWebhookObjectTypes.Subscription &&
                root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
            {
                externalReference = OptionalString(detail, externalReferenceProperty);
            }

            if (string.IsNullOrWhiteSpace(externalReference))
            {
                throw new AutomaticDebitProviderException("PayPerTIC returned an invalid read-back response.");
            }

            DateTimeOffset? lastUpdateDate = null;
            var lastUpdateRaw = OptionalString(root, "last_update_date") ??
                                OptionalString(root, "last_updated_date");
            if (!string.IsNullOrWhiteSpace(lastUpdateRaw))
            {
                if (!DateTimeOffset.TryParse(
                        lastUpdateRaw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var parsed))
                {
                    throw new AutomaticDebitProviderException("PayPerTIC returned an invalid read-back response.");
                }

                lastUpdateDate = parsed;
            }

            JsonElement paymentDetail = default;
            if (objectType == AutomaticDebitWebhookObjectTypes.Payment &&
                root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() == 1 && details[0].ValueKind == JsonValueKind.Object)
                paymentDetail = details[0];

            decimal? detailAmount = null;
            if (paymentDetail.ValueKind == JsonValueKind.Object &&
                paymentDetail.TryGetProperty("amount", out var amount) &&
                amount.ValueKind == JsonValueKind.Number && amount.TryGetDecimal(out var parsedAmount))
                detailAmount = parsedAmount;

            return new AutomaticDebitProviderReadBack(
                objectType,
                id,
                status,
                collectorId,
                externalReference,
                lastUpdateDate)
            {
                PaymentType = objectType == AutomaticDebitWebhookObjectTypes.Payment ? OptionalString(root, "type") : null,
                PaymentCurrencyId = objectType == AutomaticDebitWebhookObjectTypes.Payment ? OptionalString(root, "currency_id") : null,
                DetailAmount = detailAmount,
                DetailExternalReference = paymentDetail.ValueKind == JsonValueKind.Object ? OptionalString(paymentDetail, "external_reference") : null,
                DetailConceptId = paymentDetail.ValueKind == JsonValueKind.Object ? OptionalString(paymentDetail, "concept_id") : null,
                DetailConceptDescription = paymentDetail.ValueKind == JsonValueKind.Object ? OptionalString(paymentDetail, "concept_description") : null
            };
        }
        catch (AutomaticDebitProviderException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Falló el transporte de la consulta autoritativa del débito automático en PayPerTIC para la jurisdicción {JurisdictionId}.",
                jurisdictionId);
            throw new AutomaticDebitProviderException("PayPerTIC automatic-debit read-back failed.");
        }
    }

    private static string RequiredString(JsonElement root, string propertyName)
    {
        var value = OptionalString(root, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new AutomaticDebitProviderException("PayPerTIC returned an invalid read-back response.")
            : value;
    }

    private static string? OptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<string> ReadProviderDiagnosticAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "<empty>";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var diagnostic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectProviderDiagnostic(document.RootElement, diagnostic);
            if (diagnostic.Count == 0)
            {
                return "<json-without-diagnostic-fields>";
            }

            var serialized = JsonSerializer.Serialize(diagnostic);
            return serialized.Length <= MaximumProviderDiagnosticLength
                ? serialized
                : serialized[..MaximumProviderDiagnosticLength];
        }
        catch (JsonException)
        {
            return "<non-json>";
        }
    }

    private static void CollectProviderDiagnostic(
        JsonElement element,
        IDictionary<string, string> diagnostic)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ProviderDiagnosticProperties.Contains(property.Name) &&
                    property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    diagnostic[property.Name] = property.Value.ToString();
                }
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    CollectProviderDiagnostic(property.Value, diagnostic);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectProviderDiagnostic(item, diagnostic);
            }
        }
    }

    private static Uri BuildUri(string apiUrl, string relativePath) =>
        new($"{apiUrl.TrimEnd('/')}/{relativePath}", UriKind.Absolute);

    private static string FormatProviderDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture).Remove(22, 1);

    private sealed record CreateAdhesionPayload(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("collector_id")] string CollectorId,
        [property: JsonPropertyName("currency_id")] string CurrencyId,
        [property: JsonPropertyName("notification_url")] string? NotificationUrl,
        [property: JsonPropertyName("return_url")] string? ReturnUrl,
        [property: JsonPropertyName("back_url")] string? BackUrl,
        [property: JsonPropertyName("detail")] AdhesionDetail Detail,
        [property: JsonPropertyName("payer")] AdhesionPayer Payer);

    private sealed record AdhesionDetail(
        [property: JsonPropertyName("external_reference")] string ExternalReference,
        [property: JsonPropertyName("concept_id")] string ConceptId,
        [property: JsonPropertyName("concept_description")] string ConceptDescription);

    private sealed record AdhesionPayer(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("external_reference")] string ExternalReference,
        [property: JsonPropertyName("identification")] PayerIdentification Identification);

    private sealed record PayerIdentification(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("number")] string Number,
        [property: JsonPropertyName("country")] string Country);

    private sealed record CancelAdhesionPayload(
        [property: JsonPropertyName("status_detail")] string StatusDetail);

    private sealed record CreateDebitPayload(
        [property: JsonPropertyName("currency_id")] string CurrencyId,
        [property: JsonPropertyName("collector_id")] string CollectorId,
        [property: JsonPropertyName("external_transaction_id")] string ExternalTransactionId,
        [property: JsonPropertyName("due_date")] string DueDate,
        [property: JsonPropertyName("notification_url")] string? NotificationUrl,
        [property: JsonPropertyName("details")] IReadOnlyList<DebitDetail> Details);

    private sealed record DebitDetail(
        [property: JsonPropertyName("external_reference")] string ExternalReference,
        [property: JsonPropertyName("concept_id")] string ConceptId,
        [property: JsonPropertyName("concept_description")] string ConceptDescription,
        [property: JsonPropertyName("amount")] decimal Amount);

    private sealed record ProviderPaymentResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("status")] string Status);

    private sealed record ProviderAdhesionResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("form_url")] string? FormUrl,
        [property: JsonPropertyName("status")] string Status);
}
