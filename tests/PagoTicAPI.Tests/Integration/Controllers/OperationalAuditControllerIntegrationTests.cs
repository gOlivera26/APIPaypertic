using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.RateLimiting;
using PagoTicAPI.API.AutomaticDebits.PublicEndpoints;
using PagoTicAPI.API.Controllers;
using PagoTicAPI.Application.RequestDto.OperationalAudit;
using PagoTicAPI.Application.ResponseDto.OperationalAudit;
using PagoTicAPI.Application.ResponseDto.Common;

namespace PagoTicAPI.Tests.Integration.Controllers;

public sealed class OperationalAuditControllerIntegrationTests : IClassFixture<IntegrationTestBase>
{
    private readonly IntegrationTestBase _factory;

    public OperationalAuditControllerIntegrationTests(IntegrationTestBase factory) => _factory = factory;

    [Fact]
    public void Controller_UsesExistingAutomaticDebitPublicRateLimitPolicy()
    {
        var metadata = typeof(OperationalAuditController)
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        metadata.Should().NotBeNull();
        metadata!.PolicyName.Should().Be(AutomaticDebitPublicEndpointOptions.RateLimitPolicy);
    }

    [Fact]
    public async Task DebitList_IsAnonymousAndBindsDefaultPagination()
    {
        _factory.MockOperationalAuditService
            .Setup(x => x.GetAutomaticDebitsAsync(
                It.Is<AutomaticDebitAuditFilter>(f => f.Page == 1 && f.PageSize == 20),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<PagedAuditResult<AutomaticDebitAuditItem>>.SuccessResponse(
                new PagedAuditResult<AutomaticDebitAuditItem>(1, 20, 0, 0, [])));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/operaciones/debitos");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CheckoutList_BindsCombinedFilters()
    {
        _factory.MockOperationalAuditService
            .Setup(x => x.GetCheckoutsAsync(
                It.Is<CheckoutAuditFilter>(f =>
                    f.Page == 2 && f.PageSize == 10 && f.JurisdictionId == 2419 &&
                    f.TaxpayerAccountId == "ACC-1" && f.ObligationId == "OBL-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<PagedAuditResult<CheckoutAuditItem>>.SuccessResponse(
                new PagedAuditResult<CheckoutAuditItem>(2, 10, 0, 0, [])));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/operaciones/checkouts?page=2&pageSize=10&jurisdictionId=2419&taxpayerAccountId=ACC-1&obligationId=OBL-1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Summary_IsAnonymousAndBindsJurisdictionAndDateRange()
    {
        _factory.MockOperationalAuditService
            .Setup(x => x.GetSummaryAsync(
                It.Is<OperationalSummaryFilter>(f =>
                    f.JurisdictionId == 2419 && f.From == new DateTime(2026, 9, 18, 0, 0, 0)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<OperationalAuditSummary>.SuccessResponse(
                new OperationalAuditSummary(
                    2419, null, null, DateTimeOffset.Now,
                    new AutomaticDebitOperationalSummary(0, 0, 0, []),
                    new CheckoutOperationalSummary(0, 0, 0, []),
                    new InboxOperationalSummary(0, [], 0, []))));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/operaciones/resumen?jurisdictionId=2419&from=2026-09-18T00:00:00");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OperationalQueries_DoNotCallPayPerTicOrBusinessOperationServices()
    {
        _factory.MockOperationalAuditService
            .Setup(x => x.GetAutomaticDebitsAsync(
                It.IsAny<AutomaticDebitAuditFilter>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResponse<PagedAuditResult<AutomaticDebitAuditItem>>.SuccessResponse(
                new PagedAuditResult<AutomaticDebitAuditItem>(1, 20, 0, 0, [])));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/operaciones/debitos");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.MockPayPerTicService.VerifyNoOtherCalls();
        _factory.MockAutomaticDebitAdhesionService.VerifyNoOtherCalls();
        _factory.MockAutomaticDebitGenerationService.VerifyNoOtherCalls();
        _factory.MockAutomaticDebitWebhookProcessor.VerifyNoOtherCalls();
    }
}
