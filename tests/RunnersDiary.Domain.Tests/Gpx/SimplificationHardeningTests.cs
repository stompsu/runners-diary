using System.Diagnostics;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Tracks;
using static RunnersDiary.Domain.Tests.Gpx.GpxTestSupport;

namespace RunnersDiary.Domain.Tests.Gpx;

/// <summary>M4 review H1: Douglas–Peucker must stay cheap on adversarial input and unchanged on normal input.</summary>
public sealed class SimplificationHardeningTests
{
    /// <summary>Alternating sides of the line with slowly growing amplitude: every split peels off one end point (quadratic work).</summary>
    private static GpxDocument Zigzag(int n)
    {
        var points = new (double, double, double?, double?)[n];
        for (var i = 0; i < n; i++)
        {
            var amplitude = 100 + i * 0.001; // metres
            var y = (i % 2 == 0 ? 1 : -1) * amplitude;
            points[i] = (y / MetersPerDegree, i * 0.5 / MetersPerDegree, null, null);
        }

        return Document(points);
    }

    [Theory]
    [InlineData(125_000)]
    [InlineData(250_000)]
    public void The_worst_case_zigzag_is_processed_within_the_budget_and_in_bounded_time(int points)
    {
        var doc = Zigzag(points);
        var geometry = new TrackGeometry(doc);

        var watch = Stopwatch.StartNew();
        var line = DouglasPeucker.Simplify(
            geometry, DouglasPeucker.StartToleranceMeters, DouglasPeucker.MaxVertices, DouglasPeucker.MaxDistanceEvaluations,
            TestContext.Current.CancellationToken, out var evaluations, out var decimated);
        var simplifyTime = watch.Elapsed;
        watch.Restart();
        var processed = TrackProcessor.Process(doc, TestContext.Current.CancellationToken);

        Assert.True(simplifyTime < TimeSpan.FromSeconds(2), $"simplification took {simplifyTime}");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"processing took {watch.Elapsed}");
        Assert.True(evaluations <= DouglasPeucker.MaxDistanceEvaluations);
        Assert.True(decimated, "the quadratic input must hit the budget and fall back to uniform decimation");
        Assert.InRange(line.Sum(l => l.Count), 2, DouglasPeucker.MaxVertices);
        Assert.Equal(doc.Segments[0].Points[0].Longitude, line[0][0].Longitude, 6);
        Assert.Equal(doc.Segments[0].Points[^1].Longitude, line[0][^1].Longitude, 6);
        Assert.InRange(processed.Line.Sum(l => l.Count), 2, DouglasPeucker.MaxVertices);
    }

    [Fact]
    public void An_exhausted_budget_falls_back_to_uniform_decimation_keeping_every_segments_ends()
    {
        var doc = Document(
            [.. Enumerable.Range(0, 3000).Select(i => (50 + i * 1e-5, 18 + Math.Sin(i * 0.1) * 1e-3, (double?)null, (double?)null))],
            [.. Enumerable.Range(0, 3000).Select(i => (51 + i * 1e-5, 18 + Math.Sin(i * 0.1) * 1e-3, (double?)null, (double?)null))]);

        var lines = DouglasPeucker.Simplify(
            new TrackGeometry(doc), 3, 500, 1_000, TestContext.Current.CancellationToken, out var evaluations, out var decimated);

        Assert.True(decimated);
        Assert.True(evaluations <= 1_000);
        Assert.InRange(lines.Sum(l => l.Count), 4, 500);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.True(l.Count >= 2));
        Assert.Equal(Math.Round(doc.Segments[1].Points[^1].Latitude, 6), lines[1][^1].Latitude);
    }

    [Fact]
    public void Normal_tracks_never_touch_the_budget_and_match_the_tolerance_doubling_reference()
    {
        var random = new Random(42);
        foreach (var (count, step) in new[] { (500, 1e-4), (5_000, 3e-5), (30_000, 2e-5), (30_000, 2e-4) })
        {
            double lat = 50, lon = 18;
            var points = new (double, double, double?, double?)[count];
            for (var i = 0; i < count; i++)
            {
                lat += (random.NextDouble() - 0.45) * step;
                lon += (random.NextDouble() - 0.5) * step;
                points[i] = (lat, lon, null, null);
            }

            var doc = Document(points);

            var actual = DouglasPeucker.Simplify(
                new TrackGeometry(doc), DouglasPeucker.StartToleranceMeters, DouglasPeucker.MaxVertices, DouglasPeucker.MaxDistanceEvaluations,
                TestContext.Current.CancellationToken, out _, out var decimated);

            Assert.False(decimated);
            Assert.Equal(Reference(doc, DouglasPeucker.StartToleranceMeters, DouglasPeucker.MaxVertices), actual[0]);
        }
    }

    [Fact]
    public void A_cancelled_token_stops_processing()
    {
        Assert.Throws<OperationCanceledException>(() => TrackProcessor.Process(Zigzag(5_000), new CancellationToken(canceled: true)));
    }

    [Fact]
    public void Start_and_end_times_are_stored_with_whole_seconds_like_datetime2_0()
    {
        // 07:59:59.7 rounds up to 08:00:00, which moves the local start minute; the entity must already say so.
        var start = new DateTime(2025, 6, 1, 7, 59, 59, 700, DateTimeKind.Utc);
        var shifted = new GpxDocument(
            [new GpxSegment([new GpxPoint(50.0, 18.0, 100, start), new GpxPoint(50.001, 18.0, 101, start.AddSeconds(10))])],
            true,
            TrackWarnings.None);

        var track = GpxTrack.CreateProcessed(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), GpxTrackKind.RecordedTrack, "a.gpx", 10, new byte[32], "o", "p",
            TrackProcessor.Process(shifted, TestContext.Current.CancellationToken), new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTime(2025, 6, 1, 8, 0, 0, DateTimeKind.Utc), track.StartUtc);
        Assert.Equal(new DateTime(2025, 6, 1, 8, 0, 10, DateTimeKind.Utc), track.EndUtc);
    }

    [Theory]
    [InlineData("run‮gpx.gpx", "rungpx.gpx")]
    [InlineData("a​b⁦c.gpx", "abc.gpx")]
    public void Format_characters_are_stripped_from_file_names(string raw, string expected)
    {
        Assert.True(GpxFileName.TryNormalize(raw, out var name));
        Assert.Equal(expected, name);
    }

    /// <summary>The original implementation: a full Douglas–Peucker pass per tolerance, doubling until the cap holds.</summary>
    private static List<LonLat> Reference(GpxDocument doc, double start, int max)
    {
        var pts = doc.Segments[0].Points;
        var lat0 = (pts.Min(p => p.Latitude) + pts.Max(p => p.Latitude)) / 2;
        var lon0 = (pts.Min(p => p.Longitude) + pts.Max(p => p.Longitude)) / 2;
        var k = Math.PI / 180 * Haversine.EarthRadiusMeters;
        var cos = Math.Cos(lat0 * Math.PI / 180);
        var x = pts.Select(p => (p.Longitude - lon0) * cos * k).ToArray();
        var y = pts.Select(p => (p.Latitude - lat0) * k).ToArray();
        for (var tolerance = start; ; tolerance *= 2)
        {
            var keep = new bool[pts.Length];
            keep[0] = keep[^1] = true;
            var stack = new Stack<(int, int)>([(0, pts.Length - 1)]);
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                double best = -1;
                var index = -1;
                for (var i = a + 1; i < b; i++)
                {
                    var dx = x[b] - x[a];
                    var dy = y[b] - y[a];
                    var d = Math.Abs(dx * (y[a] - y[i]) - dy * (x[a] - x[i])) / Math.Sqrt(dx * dx + dy * dy);
                    if (d > best)
                    {
                        best = d;
                        index = i;
                    }
                }

                if (best > tolerance)
                {
                    keep[index] = true;
                    stack.Push((a, index));
                    stack.Push((index, b));
                }
            }

            if (keep.Count(v => v) <= max || tolerance > 1e7)
            {
                return [.. pts.Where((_, i) => keep[i]).Select(p => new LonLat(Math.Round(p.Longitude, 6), Math.Round(p.Latitude, 6)))];
            }
        }
    }
}
