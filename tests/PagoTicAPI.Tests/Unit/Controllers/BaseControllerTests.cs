using PagoTicAPI.API.Controllers;

namespace PagoTicAPI.Tests.Unit.Controllers;

public sealed class BaseControllerTests
{
    [Theory]
    [InlineData(StatusCodes.Status200OK)]
    [InlineData(StatusCodes.Status201Created)]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public void Return_MapsKnownStatusCodeAndPreservesOperationResponse(int statusCode)
    {
        var response = CreateResponse(statusCode);
        var controller = new TestBaseController();

        var result = controller.Map(response);

        var objectResult = result.Should().BeAssignableTo<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(statusCode);
        objectResult.Value.Should().BeSameAs(response);
    }

    [Fact]
    public void Return_WhenCodeIsForbidden_ReturnsBodyWithoutUsingMessageAsAuthenticationScheme()
    {
        var response = CreateResponse(StatusCodes.Status403Forbidden, "not-an-authentication-scheme");
        var controller = new TestBaseController();

        var result = controller.Map(response);

        result.Should().BeOfType<ObjectResult>();
        result.Should().NotBeOfType<ForbidResult>();

        var objectResult = (ObjectResult)result;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        objectResult.Value.Should().BeSameAs(response);
    }

    private static OperationResponse<string> CreateResponse(int statusCode, string message = "test-message") =>
        OperationResponse<string>.CreateBuilder()
            .WithSuccess(statusCode is >= 200 and < 300)
            .WithMessage(message)
            .WithData("test-data")
            .WithCode(statusCode)
            .Build();

    private sealed class TestBaseController : BaseController
    {
        public IActionResult Map<T>(OperationResponse<T> response) => Return(response);
    }
}
