using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Tests;

/// <summary>A provider-neutral test geocoder: returns six places (the endpoint must cut to five) and records what it was asked.</summary>
internal sealed class FakeGeocoder : IGeocoder
{
    public static string? LastQuery { get; private set; }

    public static (double? Lat, double? Lon) LastBias { get; private set; }

    public static bool Throw { get; set; }

    public bool IsAvailable => true;

    public Task<IReadOnlyList<GeocodedPlace>> SearchAsync(string query, double? biasLatitude, double? biasLongitude, CancellationToken cancellationToken)
    {
        if (Throw)
        {
            throw new HttpRequestException("provider secret detail");
        }

        LastQuery = query;
        LastBias = (biasLatitude, biasLongitude);
        IReadOnlyList<GeocodedPlace> places =
        [
            .. Enumerable.Range(1, 6).Select(i => new GeocodedPlace($"{query} {i}", 50.29 + i / 100.0, 18.67, i == 1 ? "locality" : null)),
        ];
        return Task.FromResult(places);
    }
}
