using RunnersDiary.Domain.Gpx;
using static RunnersDiary.Domain.Tests.Gpx.GpxTestSupport;

namespace RunnersDiary.Domain.Tests.Gpx;

public sealed class TrackMetricsTests
{
    [Fact]
    public void Haversine_of_one_degree_of_latitude_is_R_times_pi_over_180()
    {
        var d = Haversine.Distance(0, 0, 1, 0);

        // 6 371 008.8 * pi / 180 = 111 195.08 m (the textbook 111.2 km per degree).
        Assert.Equal(111_195.08, d, 1);
        Assert.Equal(MetersPerDegree, d, 6);
    }

    [Fact]
    public void Haversine_is_symmetric_zero_for_equal_points_and_handles_the_antimeridian()
    {
        Assert.Equal(0, Haversine.Distance(50, 18, 50, 18));
        Assert.Equal(Haversine.Distance(50, 18, 51, 19), Haversine.Distance(51, 19, 50, 18), 6);
        Assert.Equal(Haversine.Distance(0, 179.5, 0, -179.5), MetersPerDegree, 3);
    }

    [Fact]
    public void Haversine_along_the_equator_matches_arc_length()
    {
        Assert.Equal(MetersPerDegree * 0.5, Haversine.Distance(0, 10, 0, 10.5), 3);
    }

    [Fact]
    public void Road_fixture_distance_time_and_start_finish_are_hand_checkable()
    {
        var s = Process("road-5k.gpx").Summary;

        // 5 steps of 0.009 degrees of latitude along a meridian.
        Assert.Equal(Math.Round(5 * 0.009 * MetersPerDegree, 1), s.DistanceMeters, 6);
        Assert.Equal(6, s.PointCount);
        Assert.Equal(1500, s.ElapsedSeconds);
        Assert.Equal(1500, s.MovingSeconds);
        Assert.Equal(new DateTime(2025, 6, 1, 7, 0, 0, DateTimeKind.Utc), s.StartUtc);
        Assert.Equal(new DateTime(2025, 6, 1, 7, 25, 0, DateTimeKind.Utc), s.EndUtc);
        Assert.Equal((50.0, 18.0), (s.StartLatitude, s.StartLongitude));
        Assert.Equal(50.045, s.FinishLatitude, 9);
        Assert.Equal(0.0, s.ElevationGainMeters);
        Assert.Equal(0.0, s.ElevationLossMeters);
        Assert.Equal(100.0, s.MinElevationMeters);
        Assert.Equal(100.0, s.MaxElevationMeters);
        Assert.Equal(TrackWarnings.None, s.Warnings);
    }

    [Fact]
    public void Hysteresis_ignores_noise_under_three_metres_and_counts_the_real_steps()
    {
        // Hand computed: pivot 100; 100/102 noise; 110 (+10); 112/110/111 noise; 120 (+10); 118/121/119 noise;
        // 110 (-10); 107 (-3, exactly the threshold); 108/106 noise.
        var s = Process("hill-hysteresis.gpx").Summary;

        Assert.Equal(20.0, s.ElevationGainMeters);
        Assert.Equal(13.0, s.ElevationLossMeters);
        Assert.Equal(100.0, s.MinElevationMeters);
        Assert.Equal(121.0, s.MaxElevationMeters);
    }

    [Fact]
    public void Hysteresis_helper_cases()
    {
        Assert.Equal((0.0, 0.0), ElevationHysteresis.Compute([]));
        Assert.Equal((0.0, 0.0), ElevationHysteresis.Compute([100]));
        Assert.Equal((0.0, 0.0), ElevationHysteresis.Compute([100, 102.9, 100.1, 97.1]));
        Assert.Equal((3.0, 0.0), ElevationHysteresis.Compute([100, 103]));
        Assert.Equal((0.0, 3.0), ElevationHysteresis.Compute([103, 100]));
        // Slow ramp of 1 m steps: gain is counted in 3 m chunks relative to the moving pivot.
        Assert.Equal((9.0, 0.0), ElevationHysteresis.Compute([100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110]));
        // Up 5, down 5, up 5 = two ups and one down.
        Assert.Equal((10.0, 5.0), ElevationHysteresis.Compute([100, 105, 100, 105]));
    }

    [Fact]
    public void Hysteresis_pivot_is_reset_per_segment()
    {
        // Segment A ends at 110, segment B starts at 100: a continuous walk would count a 10 m loss.
        var doc = Document(
            [(50, 18, 100, 0), (50.001, 18, 110, 10)],
            [(50.01, 18, 100, 20), (50.011, 18, 100, 30)]);

        var s = TrackMetrics.Compute(doc);

        Assert.Equal(10.0, s.ElevationGainMeters);
        Assert.Equal(0.0, s.ElevationLossMeters);
    }

    [Fact]
    public void Points_without_elevation_are_skipped_by_the_hysteresis()
    {
        var doc = Document([(50, 18, 100, 0), (50.001, 18, null, 10), (50.002, 18, 106, 20)]);

        Assert.Equal(6.0, TrackMetrics.Compute(doc).ElevationGainMeters);
    }

