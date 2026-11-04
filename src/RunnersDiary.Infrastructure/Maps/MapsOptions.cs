namespace RunnersDiary.Infrastructure.Maps;

public enum MapProvider
{
    MapLibre,
    AzureMaps,
}

public enum GeocodingProvider
{
    None,
    AzureMaps,
}

/// <summary>Non-secret map settings (<c>Maps</c> section, ADR 0022). The Azure Maps adapters are deferred to M7.</summary>
public sealed class MapsOptions
{
    public const string SectionName = "Maps";

    public const string DefaultStyleUrl = "https://tiles.openfreemap.org/styles/liberty";

    public MapProvider Provider { get; set; } = MapProvider.MapLibre;

    public MapLibreOptions MapLibre { get; set; } = new();

    public GeocodingOptions Geocoding { get; set; } = new();

    public sealed class MapLibreOptions
    {
        public string StyleUrl { get; set; } = DefaultStyleUrl;
    }

    public sealed class GeocodingOptions
    {
        public GeocodingProvider Provider { get; set; } = GeocodingProvider.None;
    }
}
