namespace RunnersDiary.Infrastructure.Maps;

/// <summary>One geocoding hit. <see cref="Type"/> is a provider-neutral category (e.g. "locality") or null.</summary>
public sealed record GeocodedPlace(string Name, double Latitude, double Longitude, string? Type);

/// <summary>Provider-neutral place search (ADR 0022 §4). Implementations never receive the caller's identity.</summary>
public interface IGeocoder
{
    /// <summary>False when no provider is configured; the endpoint then answers 503 <c>feature-unavailable</c> and the SPA hides search.</summary>
    bool IsAvailable { get; }

    /// <summary>At most five places, best first. <paramref name="biasLatitude"/>/<paramref name="biasLongitude"/> are both set or both null.</summary>
    Task<IReadOnlyList<GeocodedPlace>> SearchAsync(
        string query, double? biasLatitude, double? biasLongitude, CancellationToken cancellationToken);
}

/// <summary>The default when <c>Maps:Geocoding:Provider</c> is <c>None</c>.</summary>
public sealed class NoGeocoder : IGeocoder
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<GeocodedPlace>> SearchAsync(string query, double? biasLatitude, double? biasLongitude, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("No geocoding provider is configured.");
}
