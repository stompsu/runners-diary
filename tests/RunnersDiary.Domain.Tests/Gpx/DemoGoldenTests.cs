using RunnersDiary.Domain.Gpx;
using static RunnersDiary.Domain.Tests.Gpx.GpxTestSupport;

namespace RunnersDiary.Domain.Tests.Gpx;

/// <summary>
/// Golden values for the six demo files. Two reference sets:
/// (1) seed/demo/README.md (distance km to 3 decimals, gain/loss whole metres, time) and
/// (2) an independent re-implementation (Python, same algorithm) run on the committed files, which carry elevations
///     rounded to 0.1 m. The generator computed the README gain/loss from unrounded elevations, so the hysteresis pivots
///     can land differently: up to 3.8 m apart (see the half-marathon course loss: README 40, from the file 36.2).
/// </summary>
public sealed class DemoGoldenTests
{
    // file, points, README distance (m), file-derived distance (m), README gain, README loss, file-derived gain, file-derived loss, seconds (null = untimed)
    public static TheoryData<string, int, double, double, double, double, double, double, int?> Cases => new()
    {
        { "gliwice-10k.gpx", 893, 10_005, 10_004.78, 15, 15, 15.0, 15.0, 2970 },
        { "gliwice-half-marathon.gpx", 1213, 21_098, 21_098.18, 40, 37, 39.2, 39.1, 6820 },
        { "ustron-trail-16k.gpx", 1118, 15_995, 15_995.29, 746, 744, 747.2, 747.5, 8640 },
        { "gliwice-cross-6k.gpx", 654, 6_002, 6_001.93, 12, 12, 12.2, 12.2, 1980 },
        { "gliwice-10k-course.gpx", 893, 10_004, 10_004.03, 15, 12, 15.1, 15.0, null },
        { "gliwice-half-marathon-course.gpx", 1213, 21_094, 21_094.37, 40, 40, 39.2, 36.2, null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Demo_file_matches_the_golden_values(
        string file, int points, double readmeDistance, double fileDistance, double readmeGain, double readmeLoss,
        double fileGain, double fileLoss, int? seconds)
    {
        var s = TrackProcessor.Process(Ok(Read(DemoFile(file))), TestContext.Current.CancellationToken).Summary;

        Assert.Equal(points, s.PointCount);

        // README distance is published in km with 3 decimals: +-0.5 m rounding, plus 0.5 m margin -> 1 m (plan AC3).
        Assert.InRange(s.DistanceMeters, readmeDistance - 1, readmeDistance + 1);
        // Against the independent calculation on the same bytes only the 0.1 m output rounding may differ.
        Assert.InRange(s.DistanceMeters, fileDistance - 0.1, fileDistance + 0.1);

        Assert.InRange(s.ElevationGainMeters!.Value, fileGain - 0.11, fileGain + 0.11);
        Assert.InRange(s.ElevationLossMeters!.Value, fileLoss - 0.11, fileLoss + 0.11);
        // README gain/loss: whole metres from unrounded generator elevations; observed worst case 3.8 m, so 4 m.
        Assert.InRange(s.ElevationGainMeters!.Value, readmeGain - 4, readmeGain + 4);
        Assert.InRange(s.ElevationLossMeters!.Value, readmeLoss - 4, readmeLoss + 4);

        Assert.Equal(seconds, s.ElapsedSeconds); // exact
        Assert.Equal(seconds is null, s.StartUtc is null);
        if (seconds is not null)
        {
            Assert.InRange(s.MovingSeconds!.Value, seconds.Value * 0.95, seconds.Value);
            Assert.Equal(TrackWarnings.None, s.Warnings);
        }
        else
        {
            Assert.Equal(TrackWarnings.None, s.Warnings);
        }
    }

    [Fact]
    public void Demo_run_elevation_figures_equal_the_whole_metre_values_the_pipeline_computes_from_their_track()
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(DemoDirectory, "runs.json")));
        var checkedRuns = 0;
        var mismatches = new List<string>();
        foreach (var run in json.RootElement.GetProperty("runs").EnumerateArray())
        {
            if (!run.TryGetProperty("gpx", out var gpx) || !run.TryGetProperty("elevationGainMeters", out var gain))
            {
                continue;
            }

            var s = TrackProcessor.Process(Ok(Read(DemoFile(Path.GetFileName(gpx.GetProperty("file").GetString()!)))), TestContext.Current.CancellationToken).Summary;
            var expectedGain = Math.Round(s.ElevationGainMeters!.Value, MidpointRounding.AwayFromZero);
            var expectedLoss = Math.Round(s.ElevationLossMeters!.Value, MidpointRounding.AwayFromZero);
            if (gain.GetDouble() != expectedGain || run.GetProperty("elevationLossMeters").GetDouble() != expectedLoss)
            {
                mismatches.Add($"{gpx.GetProperty("file").GetString()}: runs.json +{gain.GetDouble()}/-{run.GetProperty("elevationLossMeters").GetDouble()}, track +{expectedGain}/-{expectedLoss}");
            }

            checkedRuns++;
        }

        Assert.True(mismatches.Count == 0, string.Join(" ; ", mismatches));
        Assert.Equal(4, checkedRuns);
    }

    [Fact]
    public void Recorded_race_starts_at_09_00_local_in_warsaw()
    {
        var s = TrackProcessor.Process(Ok(Read(DemoFile("gliwice-10k.gpx"))), TestContext.Current.CancellationToken).Summary;

        var local = TrackStartLocal.For(s.StartUtc, "Europe/Warsaw")!;

        Assert.Equal(new DateOnly(2025, 9, 14), local.Date);
        Assert.Equal(new TimeOnly(9, 0), local.Time);
    }

    [Fact]
    public void Demo_run_distances_agree_with_their_tracks_within_one_percent()
    {
        var runs = DemoRunDistances();

        foreach (var (file, runDistance) in runs)
        {
            var distance = TrackProcessor.Process(Ok(Read(DemoFile(file))), TestContext.Current.CancellationToken).Summary.DistanceMeters;
            Assert.InRange(distance / runDistance, 0.99, 1.01);
        }
    }

    [Fact]
    public void All_demo_files_process_within_the_output_budgets()
    {
        foreach (var file in Cases.Select(c => c.Data.Item1))
        {
            var t = TrackProcessor.Process(Ok(Read(DemoFile(file))), TestContext.Current.CancellationToken);

            Assert.InRange(t.Line.Sum(l => l.Count), 2, DouglasPeucker.MaxVertices);
            Assert.NotNull(t.Profile);
            Assert.Equal(Math.Ceiling(t.Summary.DistanceMeters / 1000), t.Splits.Count);
            Assert.Equal(t.Summary.DistanceMeters, t.Splits.Sum(x => x.DistanceMeters), 0.5 + t.Splits.Count * 0.05);
        }
    }

    private static IEnumerable<(string File, double Distance)> DemoRunDistances()
    {
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(DemoDirectory, "runs.json")));
        foreach (var run in json.RootElement.GetProperty("runs").EnumerateArray())
        {
            if (run.TryGetProperty("gpx", out var gpx) && run.TryGetProperty("distanceMeters", out var d))
            {
                yield return (Path.GetFileName(gpx.GetProperty("file").GetString()!), d.GetDouble());
            }
        }
    }
}
