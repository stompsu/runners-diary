using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Tracks;

namespace RunnersDiary.Domain.Tests.Demo;

/// <summary>Guards the guarantees of the real <c>seed/demo/runs.json</c> (M2 plan §6, AC5) for every "today" of a whole leap year.</summary>
public sealed class DemoDatasetTests
{
    private static readonly string DemoDirectory = FindDemoDirectory();
    private static readonly DemoTemplate Template = DemoTemplateParser.Parse(File.ReadAllText(Path.Combine(DemoDirectory, "runs.json")));

    // 2028 is a leap year and contains both Europe/Warsaw DST changes and every month boundary.
    public static TheoryData<DateOnly> Todays()
    {
        var data = new TheoryData<DateOnly>();
        for (var day = new DateOnly(2028, 1, 1); day.Year == 2028; day = day.AddDays(1))
        {
            data.Add(day);
        }

        return data;
    }

    [Fact]
    public void Dataset_has_the_documented_shape()
    {
        Assert.Equal("Europe/Warsaw", Template.TimeZoneId);
        Assert.InRange(Template.Runs.Count, 100, 130);
        Assert.InRange(Template.Runs.Min(r => r.Week), -30, -20);
        Assert.InRange(Template.Runs.Max(r => r.Week), 4, 6);
    }

    [Fact]
    public void Every_referenced_gpx_file_exists_and_is_small()
    {
        var references = Template.Runs.Where(r => r.Gpx is not null).Select(r => r.Gpx!.File).Distinct().ToList();

        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            var path = Path.GetFullPath(Path.Combine(DemoDirectory, reference));
            Assert.StartsWith(DemoDirectory, path, StringComparison.Ordinal);
            Assert.True(File.Exists(path), $"Missing demo GPX '{reference}'.");
            Assert.True(new FileInfo(path).Length is > 0 and < 200_000, $"Demo GPX '{reference}' has an unexpected size.");
        }
    }

    [Fact]
    public void Recorded_tracks_belong_to_completed_races_and_courses_to_planned_ones()
    {
        foreach (var run in Template.Runs.Where(r => r.Gpx is not null))
        {
            Assert.Equal(RunTypeCategory.Race, RunTypeCatalog.FindByCode(run.TypeCode)!.Category);
            Assert.Equal(run.Gpx!.Kind == GpxTrackKind.RecordedTrack ? RunStatus.Completed : RunStatus.Planned, run.Status);
        }
    }

    [Theory]
    [MemberData(nameof(Todays))]
    public void Guarantees_hold_for_every_today(DateOnly today)
    {
        var runs = DemoScheduleExpander.Expand(Template, today);

        // Every entry satisfies the same rules the API enforces.
        Assert.All(runs, r => Assert.Empty(RunRules.Validate(r.Values)));

        // The dataset fits the default guest run caps (a guest can still add runs on top).
        Assert.True(runs.Count < RunLimitDefaults.GuestMaxRuns / 2, $"{runs.Count} demo runs leave too little room under the guest cap.");
        Assert.True(runs.GroupBy(r => r.Values.Date).Max(g => g.Count()) <= RunLimitDefaults.MaxRunsPerDate / 2);

        // History is history, the future is a plan.
        Assert.DoesNotContain(runs, r => r.Values.Date > today && r.Values.Status == RunStatus.Completed);

        // The current month is never empty (calendar looks alive on every day of the year).
        var inMonth = runs.Count(r => r.Values.Date.Year == today.Year && r.Values.Date.Month == today.Month
            && !RunTypeCatalog.Get(r.Values.RunTypeId).IsRestDay);
        Assert.True(inMonth >= 4, $"Only {inMonth} runs in {today:yyyy-MM}.");

        // Races: at least three completed with track, result and rating; one or two planned with a course.
        var completedRaces = runs.Where(r => r.Values.Status == RunStatus.Completed
            && RunTypeCatalog.Get(r.Values.RunTypeId).Category == RunTypeCategory.Race).ToList();
        Assert.True(completedRaces.Count >= 3);
        Assert.All(completedRaces, r =>
        {
            Assert.Equal(GpxTrackKind.RecordedTrack, r.Gpx?.Kind);
            Assert.NotNull(r.Values.Rating);
            Assert.NotNull(r.Values.Race?.ResultTimeSeconds);
        });

        var plannedRaces = runs.Where(r => r.Values.Status == RunStatus.Planned && r.Values.Date > today
            && RunTypeCatalog.Get(r.Values.RunTypeId).Category == RunTypeCategory.Race).ToList();
        Assert.InRange(plannedRaces.Count, 1, 2);
        Assert.All(plannedRaces, r => Assert.Equal(GpxTrackKind.PlannedCourse, r.Gpx?.Kind));

        // Load story: one spike week and one taper week, both before the current week (so they do not depend on today).
        var weeks = CompletedWeeklyKilometres(runs, today);
        var spike = weeks.Keys.Where(m => Ratio(weeks, m) >= 1.3).ToList();
        Assert.NotEmpty(spike);

        var raceWeeks = completedRaces.Select(r => IsoWeek.MondayOf(r.Values.Date)).ToHashSet();
        var taper = weeks.Keys.Where(m => raceWeeks.Contains(m.AddDays(7)) && Ratio(weeks, m) <= 0.75).ToList();
        Assert.NotEmpty(taper);
    }

    /// <summary>Completed running kilometres per ISO week, for the weeks that ended before the current one.</summary>
    private static Dictionary<DateOnly, double> CompletedWeeklyKilometres(IReadOnlyList<DemoRun> runs, DateOnly today)
    {
        var currentMonday = IsoWeek.MondayOf(today);
        return runs
            .Where(r => r.Values.Status == RunStatus.Completed
                && RunTypeCatalog.Get(r.Values.RunTypeId).CountsTowardRunningDistance
                && IsoWeek.MondayOf(r.Values.Date) < currentMonday)
            .GroupBy(r => IsoWeek.MondayOf(r.Values.Date))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Values.DistanceMeters ?? 0) / 1000);
    }

    /// <summary>A week's volume relative to the mean of the four weeks before it (0 when there is no full history).</summary>
    private static double Ratio(Dictionary<DateOnly, double> weeks, DateOnly monday)
    {
        var previous = Enumerable.Range(1, 4).Select(i => weeks.GetValueOrDefault(monday.AddDays(-7 * i))).ToList();
        if (previous.Any(v => v <= 0))
        {
            return 0;
        }

        return weeks[monday] / previous.Average();
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
