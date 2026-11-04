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

/// <summary>Non-secret map settings (<c>Maps</c> section, ADR 0022 and 0033).</summary>
public sealed class MapsOptions
{
    public const string SectionName = "Maps";

    public const string DefaultStyleUrl = "https://tiles.openfreemap.org/styles/liberty";

    /// <summary>The display provider the SPA loads. <see cref="MapProvider.AzureMaps"/> needs the SAS settings in <see cref="AzureMaps"/> (ADR 0033).</summary>
    public MapProvider Provider { get; set; } = MapProvider.MapLibre;

    public MapLibreOptions MapLibre { get; set; } = new();

    public GeocodingOptions Geocoding { get; set; } = new();

    public AzureMapsOptions AzureMaps { get; set; } = new();

    public sealed class MapLibreOptions
    {
        public string StyleUrl { get; set; } = DefaultStyleUrl;
    }

    public sealed class GeocodingOptions
    {
        public GeocodingProvider Provider { get; set; } = GeocodingProvider.None;

        /// <summary>
        /// Geocode calls per UTC day and replica, across all callers (ADR 0033 §2). With at most two replicas the default of 75 stays
        /// below the 5,000 free searches a month.
        /// </summary>
        public int GlobalDailyLimit { get; set; } = 75;
    }
}
