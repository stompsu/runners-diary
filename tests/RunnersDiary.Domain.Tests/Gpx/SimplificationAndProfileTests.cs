using RunnersDiary.Domain.Gpx;
using static RunnersDiary.Domain.Tests.Gpx.GpxTestSupport;

namespace RunnersDiary.Domain.Tests.Gpx;

public sealed class DouglasPeuckerTests
{
    private static (double, double, double?, double?)[] Line(int n, Func<int, (double Lat, double Lon)> f) =>
        [.. Enumerable.Range(0, n).Select(i => { var (la, lo) = f(i); return (la, lo, (double?)null, (double?)null); })];

    [Fact]
    public void A_straight_line_collapses_to_its_endpoints()
    {
        var doc = Document(Line(1000, i => (50 + i * 0.00001, 18)));

        var line = Assert.Single(DouglasPeucker.Simplify(doc));

        Assert.Equal(2, line.Count);
        Assert.Equal(new LonLat(18, 50), line[0]);
        Assert.Equal(new LonLat(18, 50.00999), line[^1]);
    }

    [Fact]
    public void A_corner_larger_than_the_tolerance_is_kept_and_a_smaller_one_dropped()
    {
        // 0.001 degrees of longitude at lat 50 is about 71.5 m; a 20 m sideways bump is kept, a 2 m one is not.
        var bump = 20 / MetersPerDegree;
        var big = Document([(50, 18, null, null), (50.001, 18 + bump, null, null), (50.002, 18, null, null)]);
        var small = Document([(50, 18, null, null), (50.001, 18 + 2 / MetersPerDegree, null, null), (50.002, 18, null, null)]);

        Assert.Equal(3, DouglasPeucker.Simplify(big)[0].Count);
        Assert.Equal(2, DouglasPeucker.Simplify(small)[0].Count);
    }

    [Fact]
    public void Segments_are_simplified_separately_and_keep_their_endpoints()
    {
        var doc = Document(
            Line(50, i => (50 + i * 0.0001, 18)),
            Line(50, i => (51 + i * 0.0001, 19)));

        var lines = DouglasPeucker.Simplify(doc);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(2, l.Count));
        Assert.Equal(51, lines[1][0].Latitude);
    }

    [Fact]
    public void Single_point_segments_are_not_lines()
    {
        var doc = Document(Line(5, i => (50 + i * 0.001, 18)), [(52, 18, null, null)]);

        Assert.Single(DouglasPeucker.Simplify(doc));
    }

    [Fact]
    public void Output_is_capped_at_four_thousand_vertices_for_a_250k_point_spiral()
    {
        var doc = Document(Line(250_000, i =>
        {
            var a = i * 0.01;
            var r = 0.00002 * i / 10; // slowly growing radius in degrees, so a dense spiral
            return (50 + r * Math.Sin(a) * 0.0001 * 50, 18 + r * Math.Cos(a) * 0.0001 * 50);
        }));

        var lines = DouglasPeucker.Simplify(doc);

        Assert.InRange(lines.Sum(l => l.Count), 2, DouglasPeucker.MaxVertices);
        Assert.Equal(new LonLat(Math.Round(doc.Segments[0].Points[0].Longitude, 6), Math.Round(doc.Segments[0].Points[0].Latitude, 6)), lines[0][0]);
        var last = doc.Segments[0].Points[^1];
        Assert.Equal(new LonLat(Math.Round(last.Longitude, 6), Math.Round(last.Latitude, 6)), lines[0][^1]);
    }

    [Fact]
    public void A_noisy_zigzag_forces_the_tolerance_to_grow_and_stays_deterministic()
    {
        var doc = Document(Line(20_000, i => (50 + i * 0.00002, 18 + (i % 2 == 0 ? 0 : 0.0001) + Math.Sin(i * 0.37) * 0.0003)));

        var a = DouglasPeucker.Simplify(doc).Sum(l => l.Count);
        var b = DouglasPeucker.Simplify(doc).Sum(l => l.Count);

        Assert.Equal(a, b);
        Assert.InRange(a, 2, DouglasPeucker.MaxVertices);
    }

    [Fact]
    public void A_250k_point_line_does_not_overflow_the_stack_even_when_every_split_is_maximally_unbalanced()
    {
        // A convex arc forces a deep split sequence for a recursive implementation; the stack-based one must cope.
        var doc = Document(Line(250_000, i => (50 + i * 1e-7, 18 + Math.Pow(i / 250_000.0, 2) * 0.01)));

        var lines = DouglasPeucker.Simplify(doc, 3, 100_000);

        Assert.True(lines[0].Count >= 2);
    }

    [Fact]
    public void Coordinates_are_rounded_to_six_decimals()
    {
        var doc = Document([(50.1234567891, 18.9876543219, null, null), (50.2, 18.99, null, null)]);

        var line = DouglasPeucker.Simplify(doc)[0];

        Assert.Equal(new LonLat(18.987654, 50.123457), line[0]);
    }

    [Fact]
    public void A_closed_loop_keeps_its_far_side()
    {
        // First and last point coincide: the chord is degenerate and the farthest point must still be found.
        var doc = Document(Line(361, i => (50 + 0.002 * Math.Sin(i * Math.PI / 180), 18 + 0.003 * Math.Cos(i * Math.PI / 180))));

        var line = DouglasPeucker.Simplify(doc)[0];

        Assert.True(line.Count > 8);
    }
}

