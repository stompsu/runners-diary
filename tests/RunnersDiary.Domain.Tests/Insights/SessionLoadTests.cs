using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

public sealed class SessionLoadTests
{
    [Fact]
    public void Forty_five_minutes_at_rpe_six_is_270_au()
    {
        var load = SessionLoad.Of(Run("2026-11-02", "tempo", seconds: 2700, rpe: 6));

        Assert.Equal(16_200, load!.Value.RpeSeconds);
        Assert.Equal(270.0, load.Value.Au);
        Assert.False(load.Value.Estimated);
    }

    [Fact]
    public void Missing_rpe_uses_the_type_default_and_is_flagged_as_estimated()
    {
        var load = SessionLoad.Of(Run("2026-11-02", "easy", seconds: 2700));

        Assert.Equal(3 * 2700, load!.Value.RpeSeconds);
        Assert.True(load.Value.Estimated);
    }

    [Fact]
    public void Missing_duration_gives_no_load_and_is_flagged()
    {
        var load = SessionLoad.Of(Run("2026-11-02", "easy", meters: 5000, rpe: 4));

        Assert.Equal(0, load!.Value.RpeSeconds);
        Assert.True(load.Value.MissingDuration);
        Assert.False(load.Value.Estimated);
    }

    [Fact]
    public void Rest_days_have_no_load_at_all()
    {
        Assert.Null(SessionLoad.Of(Run("2026-11-02", "rest")));
    }

    [Fact]
    public void Cross_training_counts_toward_load_but_not_toward_running_totals()
    {
        var facts = new[] { Run("2026-11-02", "crossTraining", seconds: 3000, rpe: 3), Run("2026-11-03", "easy", meters: 5000, seconds: 1800, rpe: 3) };

        var totals = PeriodTotals.Compute(facts);

        Assert.Equal(1, totals.RunCount);
        Assert.Equal(5000, totals.DistanceMeters);
        Assert.Equal(1800, totals.DurationSeconds);
        Assert.Equal((9000 + 5400) / 60.0, totals.Load);
        Assert.Equal(2, totals.LoadSessionCount);
        Assert.Equal(2, totals.ActiveDays);
    }

    [Fact]
    public void Planned_runs_and_completed_runs_in_the_future_never_count()
    {
        var facts = new[]
        {
            Run("2026-11-02", "easy", seconds: 1800, rpe: 4, id: 1),
            Run("2026-11-03", "easy", seconds: 1800, rpe: 4, status: RunStatus.Planned, id: 2),
            Run("2026-11-06", "easy", seconds: 1800, rpe: 4, id: 3), // completed, but after "today"
        };

        var report = Build("2026-11-02", "2026-11-08", "2026-11-04", facts);

        Assert.Equal(1, report.Totals.RunCount);
        Assert.Equal(120.0, report.Totals.Load);
        Assert.Equal(1, report.Planned.RunCount);
        Assert.Equal(1, report.Planned.MissedCount); // dated before today
        Assert.Equal(0, report.Planned.UpcomingCount);
    }

    [Fact]
    public void Sums_are_exact_integers_and_only_rounded_at_the_boundary()
    {
        // One second at RPE 1 is 1/60 AU = 0.0 after rounding, but 100 of them are exactly 100 RPE·s = 1.7 AU.
        var facts = Enumerable.Range(0, 100).Select(i => Run("2026-11-02", "easy", seconds: 1, rpe: 1, id: i)).ToList();

        Assert.Equal(0.0, SessionLoad.Of(facts[0])!.Value.Au);
        Assert.Equal(1.7, PeriodTotals.Compute(facts).Load);
    }

    [Theory]
    [InlineData("recovery", 2)]
    [InlineData("easy", 3)]
    [InlineData("stridesDrills", 3)]
    [InlineData("treadmill", 3)]
    [InlineData("crossTraining", 4)]
    [InlineData("long", 5)]
    [InlineData("trailTraining", 5)]
    [InlineData("fartlek", 6)]
    [InlineData("progression", 6)]
    [InlineData("tempo", 7)]
    [InlineData("racePace", 7)]
    [InlineData("intervals", 8)]
    [InlineData("hillRepeats", 8)]
    [InlineData("ultra", 8)]
    [InlineData("roadRace", 9)]
    [InlineData("trailRace", 9)]
    [InlineData("trackRace", 9)]
    [InlineData("crossCountry", 9)]
    [InlineData("virtualRace", 9)]
    public void Default_rpe_table_v1(string code, int expected) =>
        Assert.Equal(expected, DefaultRpe.For(RunTypeCatalog.FindByCode(code)!));

    [Fact]
    public void Every_load_bearing_type_has_a_documented_default()
    {
        var documented = new HashSet<string>
        {
            "recovery", "easy", "stridesDrills", "treadmill", "crossTraining", "long", "trailTraining", "fartlek", "progression",
            "tempo", "racePace", "intervals", "hillRepeats", "ultra", "roadRace", "trailRace", "trackRace", "crossCountry", "virtualRace",
        };

        Assert.Equal(documented.Order(), RunTypeCatalog.All.Where(t => t.CountsTowardLoad).Select(t => t.Code).Order());
    }
}
