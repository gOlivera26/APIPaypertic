namespace PagoTicAPI.Application.ResponseDto.PayPerTic;

/// <summary>
/// Resultado que nuestra API devuelve al frontend luego de iniciar un checkout con PayPerTIC.
/// El frontend debe redirigir al usuario a <see cref="FormUrl"/> para completar el pago.
/// </summary>
public class PayPerTicCheckoutResultDto
{
    /// <summary>Indica si el checkout fue creado exitosamente en PayPerTIC.</summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// ID del pago asignado por PayPerTIC (UUID).
    /// Equivale a GtwPago.PagIdExterno. Sirve para consultas posteriores.
    /// </summary>
    public string? PaymentId { get; set; }

    /// <summary>
    /// URL del formulario de pago hosted de PayPerTIC.
    /// El frontend debe redirigir al usuario aquí para que complete el pago.
    /// </summary>
    public string? FormUrl { get; set; }

    /// <summary>Importe total confirmado por PayPerTIC.</summary>
    public decimal? FinalAmount { get; set; }

    /// <summary>Estado inicial del pago. Normalmente "pending".</summary>
    public string? Status { get; set; }

    /// <summary>El ID de transacción propio que se envió a PayPerTIC.</summary>
    public string? ExternalTransactionId { get; set; }

    /// <summary>ID del pago en el sistema local (GtwPago.PagId).</summary>
    public int? LocalPagId { get; set; }

    /// <summary>Mensaje de error en caso de fallo.</summary>
    public string? ErrorMessage { get; set; }
}
