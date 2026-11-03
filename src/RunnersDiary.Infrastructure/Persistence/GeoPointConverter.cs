using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite.Geometries;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Infrastructure.Persistence;

/// <summary>GeoPoint (Domain, no NTS) to a NetTopologySuite Point in SRID 4326 (X = longitude, Y = latitude) for a SQL Server <c>geography</c> column.</summary>
public sealed class GeoPointConverter : ValueConverter<GeoPoint, Point>
{
    public const int Srid = 4326;

    private static readonly GeometryFactory Factory = new(new PrecisionModel(), Srid);

    public GeoPointConverter()
        : base(p => ToPoint(p), p => new GeoPoint(p.Y, p.X))
    {
    }

    private static Point ToPoint(GeoPoint p) => Factory.CreatePoint(new Coordinate(p.Longitude, p.Latitude));
}
