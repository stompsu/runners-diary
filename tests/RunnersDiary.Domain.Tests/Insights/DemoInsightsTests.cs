using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

/// <summary>The real <c>seed/demo/runs.json</c> through the metrics (M5 plan AC3): the load story must hold for every "today".</summary>
public sealed class DemoInsightsTests
{
    private static readonly string DemoDirectory = FindDemoDirectory();
    private static readonly DemoTemplate Template = DemoTemplateParser.Parse(File.ReadAllText(Path.Combine(DemoDirectory, "runs.json")));

    public static TheoryData<DateOnly> Todays()
    {
        var data = new TheoryData<DateOnly>();
        for (var day = new DateOnly(2028, 1, 1); day.Year == 2028; day = day.AddDays(1))
        {
            data.Add(day);
        }

        return data;
    }

    /// <summary>The demo dataset as a guest would have it on <paramref name="today"/>, as metrics inputs with deterministic ids.</summary>
    internal static (List<RunFact> Facts, List<RaceFact> Races) Facts(DateOnly today)
    {
        var facts = new List<RunFact>();
        var races = new List<RaceFact>();
        var index = 0;
        foreach (var run in DemoScheduleExpander.Expand(Template, today))
        {
            var v = run.Values;
            var id = new Guid(++index, 0, 0, new byte[8]);
            facts.Add(new RunFact(id, v.Date, v.Status, v.RunTypeId, v.DistanceMeters, v.DurationSeconds, v.ElevationGainMeters, v.Rpe, v.Surface, v.AvgHeartRate));
            if (v.Race is { } race)
            {
                races.Add(new RaceFact(
                    id, v.Date, v.Status, v.RunTypeId, race.Name, race.DistanceCategory, race.OfficialDistanceMeters, v.DistanceMeters,
                    v.DurationSeconds, race.ResultTimeSeconds, race.TargetTimeSeconds, race.OverallPlace, race.CategoryPlace, race.CategoryName, v.Rating?.Stars));
            }
        }

        return (facts, races);
    }

    /// <summary>The default dashboard view: the last 12 weeks (Monday 77 days back to this Sunday) against the 12 before.</summary>
    internal static InsightsReport TwelveWeeks(DateOnly today)
    {
        var (facts, races) = Facts(today);
        var monday = IsoWeek.MondayOf(today);
        return InsightsReportBuilder.Build(new InsightsPeriod(monday.AddDays(-77), monday.AddDays(6)), ComparisonKind.Previous, today, facts, facts, races, null);
    }

    [Theory]
    [MemberData(nameof(Todays))]
    public void The_load_story_holds_for_every_today(DateOnly today)
    {
        var report = TwelveWeeks(today);
        var monday = IsoWeek.MondayOf(today);

        // Spike: the Sunday of week -11 is "high"; taper: the Sunday of week -8 is "low"; today is available.
        var spike = report.Load.Daily.Single(d => d.Date == monday.AddDays(-71));
        var taper = report.Load.Daily.Single(d => d.Date == monday.AddDays(-50));
        Assert.Equal(AcwrBand.High, spike.Band);
        Assert.True(spike.Acwr >= 1.31, $"Spike week ACWR {spike.Acwr}.");
        Assert.Equal(AcwrBand.Low, taper.Band);
        Assert.True(taper.Acwr < 0.80, $"Taper week ACWR {taper.Acwr}.");
        Assert.Null(report.Load.AtAsOf.UnavailableReason);
        Assert.NotNull(report.Load.AtAsOf.Value);

        // The week buckets agree with the daily series.
        var spikeWeek = report.Weeks.Single(w => w.Start == monday.AddDays(-77));
        Assert.Equal(spike.Acwr, spikeWeek.AcwrAtEnd);

        // Easy-pace trend has enough weekly points; the dataset records RPE on every completed session.
        Assert.True(report.PaceTrend.Weeks.Count >= 4, $"{report.PaceTrend.Weeks.Count} weekly easy-pace points.");
        Assert.NotNull(report.PaceTrend.SlopeSecondsPerKmPerWeek);
        Assert.Equal(0, report.DataQuality.SessionsWithEstimatedRpe);
        Assert.Equal(0.0, report.DataQuality.EstimatedLoadShare);
        Assert.Equal(0, report.DataQuality.SessionsWithoutDuration);

        // Races of the last 26 weeks: at least three completed with a result.
        var (_, races) = Facts(today);
        var results = RaceHistory.Compute(races, new InsightsPeriod(monday.AddDays(-175), monday.AddDays(6)), today)
            .Where(r => r.Status == RunStatus.Completed && r.ResultTimeSeconds is > 0)
            .ToList();
        Assert.True(results.Count >= 3, $"{results.Count} race results in the last 26 weeks.");

        // The default view has a comparison and its band days cover every elapsed day.
        Assert.NotNull(report.Comparison);
        Assert.Equal(report.Period.ElapsedDays, report.Load.BandDays.Low + report.Load.BandDays.Optimal + report.Load.BandDays.High + report.Load.BandDays.Unavailable);
    }

    [Fact]
    public void The_story_numbers_for_a_fixed_today()
    {
        var report = TwelveWeeks(D("2026-11-04"));
        var monday = D("2026-11-02");

        var spike = report.Weeks.Single(w => w.Start == monday.AddDays(-77));
        var taper = report.Weeks.Single(w => w.Start == monday.AddDays(-56));
        Assert.Equal(1.48, spike.AcwrAtEnd);
        Assert.Equal(0.55, taper.AcwrAtEnd);
        Assert.Equal(1440.7, spike.Load);
        Assert.Equal(632.2, taper.Load);
    }

    [Fact]
    public void Easy_pace_improves_from_about_5_55_to_about_5_40_over_26_weeks()
    {
        var today = D("2026-11-04");
        var (facts, races) = Facts(today);
        var monday = IsoWeek.MondayOf(today);

        var report = InsightsReportBuilder.Build(new InsightsPeriod(monday.AddDays(-182), monday.AddDays(6)), ComparisonKind.None, today, facts, [], races, null);
        var weeks = report.PaceTrend.Weeks;

        Assert.InRange(weeks[0].PaceSecondsPerKm, 350, 360);   // ~5:55 /km
        Assert.InRange(weeks[^1].PaceSecondsPerKm, 336, 345);  // ~5:40 /km
        Assert.InRange(report.PaceTrend.SlopeSecondsPerKmPerWeek!.Value, -0.8, -0.4);
        Assert.Equal(0, report.PaceTrend.ExcludedRunCount);
    }

    private static string FindDemoDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "seed", "demo");
            if (File.Exists(Path.Combine(candidate, "runs.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("seed/demo/runs.json was not found above " + AppContext.BaseDirectory);
    }
}
