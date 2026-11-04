using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Api.Http;
using RunnersDiary.Api.RateLimiting;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Features.Geocoding;

public sealed record PlaceDto
{
    public required string Name { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required string? Type { get; init; }
}

public sealed record PlaceSearchResponse
{
    /// <summary>At most five places, best first.</summary>
    public required IReadOnlyList<PlaceDto> Results { get; init; }
}

public static class SearchPlaces
{
    /// <summary>Problem <c>code</c> of the 429 that the shared daily budget answers with (the per-caller limiter has none), so the SPA can say why.</summary>
    public const string GeocodeBudgetCode = "geocode-budget";

    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 100;

    public static IEndpointRouteBuilder MapGeocodingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/geocoding/search", HandleAsync)
            .WithName("searchPlaces")
            .WithTags("Geocoding")
            .WithSummary("Searches places by name (provider-neutral). 503 feature-unavailable when no geocoding provider is configured; 429 with code geocode-budget when the daily provider budget is spent.")
            .Produces<PlaceSearchResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitingSetup.GeocodePolicy);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] IGeocoder geocoder, [FromServices] GeocodeBudget budget, HttpContext context, CancellationToken cancellationToken, string? q = null, double? lat = null, double? lon = null)
    {
        if (!geocoder.IsAvailable)
        {
            return Unavailable("Place search is not available.");
        }

        var errors = new Dictionary<string, string[]>();
        var query = q?.Trim() ?? string.Empty;
        if (query.Length is < MinQueryLength or > MaxQueryLength)
        {
            errors["q"] = [$"Must be {MinQueryLength} to {MaxQueryLength} characters."];
        }

        if ((lat is null) != (lon is null))
        {
            errors["lat"] = ["lat and lon must be given together."];
        }
        else if (lat is { } la && lon is { } lo)
        {
            if (!double.IsFinite(la) || la is < -90 or > 90)
            {
                errors["lat"] = ["Must be between -90 and 90."];
            }

            if (!double.IsFinite(lo) || lo is < -180 or > 180)
            {
                errors["lon"] = ["Must be between -180 and 180."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // After validation, so a malformed request never spends the shared daily budget (ADR 0033 section 2).
        var (granted, retryAfter) = await budget.TryAcquireAsync(cancellationToken);
        if (!granted)
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many requests",
                detail: "The daily place search budget is used up. Retry after the time given in the Retry-After header.",
                type: ProblemTypes.RateLimited,
                extensions: new Dictionary<string, object?> { ["code"] = GeocodeBudgetCode });
        }

        try
        {
            var places = await geocoder.SearchAsync(query, lat, lon, cancellationToken);
            return TypedResults.Ok(new PlaceSearchResponse
            {
                Results = [.. places.Take(5).Select(p => new PlaceDto { Name = p.Name, Latitude = p.Latitude, Longitude = p.Longitude, Type = p.Type })],
            });
        }
#pragma warning disable CA1031 // A provider failure of any kind is an outage for the caller, never a 500 with provider details.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return Unavailable("Place search is temporarily unavailable.");
        }
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Unavailable(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Feature unavailable",
        detail: detail,
        type: ProblemTypes.FeatureUnavailable);
}
