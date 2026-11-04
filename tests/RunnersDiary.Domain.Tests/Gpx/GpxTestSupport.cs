using System.Text;
using RunnersDiary.Domain.Gpx;

namespace RunnersDiary.Domain.Tests.Gpx;

internal static class GpxTestSupport
{
    public static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Meters per degree of latitude on the sphere used by the processor.</summary>
    public static readonly double MetersPerDegree = Haversine.EarthRadiusMeters * Math.PI / 180;

    private static readonly string Fixtures = FindUp("tests", "fixtures", "gpx");
    private static readonly string Demo = FindUp("seed", "demo", "gpx");

    public static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(Fixtures, name));

    public static byte[] DemoFile(string name) => File.ReadAllBytes(Path.Combine(Demo, name));

    public static string DemoDirectory => Path.GetDirectoryName(Demo)!;

    public static GpxReadResult Read(byte[] bytes, GpxReadLimits? limits = null) =>
        GpxReader.Read(new MemoryStream(bytes), limits ?? GpxReadLimits.Default, Now);

    public static GpxReadResult Read(string xml, GpxReadLimits? limits = null) => Read(Encoding.UTF8.GetBytes(xml), limits);

    public static GpxDocument ReadFixture(string name) => Ok(Read(Fixture(name)));

    public static GpxDocument Ok(GpxReadResult result)
    {
        Assert.True(result.IsSuccess, result.Rejection?.Code.ToString());
        return result.Document!;
    }

    public static ProcessedTrack Process(string fixture) => TrackProcessor.Process(ReadFixture(fixture));

    /// <summary>Builds a GPX 1.1 document from raw lines of <c>lat,lon[,ele[,time]]</c> tuples in one segment.</summary>
    public static string BuildGpx(IEnumerable<(double Lat, double Lon, double? Ele, string? Time)> points)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\"?><gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\"><trk><trkseg>");
        foreach (var (lat, lon, ele, time) in points)
        {
            sb.Append(FormattableString.Invariant($"<trkpt lat=\"{lat}\" lon=\"{lon}\">"));
            if (ele is not null)
            {
                sb.Append(FormattableString.Invariant($"<ele>{ele}</ele>"));
            }

            if (time is not null)
            {
                sb.Append("<time>").Append(time).Append("</time>");
            }

            sb.Append("</trkpt>");
        }

        return sb.Append("</trkseg></trk></gpx>").ToString();
    }

    public static GpxDocument Document(params (double Lat, double Lon, double? Ele, double? Seconds)[][] segments)
    {
        var t0 = new DateTime(2025, 6, 1, 7, 0, 0, DateTimeKind.Utc);
        var timed = segments.All(s => s.All(p => p.Seconds is not null));
        return new GpxDocument(
            [.. segments.Select(s => new GpxSegment([.. s.Select(p => new GpxPoint(p.Lat, p.Lon, p.Ele, timed ? t0.AddSeconds(p.Seconds!.Value) : null))]))],
            timed,
            TrackWarnings.None);
    }

    private static string FindUp(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(Path.Combine(parts) + " was not found above " + AppContext.BaseDirectory);
    }
}
