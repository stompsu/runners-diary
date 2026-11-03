using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RunnersDiary.Api.Features.Health;

[JsonConverter(typeof(JsonStringEnumConverter<ApiHealthStatus>))]
public enum ApiHealthStatus
{
    Unhealthy,
    Degraded,
    Healthy,
}

public sealed record ApiHealthResponse(ApiHealthStatus Status, string Version);

public static class GetApiHealth
{
    private static readonly string Version =
        typeof(GetApiHealth).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static IServiceCollection AddHealthFeature(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CachedHealthReport>();
        return services;
    }

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", HandleAsync)
            .WithName("getApiHealth")
            .WithTags("Health")
            .WithSummary("Aggregate API health for the web app shell.")
            .Produces<ApiHealthResponse>(StatusCodes.Status200OK)
            .Produces<ApiHealthResponse>(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous();

        return endpoints;
    }

    // Returns only the aggregate status; per-check names and errors are never exposed (ADR 0007).
    private static async Task<IResult> HandleAsync([FromServices] CachedHealthReport healthReport, CancellationToken cancellationToken)
    {
        var status = await healthReport.GetStatusAsync(cancellationToken) switch
        {
            HealthStatus.Healthy => ApiHealthStatus.Healthy,
            HealthStatus.Degraded => ApiHealthStatus.Degraded,
            _ => ApiHealthStatus.Unhealthy,
        };
        var response = new ApiHealthResponse(status, Version);

        return status == ApiHealthStatus.Unhealthy
            ? TypedResults.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable)
            : TypedResults.Ok(response);
    }
}
