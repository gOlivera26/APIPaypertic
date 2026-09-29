using System.Text.Json.Serialization;

namespace PagoTicAPI.Application.Clients.PayPerTic.Contracts.Payments;

/// <summary>
/// Línea de detalle dentro de un pago enviado a PayPerTIC.
/// Un pago puede tener múltiples líneas (ej. varias obligaciones tributarias).
/// </summary>
public class PayPerTicPaymentDetail
{
    /// <summary>Importe de esta línea (formato ##.00).</summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>Código del concepto definido en PayPerTIC.</summary>
    [JsonPropertyName("concept_id")]
    public string ConceptId { get; set; } = string.Empty;

    /// <summary>Descripción del concepto (ej. "Tasa de Seguridad e Higiene").</summary>
    [JsonPropertyName("concept_description")]
    public string ConceptDescription { get; set; } = string.Empty;

    /// <summary>Referencia propia del sistema para esta línea.</summary>
    [JsonPropertyName("external_reference")]
    public string? ExternalReference { get; set; }

    /// <summary>Identificador del recaudador para esta línea (si difiere del principal).</summary>
    [JsonPropertyName("collector_id")]
    public string? CollectorId { get; set; }
}
