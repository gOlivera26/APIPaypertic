using PagoTicAPI.Application.Services.Interfaces;

using PagoTicAPI.Application.Utilities;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public sealed class PayPerTicIdentificationMapperTests
{
    [Theory]
    [InlineData("20-12345678-9", "30111222", "CUIT_ARG", "20123456789")]
    [InlineData(" 27 12345678 5 ", "30111222", "CUIT_ARG", "27123456785")]
    public void Map_UsesCuitWhenCuilContainsElevenDigits(
        string cuil,
        string document,
        string expectedType,
        string expectedNumber)
    {
        PayPerTicIdentificationMapper.Map(cuil, document)
            .Should().Be(new AutomaticDebitIdentification(expectedType, expectedNumber, "ARG"));
    }

    [Theory]
    [InlineData(null, "30.111.222", "30111222")]
    [InlineData("invalid", "12 345 678", "12345678")]
    public void Map_FallsBackToDniWhenCuilIsNotElevenDigits(
        string? cuil,
        string document,
        string expectedNumber)
    {
        PayPerTicIdentificationMapper.Map(cuil, document)
            .Should().Be(new AutomaticDebitIdentification("DNI_ARG", expectedNumber, "ARG"));
    }

    [Fact]
    public void Map_RejectsMissingNumericIdentification()
    {
        var act = () => PayPerTicIdentificationMapper.Map(null, "not-a-document");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*identification*");
    }
}
