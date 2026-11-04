namespace RunnersDiary.Domain.Gpx;

/// <summary>Great-circle distance on a sphere of mean Earth radius (the same R as the demo generator).</summary>
public static class Haversine
{
    /// <summary>IUGG mean Earth radius in metres (6 371 008.8 m).</summary>
    public const double EarthRadiusMeters = 6_371_008.8;

    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        var p1 = lat1 * Math.PI / 180;
        var p2 = lat2 * Math.PI / 180;
        var dPhi = p2 - p1;
        var dLambda = (lon2 - lon1) * Math.PI / 180;
        var h = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
