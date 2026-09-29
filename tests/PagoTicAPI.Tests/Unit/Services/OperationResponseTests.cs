namespace PagoTicAPI.Tests.Unit.Services;

public class OperationResponseTests
{
    [Fact]
    public void SuccessResponse_ShouldReturnCorrectStatusCode()
    {
        var data = new { Id = 1, Nombre = "Test" };
        
        var response = OperationResponse<object>.SuccessResponse(data);

        response.Success.Should().BeTrue();
        response.Code.Should().Be(200);
        response.Message.Should().NotBeNull();
        response.Data.Should().NotBeNull();
    }

    [Fact]
    public void SuccessResponse_WithZeroData_ShouldReturnCorrectTotalRows()
    {
        var data = new { Id = 1, Nombre = "Test" };

        var response = OperationResponse<object>.SuccessResponse(data, 0);

        response.TotalRows.Should().Be(0);
    }

    [Fact]
    public void NotFoundResponse_ShouldReturn404()
    {
        var response = OperationResponse<object>.NotFoundResponse();

        response.Success.Should().BeFalse();
        response.Code.Should().Be(404);
    }

    [Fact]
    public void ErrorResponse_ShouldReturn500()
    {
        var response = OperationResponse<object>.ErrorResponse("Test error");

        response.Success.Should().BeFalse();
        response.Code.Should().Be(500);
        response.Exception.Should().Be("Test error");
    }

    [Fact]
    public void BadRequestResponse_ShouldReturn400()
    {
        var response = OperationResponse<object>.BadRequestResponse("Invalid input");

        response.Success.Should().BeFalse();
        response.Code.Should().Be(400);
        response.Message.Should().Be("Invalid input");
    }

    [Theory]
    [InlineData(400, "Bad request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    public void CustomErrorResponse_WithDifferentCodes_ShouldReturnCorrectCode(int code, string message)
    {
        var response = OperationResponse<object>.CustomErrorResponse(code, message);

        response.Code.Should().Be(code);
        response.Message.Should().Be(message);
        response.Success.Should().BeFalse();
    }
}
