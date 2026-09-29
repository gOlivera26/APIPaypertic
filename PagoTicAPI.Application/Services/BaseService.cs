namespace PagoTicAPI.Application.Services;

/// <summary>
/// Utilidades comunes para servicios de aplicación.
/// </summary>
public abstract class BaseService
{
    protected static OperationResponse<T> BadRequest<T>(string message, T? data = default)
        => OperationResponse<T>.CustomErrorResponse(400, message, data);

    protected static OperationResponse<T> NotFound<T>()
        => OperationResponse<T>.NotFoundResponse();

    protected static OperationResponse<T> InternalServerError<T>(string exception)
        => OperationResponse<T>.ErrorResponse(exception);

    protected static async Task<OperationResponse<bool>> InsertWithTransactionAsync(
        Func<Task> insertActionsAsync,
        DbContext context)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            await insertActionsAsync();
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return OperationResponse<bool>.SuccessResponse(true);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return OperationResponse<bool>.ErrorResponse(ex.Message);
        }
    }
}
