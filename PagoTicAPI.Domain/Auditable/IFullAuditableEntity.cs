namespace PagoTicAPI.Domain.Auditable;

/// <summary>
/// Extiende la auditoría con información de baja lógica.
/// </summary>
public interface IFullAuditableEntity : IAuditableEntity
{
    bool IsDeleted { get; set; }
    string? EliminadoPor { get; set; }
    DateTime? EliminadoEl { get; set; }
    string? MotivoBaja { get; set; }
}
