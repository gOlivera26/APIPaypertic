using PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

namespace PagoTicAPI.Application.Clients.Interfaces;

/// <summary>
/// Cliente HTTP para los endpoints de pagos de PayPerTIC.
/// Todos los métodos obtienen el Bearer token automáticamente vía IPayPerTicAuthClient.
/// </summary>
public interface IPayPerTicClient
{
    /// <summary>
    /// Crea un nuevo pago en PayPerTIC.
    /// Endpoint: POST /pagos
    /// Retorna el ID del pago y la form_url para redirigir al pagador.
    /// </summary>
    Task<PayPerTicCreatePaymentResponse> CreatePaymentAsync(PayPerTicCreatePaymentRequest request);

    /// <summary>
    /// Cancela un pago existente. Solo aplica a pagos en estado "pending" o "issued".
    /// Endpoint: POST /pagos/cancelar/{paymentId}
    /// </summary>
    Task CancelPaymentAsync(string paymentId, PayPerTicCancelRequest request);

    /// <summary>
    /// Solicita una devolución (refund) de un pago aprobado.
    /// Endpoint: POST /pagos/devolucion/{paymentId}
    /// </summary>
    Task<PayPerTicRefundResponse> RefundPaymentAsync(string paymentId, PayPerTicRefundRequest request);
}
