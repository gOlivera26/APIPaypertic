using System.Security.Cryptography;
using System.Text;
using PagoTicAPI.Application.Webhooks.AutomaticDebits;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public class AutomaticDebitWebhookEnvelopeTests
{
    [Fact]
    public void Create_PreservesExactBytesAndComputesSha256()
    {
        var bytes = Encoding.UTF8.GetBytes("{\n  \"type\": \"debit\", \"id\": \"pay-1\"\n}");

        var envelope = AutomaticDebitWebhookEnvelope.Create(bytes);

        envelope.RawBytes.Should().Equal(bytes);
        envelope.PayloadHash.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        envelope.Hint.Should().Be(new AutomaticDebitWebhookHint("payment", "pay-1", "debit"));
    }

    [Fact]
    public void Create_UsesExactRawBytesInsteadOfCanonicalizedJson()
    {
        var compact = Encoding.UTF8.GetBytes("{\"type\":\"debit\",\"id\":\"pay-1\"}");
        var spaced = Encoding.UTF8.GetBytes("{ \"type\": \"debit\", \"id\": \"pay-1\" }");

        var first = AutomaticDebitWebhookEnvelope.Create(compact);
        var second = AutomaticDebitWebhookEnvelope.Create(spaced);

        first.Hint.Should().Be(second.Hint);
        first.PayloadHash.Should().NotBe(second.PayloadHash);
    }

    [Theory]
    [InlineData("{\"type\":\"adhesion\",\"id\":\"adh-1\",\"notifications\":[{\"type\":\"subscription\"}]}", "subscription", "adh-1", "adhesion")]
    [InlineData("{\"type\":\"subscription\",\"id\":\"adh-1\"}", "subscription", "adh-1", "subscription")]
    [InlineData("{\"type\":\"debit\",\"id\":\"pay-2\",\"ignored\":{\"secret\":\"x\"}}", "payment", "pay-2", "debit")]
    [InlineData("{\"type\":\"online\",\"id\":\"pay-3\"}", "payment", "pay-3", "online")]
    public void Create_ParsesOnlyDocumentedHintFields(
        string json,
        string objectType,
        string id,
        string notificationType)
    {
        var envelope = AutomaticDebitWebhookEnvelope.Create(Encoding.UTF8.GetBytes(json));

        envelope.Hint.Should().Be(new AutomaticDebitWebhookHint(objectType, id, notificationType));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"type\":\"payment\"}")]
    [InlineData("{\"id\":\"pay-1\"}")]
    [InlineData("{\"type\":\"payment\",\"id\":\"pay-1\"}")]
    [InlineData("{\"type\":\"unknown\",\"id\":\"x\"}")]
    public void Create_MalformedOrUnsupportedInputHasNoHint(string body)
    {
        var envelope = AutomaticDebitWebhookEnvelope.Create(Encoding.UTF8.GetBytes(body));

        envelope.Hint.Should().BeNull();
        envelope.PayloadHash.Should().HaveLength(64);
    }
}
