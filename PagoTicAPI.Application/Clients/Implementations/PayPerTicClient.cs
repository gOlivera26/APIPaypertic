using PagoTicAPI.Application.Clients.Config;
using PagoTicAPI.Application.Clients.Interfaces;
using PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PagoTicAPI.Application.Clients.Implementations;

/// <summary>
/// Implementación del cliente HTTP para la API de pagos de PayPerTIC.
///
/// Responsabilidades:
///   - Obtener el Bearer token llamando a IPayPerTicAuthClient antes de cada request.
///   - Serializar/deserializar los DTOs de PayPerTIC.
///   - Propagar errores HTTP con mensajes claros que incluyan el body de la respuesta.
/// </summary>
public class PayPerTicClient : IPayPerTicClient
{
    private readonly HttpClient _httpClient;
    private readonly IPayPerTicAuthClient _authClient;
    private readonly PayPerTicKeys _keys;
    private readonly ILogger<PayPerTicClient> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public PayPerTicClient(
        HttpClient httpClient,
        IPayPerTicAuthClient authClient,
        IOptions<PayPerTicKeys> keys,
        ILogger<PayPerTicClient> logger)
    {
        _httpClient = httpClient;
        _authClient = authClient;
        _keys = keys.Value;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Crear pago
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PayPerTicCreatePaymentResponse> CreatePaymentAsync(PayPerTicCreatePaymentRequest request)
    {
        const string endpoint = "pagos";

        _logger.LogInformation(
            "PayPerTIC: creando pago. ExternalTransactionId={ExternalTransactionId}",
            request.ExternalTransactionId);

        var response = await SendAsync(HttpMethod.Post, endpoint, request);
        var result = await DeserializeAsync<PayPerTicCreatePaymentResponse>(response, endpoint);

        _logger.LogInformation(
            "PayPerTIC: pago creado. Id={PaymentId} Status={Status} FormUrl={FormUrl}",
            result.Id, result.Status, result.FormUrl);

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cancelar pago
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task CancelPaymentAsync(string paymentId, PayPerTicCancelRequest request)
    {
        var endpoint = $"pagos/cancelar/{paymentId}";

        _logger.LogInformation("PayPerTIC: cancelando pago. PaymentId={PaymentId}", paymentId);

        var response = await SendAsync(HttpMethod.Post, endpoint, request);

        // La API devuelve 200/204 sin body en cancelaciones exitosas.
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"PayPerTIC: error al cancelar el pago '{paymentId}'. " +
                $"HTTP {(int)response.StatusCode} - {body}");
        }

        _logger.LogInformation("PayPerTIC: pago cancelado. PaymentId={PaymentId}", paymentId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Devolver pago (refund)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PayPerTicRefundResponse> RefundPaymentAsync(string paymentId, PayPerTicRefundRequest request)
    {
        var endpoint = $"pagos/devolucion/{paymentId}";

        _logger.LogInformation("PayPerTIC: solicitando devolución. PaymentId={PaymentId}", paymentId);

        var response = await SendAsync(HttpMethod.Post, endpoint, request);
        var result = await DeserializeAsync<PayPerTicRefundResponse>(response, endpoint);

        _logger.LogInformation(
            "PayPerTIC: devolución procesada. RefundId={RefundId} Status={Status}",
            result.Id, result.Status);

        return result;
    }

    /// <summary>
    /// Agrega el Bearer token al header, serializa el body y ejecuta el request HTTP.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync<TBody>(
        HttpMethod method, string relativeUrl, TBody body)
    {
        var token = await _authClient.GetTokenAsync();

        var json = JsonSerializer.Serialize(body, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(method, $"{_keys.ApiUrl.TrimEnd('/')}/{relativeUrl}")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        _logger.LogDebug("PayPerTIC → {Method} {Url}", method, request.RequestUri);

        return await _httpClient.SendAsync(request);
    }

    /// <summary>
    /// Lee el body de la respuesta y lo deserializa al tipo indicado.
    /// Lanza excepción descriptiva si el status HTTP indica error.
    /// </summary>
    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, string endpoint)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"PayPerTIC [{endpoint}]: HTTP {(int)response.StatusCode} - {body}");

        var result = JsonSerializer.Deserialize<T>(body, _jsonOptions);

        return result ?? throw new InvalidOperationException(
            $"PayPerTIC [{endpoint}]: la respuesta no pudo deserializarse. Body={body}");
    }
}
