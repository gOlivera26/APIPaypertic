using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.Common;

namespace PagoTicAPI.Application.Services.Interfaces;

/// <summary>
/// Define los casos de uso para crear, consultar y cancelar adhesiones.
/// </summary>
public interface IAutomaticDebitAdhesionService
{
    /// <summary>Crea una adhesión en PayPerTIC para una cuenta tributaria y registra su estado local.</summary>
    Task<OperationResponse<AutomaticDebitAdhesionDto>> CreateAsync(
        CreateAutomaticDebitAdhesionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Obtiene la adhesión vigente de una cuenta y sincroniza su estado cuando corresponde.</summary>
    Task<OperationResponse<AutomaticDebitAdhesionDto>> GetAsync(
        long jurisdictionId,
        string taxpayerAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>Cancela la adhesión vigente en PayPerTIC y registra el resultado localmente.</summary>
    Task<OperationResponse<AutomaticDebitAdhesionDto>> CancelAsync(
        long jurisdictionId,
        string taxpayerAccountId,
        CancelAutomaticDebitAdhesionRequest request,
        CancellationToken cancellationToken = default);
}