    [Fact]
    public void No_elevation_gives_null_elevation_metrics()
    {
        var s = Process("no-elevation.gpx").Summary;

        Assert.Null(s.ElevationGainMeters);
        Assert.Null(s.ElevationLossMeters);
        Assert.Null(s.MinElevationMeters);
        Assert.Null(s.MaxElevationMeters);
        Assert.True(s.Warnings.HasFlag(TrackWarnings.NoElevation));
    }

    [Fact]
    public void Untimed_tracks_have_null_times()
    {
        var s = Process("no-time.gpx").Summary;

        Assert.Null(s.ElapsedSeconds);
        Assert.Null(s.MovingSeconds);
        Assert.Null(s.StartUtc);
        Assert.Null(s.EndUtc);
    }

    [Fact]
    public void Moving_time_excludes_standstill_but_elapsed_includes_it()
    {
        var s = Process("stop-and-go.gpx").Summary;

        Assert.Equal(240, s.ElapsedSeconds);
        Assert.Equal(120, s.MovingSeconds);
    }

    [Fact]
    public void Moving_threshold_is_half_a_metre_per_second_boundary()
    {
        // Just above 0.5 m/s counts, just below does not (an exact 0.5 would depend on float rounding).
        var step = 5.01 / MetersPerDegree;
        var doc = Document([(0, 0, null, 0), (0, step, null, 10), (0, step + 2.5 / MetersPerDegree, null, 20), (0, step + 2.5 / MetersPerDegree + 4.9 / MetersPerDegree, null, 30)]);

        // Intervals: 5.01 m / 10 s = 0.501 (counts), 2.5 m / 10 s = 0.25 (not), 4.9 m / 10 s = 0.49 (not).
        Assert.Equal(10, TrackMetrics.Compute(doc).MovingSeconds);
    }

    [Fact]
    public void Elapsed_time_includes_gaps_between_segments_but_distance_does_not()
    {
        var s = Process("multi-segment.gpx").Summary;

        // Two segments of 2 x 0.001 degrees each; the 0.008 degree gap between them is not counted.
        Assert.Equal(Math.Round(4 * 0.001 * MetersPerDegree, 1), s.DistanceMeters, 6);
        Assert.Equal(120, s.ElapsedSeconds);
        Assert.Equal(40, s.MovingSeconds);
        Assert.True(s.Warnings.HasFlag(TrackWarnings.MultipleSegments));
    }

    [Fact]
    public void Values_are_rounded_at_the_output_boundary_only()
    {
        var doc = Document([(0, 0, 0.04, 0), (0, 0.00001, 3.26, 1)]);

        var s = TrackMetrics.Compute(doc);

        Assert.Equal(Math.Round(0.00001 * MetersPerDegree, 1), s.DistanceMeters);
        Assert.Equal(3.2, s.ElevationGainMeters);
    }

    [Fact]
    public void Processing_is_deterministic()
    {
        var a = Process("hill-hysteresis.gpx");
        var b = Process("hill-hysteresis.gpx");

        Assert.Equal(a.Summary, b.Summary);
        Assert.Equal(a.BoundingBox, b.BoundingBox);
        Assert.Equal(a.Line.Select(l => l.ToArray()).ToArray(), b.Line.Select(l => l.ToArray()).ToArray());
        Assert.Equal(1, TrackProcessor.ProcessorVersion);
        Assert.Equal(1, a.ProcessorVersion);
    }

    [Fact]
    public void Route_only_files_get_distance_and_elevation_but_no_time()
    {
        var s = Process("route-only.gpx").Summary;

        Assert.Equal(Math.Round(3 * 0.001 * MetersPerDegree, 1), s.DistanceMeters, 6);
        Assert.Null(s.ElapsedSeconds);
        Assert.True(s.Warnings.HasFlag(TrackWarnings.RouteConverted));
    }

    [Fact]
    public void Bounding_box_covers_all_raw_points()
    {
        var t = Process("road-5k.gpx");

        Assert.Equal([18.0, 50.0, 18.0, 50.045], t.BoundingBox);
        Assert.Equal(new LonLat(18.0, 50.0), t.Start);
        Assert.Equal(new LonLat(18.0, 50.045), t.Finish);
    }
}

