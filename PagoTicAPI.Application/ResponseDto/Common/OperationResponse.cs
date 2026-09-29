namespace PagoTicAPI.Application.ResponseDto.Common;

public class OperationResponse<T>
{
    public bool? Success { get; }

    public string? Message { get; }

    public T? Data { get; }

    public int? Code { get; }

    public int? TotalRows { get; }

    public string? Exception { get; }

    public object? ExceptionDetails { get; }


    private OperationResponse(OperationResponseBuilder builder)
    {
        Success = builder.Success;
        Message = builder.Message;
        Data = builder.Data;
        Code = builder.Code;
        TotalRows = builder.TotalRows;
        Exception = builder.Exception;
        ExceptionDetails = builder.ExceptionDetails;
    }

    public class OperationResponseBuilder
    {
        public bool? Success { get; private set; }

        public string? Message { get; private set; }

        public T? Data { get; private set; }

        public int? Code { get; private set; }

        public int? TotalRows { get; private set; }

        public string? Exception { get; private set; }

        public object? ExceptionDetails { get; private set; }


        public OperationResponseBuilder WithSuccess(bool success)
        {
            Success = success;
            return this;
        }

        public OperationResponseBuilder WithMessage(string message)
        {
            Message = message;
            return this;
        }

        public OperationResponseBuilder WithData(T data)
        {
            Data = data;
            return this;
        }

        public OperationResponseBuilder WithCode(int code)
        {
            Code = code;
            return this;
        }

        public OperationResponseBuilder WithTotalRows(int totalRows)
        {
            TotalRows = totalRows;
            return this;
        }

        public OperationResponseBuilder WithException(string exception, object? exceptionDetails = null)
        {
            Exception = exception;
            ExceptionDetails = exceptionDetails;
            return this;
        }

        public OperationResponse<T> Build() => new OperationResponse<T>(this);
    }

    public static OperationResponseBuilder CreateBuilder() => new OperationResponseBuilder();

    public static OperationResponse<T> SuccessResponse(T data, int totalRows = 0) => CreateBuilder()
        .WithSuccess(true).WithMessage(AppMessages.MsjSuccess).WithData(data).WithCode(200).WithTotalRows(totalRows)
        .Build();

    public static OperationResponse<T> SuccessResponseMassive(T data, int totalRows = 0) => CreateBuilder()
        .WithSuccess(true).WithMessage(AppMessages.MsjSuccessMasive).WithData(data).WithCode(200).WithTotalRows(totalRows)
        .Build();

    public static OperationResponse<T> ErrorFileResponse(string exception, object exceptionDetails) => CreateBuilder().WithSuccess(false)
        .WithMessage(AppMessages.MsjErrorFile).WithCode(500).WithException(exception, exceptionDetails).Build();

    public static OperationResponse<T> ErrorResponse(string exception, object exceptionDetails = null) => CreateBuilder().WithSuccess(false)
        .WithMessage(AppMessages.MsjError).WithCode(500).WithException(exception, exceptionDetails).Build();

    public static OperationResponse<T> NotFoundResponse() => CreateBuilder().WithSuccess(false)
        .WithMessage(AppMessages.MsjNotFound).WithCode(404).Build();

    public static OperationResponse<T> CustomErrorResponse(int code, string message, T data = default) =>
        CreateBuilder().WithSuccess(false).WithMessage(message).WithData(data).WithCode(code).Build();

    public static OperationResponse<T> BadRequestResponse(string message) =>
        CreateBuilder()
        .WithSuccess(false)
        .WithMessage(message)
        .WithCode(400)
        .Build();

    public static OperationResponse<T> UnauthorizedResponse(string message = "No autorizado") =>
        CreateBuilder()
        .WithSuccess(false)
        .WithMessage(message)
        .WithCode(401)
        .Build();

    public override string ToString() => JsonSerializer.Serialize(this);
}
