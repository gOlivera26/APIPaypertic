using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.Common;

namespace PagoTicAPI.Application.Services.Interfaces;

/// <summary>
/// Define los casos de uso para emitir, consultar, reconciliar y cancelar débitos automáticos.
/// </summary>
public interface IAutomaticDebitGenerationService
{
    /// <summary>Emite en PayPerTIC un débito para una obligación tributaria elegible.</summary>
    Task<OperationResponse<AutomaticDebitOperationDto>> CreateAsync(
        CreateAutomaticDebitRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reconsulta PayPerTIC y concilia el estado autoritativo de una operación.</summary>
    Task<OperationResponse<AutomaticDebitOperationDto>> ReconcileAsync(
        long operationId,
        CancellationToken cancellationToken = default);

    /// <summary>Obtiene una operación de débito automático por su identificador local.</summary>
    Task<OperationResponse<AutomaticDebitOperationDto>> GetAsync(
        long operationId,
        CancellationToken cancellationToken = default);

    /// <summary>Cancela en PayPerTIC una operación que todavía admite cancelación.</summary>
    Task<OperationResponse<AutomaticDebitOperationDto>> CancelAsync(
        long operationId,
        CancelAutomaticDebitRequest request,
        CancellationToken cancellationToken = default);
}
