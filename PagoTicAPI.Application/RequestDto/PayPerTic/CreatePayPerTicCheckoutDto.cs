using System.ComponentModel.DataAnnotations;

namespace PagoTicAPI.Application.RequestDto.PayPerTic;

/// <summary>
/// Request que recibe nuestra API para iniciar un checkout con PayPerTIC.
/// El frontend/cliente envía este DTO y nosotros lo mapeamos al request de PayPerTIC.
/// </summary>
public class CreatePayPerTicCheckoutDto
{
    /// <summary>
    /// ID de transacción único generado por el sistema propio.
    /// Si no se envía, se genera automáticamente con timestamp.
    /// </summary>
    [StringLength(100)]
    public string? ExternalTransactionId { get; set; }

    /// <summary>Código de moneda ISO 4217. Por defecto "ARS".</summary>
    [Required]
    [RegularExpression("^[A-Z]{3}$")]
    public string CurrencyId { get; set; } = "ARS";

    /// <summary>Líneas de obligaciones/conceptos a pagar.</summary>
    [Required]
    [MinLength(1)]
    public List<CheckoutDetailItemDto> Details { get; set; } = new();

    /// <summary>Datos del pagador.</summary>
    [Required]
    public CheckoutPayerDto Payer { get; set; } = new();

    /// <summary>
    /// Fecha de vencimiento del pago. Si no se envía, no tiene vencimiento.
    /// Formato: yyyy-MM-ddTHH:mm:sszzz
    /// </summary>
    [StringLength(40)]
    public string? DueDate { get; set; }

    /// <summary>Tipo de pago: "online", "debit", "transfer", "coupon". Null = todos los medios.</summary>
    [RegularExpression("^(online|debit|transfer|debin|coupon)$")]
    public string? Type { get; set; }

    /// <summary>
    /// Collector ID a usar. Si no se envía, se usa el configurado en PayPerTicKeys.
    /// </summary>
    [StringLength(100)]
    public string? CollectorId { get; set; }

    /// <summary>
    /// URL del webhook donde PayPerTIC notificará el estado del pago.
    /// Si no se envía, se usa el configurado en PayPerTicKeys.NotificationUrl.
    /// </summary>
    [Url]
    [StringLength(2048)]
    public string? NotificationUrl { get; set; }

    /// <summary>ID de jurisdicción del pago en el sistema local.</summary>
    [Range(1, int.MaxValue)]
    public int JurisdiccionId { get; set; }

    /// <summary>Datos adicionales que se guardan en GtwPago (modo de pago, origen, etc).</summary>
    [Required]
    [StringLength(30)]
    [RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Origen { get; set; } = "web";
}

/// <summary>
/// Línea de detalle de un pago: representa una obligación tributaria.
/// </summary>
public class CheckoutDetailItemDto
{
    /// <summary>Importe total de esta línea.</summary>
    [Range(0.01, 999999999999.99)]
    public decimal Amount { get; set; }

    /// <summary>Código del concepto en PayPerTIC (ej. "0001").</summary>
    [Required]
    [StringLength(80)]
    public string ConceptId { get; set; } = string.Empty;

    /// <summary>Descripción legible del concepto (ej. "Tasa de Seguridad e Higiene").</summary>
    [Required]
    [StringLength(200)]
    public string ConceptDescription { get; set; } = string.Empty;

    // ── Campos para guardar en GtwPagoDetalle ──────────────────────────────────

    [StringLength(100)]
    public string? ExternalReference { get; set; }
    [StringLength(100)]
    public string? IdObligacion { get; set; }
    [StringLength(100)]
    public string? IdTributoContribuyente { get; set; }
    [StringLength(100)]
    public string? ClaveBien { get; set; }
    [StringLength(100)]
    public string? Contribuyente { get; set; }
    public int AnoCuota { get; set; }
    public int NroCuota { get; set; }
    public decimal CapitalFacturado { get; set; }
    public decimal Intereses { get; set; }
    public decimal DeudaActualizada { get; set; }
    [StringLength(20)]
    public string? TipoCuota { get; set; }
    [StringLength(20)]
    public string? TipoTributo { get; set; }
}

/// <summary>
/// Datos del pagador para el checkout.
/// </summary>
public class CheckoutPayerDto
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;
    [Required]
    [EmailAddress]
    [StringLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Referencia propia del pagador (ej. DNI, CUIT, código de contribuyente).</summary>
    [StringLength(100)]
    public string? ExternalReference { get; set; }

    /// <summary>Tipo de documento: "DNI_ARG" o "CUIT_ARG".</summary>
    [Required]
    [RegularExpression("^(DNI_ARG|CUIT_ARG)$")]
    public string IdentificationType { get; set; } = "DNI_ARG";

    /// <summary>Número de documento sin espacios ni guiones.</summary>
    [Required]
    [RegularExpression("^[0-9]{7,11}$")]
    public string IdentificationNumber { get; set; } = string.Empty;

    /// <summary>Código ISO 3166-1 alpha-3 del país. Por defecto "ARG".</summary>
    [Required]
    [RegularExpression("^[A-Z]{3}$")]
    public string IdentificationCountry { get; set; } = "ARG";
}
