namespace PagoTicAPI.Application.Services.Interfaces;

/// <summary>
/// Lógica de negocio para la pasarela PayPerTIC.
/// Orquesta la creación de pagos en BD, la comunicación con PayPerTIC
/// y el procesamiento de webhooks de estado.
/// </summary>
public interface IPayPerTicService
{
    /// <summary>
    /// Inicia un checkout con PayPerTIC:
    ///   1. Persiste GtwPago + GtwPagoDetalle en BD con estado "pending".
    ///   2. Llama a PayPerTIC para crear el pago y obtener la form_url.
    ///   3. Actualiza PagIdExterno con el ID retornado por PayPerTIC.
    ///   4. Retorna la form_url para redirigir al pagador.
    /// </summary>
    Task<OperationResponse<PayPerTicCheckoutResultDto>> CrearCheckoutAsync(CreatePayPerTicCheckoutDto request);

    /// <summary>
    /// Procesa la notificación webhook enviada por PayPerTIC cuando cambia el estado de un pago.
    /// Busca el GtwPago por PagIdExterno y actualiza su estado, forma y método de pago.
    /// </summary>
    Task<OperationResponse<bool>> RecibirWebhookAsync(PayPerTicWebhookNotification notification);

    /// <summary>
    /// Cancela un pago pendiente tanto en PayPerTIC como actualizando el estado local en BD.
    /// Solo aplica a pagos con estado "pending".
    /// </summary>
    Task<OperationResponse<bool>> CancelarPagoAsync(string pagIdExterno, string motivo);

    /// <summary>
    /// Solicita la devolución de un pago aprobado a PayPerTIC y actualiza el estado local.
    /// </summary>
    Task<OperationResponse<PayPerTicRefundResponse>> DevolverPagoAsync(string pagIdExterno, PayPerTicRefundRequest request);
}
