using Swashbuckle.AspNetCore.SwaggerGen;

namespace PagoTicAPI.API.OpenApi;

/// <summary>
/// Documents health-check endpoints that are mapped directly by ASP.NET Core and
/// therefore are not discovered by MVC's API explorer.
/// </summary>
public sealed class HealthChecksDocumentFilter : IDocumentFilter
{
    private static readonly OpenApiResponses Responses = new()
    {
        ["200"] = new OpenApiResponse { Description = "Healthy" },
        ["503"] = new OpenApiResponse { Description = "Unhealthy" }
    };

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        AddHealthPath(
            swaggerDoc,
            "/health/live",
            "Reports whether the API process is running.",
            "GetLiveness");
        AddHealthPath(
            swaggerDoc,
            "/health/ready",
            "Reports whether the API configuration and Oracle dependency are ready.",
            "GetReadiness");
        AddHealthPath(
            swaggerDoc,
            "/health",
            "Backward-compatible alias for the readiness check.",
            "GetHealth");
    }

    private static void AddHealthPath(
        OpenApiDocument swaggerDoc,
        string path,
        string description,
        string operationId)
    {
        swaggerDoc.Paths[path] = new OpenApiPathItem
        {
            Operations =
            {
                [OperationType.Get] = new OpenApiOperation
                {
                    Tags = [new OpenApiTag { Name = "Health" }],
                    Summary = description,
                    OperationId = operationId,
                    Responses = Responses,
                    Security = []
                }
            }
        };
    }
}
