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
    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 100;

    public static IEndpointRouteBuilder MapGeocodingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/geocoding/search", HandleAsync)
            .WithName("searchPlaces")
            .WithTags("Geocoding")
            .WithSummary("Searches places by name (provider-neutral). 503 feature-unavailable when no geocoding provider is configured.")
            .Produces<PlaceSearchResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequireRateLimiting(RateLimitingSetup.GeocodePolicy);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        [FromServices] IGeocoder geocoder, CancellationToken cancellationToken, string? q = null, double? lat = null, double? lon = null)
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
