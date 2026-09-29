using PagoTicAPI.Application.RequestDto.OperationalAudit;
using PagoTicAPI.Application.ResponseDto.OperationalAudit;

namespace PagoTicAPI.Application.Services.Interfaces;

/// <summary>
/// Define consultas paginadas de auditoría y monitoreo operativo.
/// </summary>
public interface IOperationalAuditService
{
    /// <summary>Lista débitos automáticos con filtros operativos y paginación.</summary>
    Task<OperationResponse<PagedAuditResult<AutomaticDebitAuditItem>>> GetAutomaticDebitsAsync(
        AutomaticDebitAuditFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Obtiene el detalle operativo de un débito automático.</summary>
    Task<OperationResponse<AutomaticDebitAuditItem>> GetAutomaticDebitAsync(
        long id,
        CancellationToken cancellationToken);

    /// <summary>Obtiene el historial paginado de eventos y notificaciones de un débito.</summary>
    Task<OperationResponse<PagedAuditResult<OperationalAuditEvent>>> GetAutomaticDebitHistoryAsync(
        long id,
        AuditHistoryFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Lista checkouts de pago con filtros operativos y paginación.</summary>
    Task<OperationResponse<PagedAuditResult<CheckoutAuditItem>>> GetCheckoutsAsync(
        CheckoutAuditFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Obtiene el detalle de un checkout y sus conceptos tributarios.</summary>
    Task<OperationResponse<CheckoutAuditDetail>> GetCheckoutAsync(
        int id,
        CancellationToken cancellationToken);

    /// <summary>Obtiene el historial paginado de notificaciones de un checkout.</summary>
    Task<OperationResponse<PagedAuditResult<OperationalAuditEvent>>> GetCheckoutHistoryAsync(
        int id,
        AuditHistoryFilter filter,
        CancellationToken cancellationToken);

    /// <summary>Genera indicadores agregados de operación para el período solicitado.</summary>
    Task<OperationResponse<OperationalAuditSummary>> GetSummaryAsync(
        OperationalSummaryFilter filter,
        CancellationToken cancellationToken);
}