public sealed class SplitsTests
{
    [Fact]
    public void Road_fixture_has_five_full_splits_and_a_partial_one()
    {
        var splits = Process("road-5k.gpx").Splits;

        var total = Math.Round(5 * 0.009 * MetersPerDegree, 1);
        Assert.Equal(6, splits.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6], splits.Select(s => s.Index));
        Assert.All(splits.Take(5), s => Assert.Equal(1000.0, s.DistanceMeters));
        // 300 s per 1000.76 m: each full km takes 299.77 s, rounded to 300.
        Assert.All(splits.Take(5), s => Assert.Equal(300, s.ElapsedSeconds));
        Assert.Equal(Math.Round(total - 5000, 1), splits[5].DistanceMeters, 6);
        Assert.Equal(1, splits[5].ElapsedSeconds); // 3.8 m at 3.34 m/s = 1.1 s
        Assert.All(splits, s => Assert.Equal(0.0, s.ElevationDeltaMeters));
    }

    [Fact]
    public void An_exact_multiple_of_a_kilometre_has_no_partial_split()
    {
        var doc = Document([(0, 0, null, 0), (0, 1000 / MetersPerDegree, null, 200), (0, 2000 / MetersPerDegree, null, 500)]);

        var splits = Splits.Compute(doc);

        Assert.Equal(2, splits.Count);
        Assert.Equal([200, 300], splits.Select(s => s.ElapsedSeconds));
    }

    [Fact]
    public void A_track_shorter_than_a_kilometre_has_one_partial_split()
    {
        var splits = Splits.Compute(Document([(0, 0, 10, 0), (0, 400 / MetersPerDegree, 14, 100)]));

        var only = Assert.Single(splits);
        Assert.Equal(400.0, only.DistanceMeters);
        Assert.Equal(100, only.ElapsedSeconds);
        Assert.Equal(4.0, only.ElevationDeltaMeters);
    }

    [Fact]
    public void Untimed_tracks_have_null_split_times_and_elevation_less_tracks_null_deltas()
    {
        var untimed = Process("no-time.gpx").Splits;
        var flat = Process("no-elevation.gpx").Splits;

        Assert.All(untimed, s => Assert.Null(s.ElapsedSeconds));
        Assert.All(flat, s => Assert.Null(s.ElevationDeltaMeters));
        Assert.All(flat, s => Assert.NotNull(s.ElapsedSeconds));
    }

    [Fact]
    public void Time_and_elevation_are_interpolated_at_the_boundary()
    {
        // 1500 m with a stop-free pace change: first 500 m in 100 s, next 1000 m in 400 s; elevation 0 -> 30 linear over distance.
        var doc = Document([(0, 0, 0, 0), (0, 500 / MetersPerDegree, 10, 100), (0, 1500 / MetersPerDegree, 30, 500)]);

        var splits = Splits.Compute(doc);

        // Boundary 1000 m lies halfway through the second interval: t = 100 + 200 = 300 s, ele = 10 + 10 = 20 m.
        Assert.Equal(2, splits.Count);
        Assert.Equal(300, splits[0].ElapsedSeconds);
        Assert.Equal(20.0, splits[0].ElevationDeltaMeters);
        Assert.Equal(200, splits[1].ElapsedSeconds);
        Assert.Equal(10.0, splits[1].ElevationDeltaMeters);
    }

    [Fact]
    public void Splits_are_capped_at_one_thousand()
    {
        var doc = Document([(0, 0, null, 0), (0, 1_100_000 / MetersPerDegree, null, 1000)]);

        Assert.Equal(1000, Splits.Compute(doc).Count);
    }
}

public sealed class TrackStartLocalTests
{
    [Fact]
    public void Summer_utc_becomes_two_hours_ahead_in_warsaw()
    {
        var local = TrackStartLocal.For(new DateTime(2025, 9, 14, 7, 0, 0, DateTimeKind.Utc), "Europe/Warsaw");

        Assert.Equal(new TrackStartLocalValue(new DateOnly(2025, 9, 14), new TimeOnly(9, 0), "Europe/Warsaw"), local);
    }

    [Fact]
    public void Winter_utc_is_one_hour_ahead_and_the_dst_boundary_is_respected()
    {
        // 2025-03-30 01:00 UTC is when Warsaw moves from CET (+1) to CEST (+2).
        var before = TrackStartLocal.For(new DateTime(2025, 3, 30, 0, 59, 0, DateTimeKind.Utc), "Europe/Warsaw")!;
        var after = TrackStartLocal.For(new DateTime(2025, 3, 30, 1, 0, 0, DateTimeKind.Utc), "Europe/Warsaw")!;

        Assert.Equal(new TimeOnly(1, 59), before.Time);
        Assert.Equal(new TimeOnly(3, 0), after.Time);
    }

    [Fact]
    public void A_western_zone_gives_the_previous_date()
    {
        var local = TrackStartLocal.For(new DateTime(2025, 9, 14, 3, 30, 45, DateTimeKind.Utc), "America/Los_Angeles")!;

        Assert.Equal(new DateOnly(2025, 9, 13), local.Date);
        Assert.Equal(new TimeOnly(20, 30), local.Time);
    }

    [Fact]
    public void Untimed_tracks_and_invalid_zones_give_null()
    {
        Assert.Null(TrackStartLocal.For(null, "Europe/Warsaw"));
        Assert.Null(TrackStartLocal.For(new DateTime(2025, 9, 14, 7, 0, 0, DateTimeKind.Utc), "Central European Standard Time"));
        Assert.Null(TrackStartLocal.For(new DateTime(2025, 9, 14, 7, 0, 0, DateTimeKind.Utc), "Nope/Nowhere"));
    }
}
