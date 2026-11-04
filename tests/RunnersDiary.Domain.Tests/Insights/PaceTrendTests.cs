using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

public sealed class PaceTrendTests
{
    private static RunFact Easy(string date, double meters, int seconds, int id, Surface? surface = null, int? hr = null, string type = "easy") =>
        Run(date, type, meters, seconds, rpe: 3, surface: surface, hr: hr, id: id);

    [Fact]
    public void A_steadily_improving_pace_has_a_slope_of_minus_three_seconds_per_week()
    {
        // 10 km at 360, 357, 354 and 351 s/km in four consecutive weeks.
        var facts = new[]
        {
            Easy("2026-10-06", 10_000, 3600, 1),
            Easy("2026-10-13", 10_000, 3570, 2),
            Easy("2026-10-20", 10_000, 3540, 3),
            Easy("2026-10-27", 10_000, 3510, 4),
        };

        var trend = PaceTrend.Compute(facts);

        Assert.Equal(-3.00, trend.SlopeSecondsPerKmPerWeek);
        Assert.Equal(["easy"], trend.RunTypeCodes);
        Assert.Equal([360.0, 357.0, 354.0, 351.0], trend.Weeks.Select(w => w.PaceSecondsPerKm).ToArray());
        Assert.Equal(0, trend.ExcludedRunCount);
    }

    [Fact]
    public void Empty_weeks_are_skipped_not_zero()
    {
        // Weeks 0, 1, 3 and 4: the slope uses the real week indices, so a perfectly linear -3/week line stays -3.
        var facts = new[]
        {
            Easy("2026-10-06", 10_000, 3600, 1),
            Easy("2026-10-13", 10_000, 3570, 2),
            Easy("2026-10-27", 10_000, 3510, 3),
            Easy("2026-11-03", 10_000, 3480, 4),
        };

        Assert.Equal(-3.00, PaceTrend.Compute(facts).SlopeSecondsPerKmPerWeek);
    }

    [Fact]
    public void Fewer_than_four_weekly_points_give_no_slope()
    {
        var facts = new[] { Easy("2026-10-06", 10_000, 3600, 1), Easy("2026-10-13", 10_000, 3570, 2), Easy("2026-10-20", 10_000, 3540, 3) };

        var trend = PaceTrend.Compute(facts);

        Assert.Null(trend.SlopeSecondsPerKmPerWeek);
        Assert.Equal(3, trend.Weeks.Count);
    }

    [Fact]
    public void Weekly_pace_is_distance_weighted_not_a_mean_of_paces()
    {
        // 5 km at 300 s/km and 10 km at 360 s/km: (1500 + 3600) / 15 km = 340.0 (the mean of the paces would be 330).
        var facts = new[] { Easy("2026-11-02", 5000, 1500, 1), Easy("2026-11-04", 10_000, 3600, 2) };

        var week = Assert.Single(PaceTrend.Compute(facts).Weeks);

        Assert.Equal(340.0, week.PaceSecondsPerKm);
        Assert.Equal(2, week.RunCount);
        Assert.Equal(15_000, week.DistanceMeters);
        Assert.Equal(5100, week.DurationSeconds);
        Assert.Equal(D("2026-11-02"), week.Start);
    }

    [Fact]
    public void Trail_short_and_implausible_runs_and_other_types_are_excluded_and_counted()
    {
        var facts = new[]
        {
            Easy("2026-11-02", 10_000, 3600, 1),                             // used
            Easy("2026-11-02", 10_000, 4200, 2, surface: Surface.Trail),     // trail
            Easy("2026-11-03", 800, 300, 3),                                 // shorter than 1 km
            Easy("2026-11-03", 10_000, 1000, 4),                             // 100 s/km: data-entry error
            Easy("2026-11-04", 1000, 1000, 5),                               // 1000 s/km: data-entry error
            Run("2026-11-04", "easy", meters: 5000, rpe: 3, id: 6),          // no duration
            Easy("2026-11-05", 10_000, 3000, 7, type: "recovery"),           // another type: not counted at all
            Easy("2026-11-05", 10_000, 3000, 8, type: "long"),
        };

        var trend = PaceTrend.Compute(facts);

        Assert.Equal(5, trend.ExcludedRunCount);
        var week = Assert.Single(trend.Weeks);
        Assert.Equal(1, week.RunCount);
        Assert.Equal(360.0, week.PaceSecondsPerKm);
    }

    [Fact]
    public void Pace_limits_are_inclusive()
    {
        var facts = new[] { Easy("2026-11-02", 10_000, 1500, 1), Easy("2026-11-03", 1000, 900, 2) };

        var trend = PaceTrend.Compute(facts);

        Assert.Equal(0, trend.ExcludedRunCount); // 150 and 900 s/km are in range
        Assert.Equal(2, Assert.Single(trend.Weeks).RunCount);
    }

    [Fact]
    public void Heart_rate_is_weighted_by_duration_over_runs_that_have_one()
    {
        var facts = new[]
        {
            Easy("2026-11-02", 3000, 1000, 1, hr: 140),
            Easy("2026-11-03", 9000, 3000, 2, hr: 160),
            Easy("2026-11-04", 5000, 1700, 3),             // no heart rate: ignored for the average
        };
        var noHr = new[] { Easy("2026-11-09", 5000, 1700, 4) };

        var with = Assert.Single(PaceTrend.Compute(facts).Weeks);
        var without = Assert.Single(PaceTrend.Compute(noHr).Weeks);

        Assert.Equal(155.0, with.AvgHeartRate); // (140 x 1000 + 160 x 3000) / 4000
        Assert.Null(without.AvgHeartRate);
    }
}
