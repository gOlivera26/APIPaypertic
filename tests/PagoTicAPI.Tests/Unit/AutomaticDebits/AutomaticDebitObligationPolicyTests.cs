using PagoTicAPI.Application.Services.Interfaces;
using PagoTicAPI.Application.Services.Implementations;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public sealed class AutomaticDebitObligationPolicyTests
{
    [Fact]
    public void Evaluate_ValidSnapshot_IsEligible() =>
        AutomaticDebitObligationPolicy.Evaluate(Valid()).IsEligible.Should().BeTrue();

    [Theory]
    [InlineData("state")]
    [InlineData("situation")]
    [InlineData("installment")]
    [InlineData("partial")]
    [InlineData("amount")]
    [InlineData("deactivated")]
    public void Evaluate_ConservativeExclusion_IsRejected(string variation)
    {
        var source = Valid();
        var snapshot = variation switch
        {
            "state" => source with { DebtState = "PA" },
            "situation" => source with { DebtSituation = "JU" },
            "installment" => source with { InstallmentNumber = "000" },
            "partial" => source with { PartiallyPaidAmount = 1m },
            "amount" => source with { Amount = 0m },
            _ => source with { IsDeactivated = true }
        };

        AutomaticDebitObligationPolicy.Evaluate(snapshot).IsEligible.Should().BeFalse();
    }

    private static AutomaticDebitObligationSnapshot Valid() => new(
        "6012353270", 2419, "VGBIN88256", "6", "Tasa", "012", "2026",
        new DateTime(2026, 12, 15), "PP", "DN", false, 36469.09m, 0m);
}
