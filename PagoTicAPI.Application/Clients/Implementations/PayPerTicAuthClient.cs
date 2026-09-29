using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PagoTicAPI.Application.Clients.Config;
using PagoTicAPI.Application.Clients.Interfaces;
using PagoTicAPI.Application.Clients.PayPerTic.Contracts.Auth;
using System.Text.Json;

namespace PagoTicAPI.Application.Clients.Implementations;

/// <summary>
/// Implementación del cliente de autenticación OAuth2 de PayPerTIC.
///
/// Flujo:
///   1. Se llama a GetTokenAsync().
///   2. Si existe un token cacheado con margen de seguridad → se retorna directamente.
///   3. Si no hay token o está por vencer → se hace POST al endpoint OAuth2 con
///      grant_type=password y se guarda el nuevo token en caché.
///
/// El token se cachea por (expires_in - SAFETY_MARGIN_SECONDS) para renovarlo
/// antes de que expire, evitando llamadas fallidas por token vencido.
/// </summary>
public class PayPerTicAuthClient : IPayPerTicAuthClient
{
    // Clave bajo la cual se guarda el token en IMemoryCache.
    private const string TOKEN_CACHE_KEY = "PayPerTic_BearerToken";

    // Segundos de margen antes del vencimiento real para renovar anticipadamente.
    private const int SAFETY_MARGIN_SECONDS = 30;

    private readonly HttpClient _httpClient;
    private readonly PayPerTicKeys _keys;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PayPerTicAuthClient> _logger;

    public PayPerTicAuthClient(
        HttpClient httpClient,
        IOptions<PayPerTicKeys> keys,
        IMemoryCache cache,
        ILogger<PayPerTicAuthClient> logger)
    {
        _httpClient = httpClient;
        _keys = keys.Value;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> GetTokenAsync()
    {
        // 1. Intentar obtener del caché
        if (_cache.TryGetValue(TOKEN_CACHE_KEY, out string? cachedToken) && !string.IsNullOrEmpty(cachedToken))
        {
            _logger.LogDebug("PayPerTIC: token obtenido desde caché.");
            return cachedToken;
        }

        // 2. Solicitar nuevo token
        _logger.LogInformation("PayPerTIC: solicitando nuevo token OAuth2...");

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username",      _keys.Username),
            new KeyValuePair<string, string>("password",      _keys.Password),
            new KeyValuePair<string, string>("grant_type",    "password"),
            new KeyValuePair<string, string>("client_id",     _keys.ClientId),
            new KeyValuePair<string, string>("client_secret", _keys.ClientSecret),
        });

        var response = await _httpClient.PostAsync(_keys.AuthUrl, formContent);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            _logger.LogError("PayPerTIC: error al obtener token. StatusCode={StatusCode} Body={Body}",
                response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"PayPerTIC Auth falló con HTTP {(int)response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<PayPerTicTokenResponse>(json)
            ?? throw new InvalidOperationException("PayPerTIC: la respuesta del token no pudo deserializarse.");

        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException("PayPerTIC: el access_token recibido está vacío.");

        // 3. Guardar en caché con margen de seguridad
        var cacheDuration = TimeSpan.FromSeconds(
            Math.Max(tokenResponse.ExpiresIn - SAFETY_MARGIN_SECONDS, 10));

        _cache.Set(TOKEN_CACHE_KEY, tokenResponse.AccessToken,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = cacheDuration });

        _logger.LogInformation(
            "PayPerTIC: token obtenido y cacheado por {Segundos}s (expires_in={ExpiresIn}s).",
            cacheDuration.TotalSeconds, tokenResponse.ExpiresIn);

        return tokenResponse.AccessToken;
    }
}
