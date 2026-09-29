using System.Text.Json;
using PagoTicAPI.Application.RequestDto.AutomaticDebits;
using PagoTicAPI.Application.ResponseDto.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public sealed class AutomaticDebitApiDateTimeTests
{
    [Fact]
    public void FromUtc_ConvertsTechnicalDateToArgentinaOffset()
    {
        var utc = new DateTime(2026, 9, 18, 15, 30, 33, DateTimeKind.Unspecified);

        var result = AutomaticDebitApiDateTime.FromUtc(utc);

        result.Should().Be(new DateTimeOffset(2026, 9, 18, 12, 30, 33, TimeSpan.FromHours(-3)));
        result.Offset.Should().Be(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void OperationDto_SerializesTechnicalDateWithOffsetWithoutChangingCivilDueDate()
    {
        var dueDate = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Unspecified);
        var dto = new AutomaticDebitOperationDto(
            5, 2419, "VGBIN84782", "6012144120", "provider-id", "external-id",
            36077.65m, dueDate, "ISSUED", "PENDING",
            AutomaticDebitApiDateTime.FromUtc(new DateTime(2026, 9, 18, 15, 30, 33)));

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("requestedAt").GetString()
            .Should().EndWith("-03:00");
        document.RootElement.GetProperty("dueDate").GetString()
            .Should().Be("2026-10-14T00:00:00");
    }

    [Fact]
    public void AdhesionDto_PreservesNullTechnicalDates()
    {
        var dto = new AutomaticDebitAdhesionDto(
            5, 2419, "VGBIN84782", "MVB329806", "provider-id", null, "PENDING",
            AutomaticDebitApiDateTime.FromUtc(new DateTime(2026, 9, 18, 15, 30, 33)),
            AutomaticDebitApiDateTime.FromUtc(null),
            AutomaticDebitApiDateTime.FromUtc(null));

        dto.RequestedAt.Offset.Should().Be(TimeSpan.FromHours(-3));
        dto.ActivatedAt.Should().BeNull();
        dto.CancelledAt.Should().BeNull();
    }
}
