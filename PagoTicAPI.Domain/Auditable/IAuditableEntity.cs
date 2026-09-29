namespace PagoTicAPI.Domain.Auditable;

/// <summary>
/// Define los datos mínimos de auditoría de creación y modificación.
/// </summary>
public interface IAuditableEntity
{
    string CreadoPor { get; set; }
    DateTime CreadoEl { get; set; }
    string? ModificadoPor { get; set; }
    DateTime? ModificadoEl { get; set; }
}