public sealed class ElevationProfileTests
{
    [Fact]
    public void Profile_is_null_without_elevation()
    {
        Assert.Null(Process("no-elevation.gpx").Profile);
    }

    [Fact]
    public void Short_tracks_sample_every_ten_metres_including_the_end()
    {
        var doc = Document([(0, 0, 100, 0), (0, 95 / MetersPerDegree, 109.5, 10)]);

        var profile = ElevationProfile.Build(doc)!;

        Assert.Equal([0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 95], profile.Select(p => p.DistanceMeters));
        Assert.Equal(100.0, profile[0].ElevationMeters);
        Assert.Equal(109.5, profile[^1].ElevationMeters);
        Assert.Equal(101.0, profile[1].ElevationMeters, 1); // 9.5 m over 95 m = 0.1 per metre
    }

    [Fact]
    public void Long_tracks_have_at_most_601_samples_with_monotonic_distance()
    {
        var points = Enumerable.Range(0, 20_001).Select(i => (0.0, i * 2.0 / MetersPerDegree, (double?)(100 + 10 * Math.Sin(i / 500.0)), (double?)i)).ToArray();

        var profile = ElevationProfile.Build(Document(points))!;

        Assert.Equal(ElevationProfile.MaxSamples, profile.Count);
        Assert.Equal(0, profile[0].DistanceMeters);
        Assert.Equal(40_000, profile[^1].DistanceMeters);
        for (var i = 1; i < profile.Count; i++)
        {
            Assert.True(profile[i].DistanceMeters > profile[i - 1].DistanceMeters);
        }
    }

    [Fact]
    public void Samples_carry_interpolated_longitude_and_latitude()
    {
        var doc = Document([(50, 18, 100, 0), (50.002, 18.004, 110, 60)]);

        var profile = ElevationProfile.Build(doc)!;
        var total = Haversine.Distance(50, 18, 50.002, 18.004);
        var mid = profile.First(p => p.DistanceMeters >= (int)(total / 2) - 5);
        var fraction = mid.DistanceMeters / total;

        Assert.Equal(18 + 0.004 * fraction, mid.Longitude, 4);
        Assert.Equal(50 + 0.002 * fraction, mid.Latitude, 4);
        Assert.Equal(18, profile[0].Longitude);
        Assert.Equal(18.004, profile[^1].Longitude);
    }

    [Fact]
    public void Points_without_elevation_are_bridged_by_interpolation_and_the_ends_are_clamped()
    {
        var doc = Document([(0, 0, null, null), (0, 50 / MetersPerDegree, 100, null), (0, 100 / MetersPerDegree, null, null), (0, 150 / MetersPerDegree, 110, null)]);

        var profile = ElevationProfile.Build(doc)!;

        Assert.Equal(100.0, profile[0].ElevationMeters);                // clamped before the first elevation
        Assert.Equal(105.0, profile.First(p => p.DistanceMeters == 100).ElevationMeters); // bridged
        Assert.Equal(110.0, profile[^1].ElevationMeters);
    }

    [Fact]
    public void Demo_profiles_fit_the_budget()
    {
        foreach (var name in new[] { "gliwice-half-marathon.gpx", "ustron-trail-16k.gpx" })
        {
            var t = TrackProcessor.Process(Ok(Read(DemoFile(name))), TestContext.Current.CancellationToken);

            Assert.InRange(t.Profile!.Count, 2, ElevationProfile.MaxSamples);
            Assert.InRange(t.Line.Sum(l => l.Count), 2, DouglasPeucker.MaxVertices);
        }
    }
}
