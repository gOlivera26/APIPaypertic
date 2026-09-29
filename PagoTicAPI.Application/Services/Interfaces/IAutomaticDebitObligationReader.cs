namespace PagoTicAPI.Application.Services.Interfaces;

/// <summary>
/// Consulta una obligación tributaria requerida para generar un débito automático.
/// </summary>
public interface IAutomaticDebitObligationReader
{
    /// <summary>
    /// Busca una obligación tributaria por jurisdicción e identificador.
    /// </summary>
    Task<AutomaticDebitObligationSnapshot?> FindAsync(
        long jurisdictionId,
        string obligationId,
        CancellationToken cancellationToken = default);
}
