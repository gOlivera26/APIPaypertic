namespace PagoTicAPI.Application.Configuration.AutomaticDebits;

public sealed class PayPerTicTokenAcquirer : IPayPerTicTokenAcquirer
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PayPerTicTokenAcquirer> _logger;

    public PayPerTicTokenAcquirer(
        HttpClient httpClient,
        ILogger<PayPerTicTokenAcquirer> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PayPerTicAccessToken> AcquireAsync(
        JurisdictionPayPerTicConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = configuration.Username,
            ["password"] = configuration.Password,
            ["grant_type"] = "password",
            ["client_id"] = configuration.ClientId,
            ["client_secret"] = configuration.ClientSecret
        });

        try
        {
            using var response = await _httpClient.PostAsync(
                configuration.AuthUrl,
                content,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Falló la obtención del token de PayPerTIC para la jurisdicción {JurisdictionId} con estado {StatusCode}.",
                    configuration.JurisdictionId,
                    (int)response.StatusCode);
                throw new AutomaticDebitTokenAcquisitionException(
                    "PayPerTIC token acquisition failed.",
                    (int)response.StatusCode);
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken: cancellationToken);
            if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken) || payload.ExpiresIn <= 0)
            {
                throw new AutomaticDebitTokenAcquisitionException(
                    "PayPerTIC returned an invalid token response.");
            }

            return new PayPerTicAccessToken(payload.AccessToken, payload.ExpiresIn);
        }
        catch (AutomaticDebitTokenAcquisitionException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Falló el transporte para obtener el token de PayPerTIC de la jurisdicción {JurisdictionId}.",
                configuration.JurisdictionId);
            throw new AutomaticDebitTokenAcquisitionException(
                "PayPerTIC token acquisition failed.");
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
