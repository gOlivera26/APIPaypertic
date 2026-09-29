using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using PagoTicAPI.API.Controllers;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public sealed class AutomaticDebitEndpointAuthorizationTests
{
    [Fact]
    public void OperationsController_IsAnonymousForControlledTesting()
    {
        var controller = typeof(AutomaticDebitOperationsController);

        controller.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull();
    }
}
