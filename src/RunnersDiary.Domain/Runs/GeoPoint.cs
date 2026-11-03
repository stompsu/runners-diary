namespace RunnersDiary.Domain.Runs;

/// <summary>WGS 84 coordinate. Infrastructure maps it to a SQL Server <c>geography</c> point (SRID 4326).</summary>
public readonly record struct GeoPoint
{
    public GeoPoint(double latitude, double longitude)
    {
        if (!IsValid(latitude, longitude))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be in -90..90 and longitude in -180..180.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    public static bool IsValid(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
}
