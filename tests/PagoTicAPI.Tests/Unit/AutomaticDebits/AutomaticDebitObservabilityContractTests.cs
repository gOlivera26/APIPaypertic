using System.Text.RegularExpressions;

namespace PagoTicAPI.Tests.Unit.AutomaticDebits;

public sealed class AutomaticDebitObservabilityContractTests
{
    private static readonly string[] ServiceFiles =
    [
        "PagoTicAPI.Application/Services/Implementations/AutomaticDebitAdhesionService.cs",
        "PagoTicAPI.Application/Services/Implementations/AutomaticDebitGenerationService.cs",
        "PagoTicAPI.Application/Webhooks/AutomaticDebits/AutomaticDebitWebhookProcessor.cs"
    ];

    [Fact]
    public void StructuredLogs_ContainCriticalTechnicalIdentifiers()
    {
        var logs = ReadLogStatements();

        logs.Should().Contain("JurisdictionId");
        logs.Should().Contain("AdhesionId");
        logs.Should().Contain("OperationId");
        logs.Should().Contain("ObligationId");
        logs.Should().Contain("ProviderTransactionId");
        logs.Should().Contain("ExternalTransactionId");
        logs.Should().Contain("ProviderState");
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("ClientSecret")]
    [InlineData("AccessToken")]
    [InlineData("IdentificationNumber")]
    [InlineData("PayerName")]
    [InlineData("PayerEmail")]
    [InlineData("CardNumber")]
    [InlineData("Cbu")]
    [InlineData("RawPayload")]
    [InlineData("RawBody")]
    public void StructuredLogs_DoNotContainSensitiveFields(string forbiddenField)
    {
        ReadLogStatements().Should().NotContainEquivalentOf(forbiddenField);
    }

    private static string ReadLogStatements()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            ServiceFiles.SelectMany(file => Regex.Matches(
                    File.ReadAllText(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))),
                    @"_logger\.Log(?:Debug|Information|Warning|Error)\([\s\S]*?\);",
                    RegexOptions.CultureInvariant)
                .Select(match => match.Value)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PagoTicAPI.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
