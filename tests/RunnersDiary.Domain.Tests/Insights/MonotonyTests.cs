using RunnersDiary.Domain.Insights;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

public sealed class MonotonyTests
{
    private static long[] Week(params int[] au) => [.. au.Select(x => x * 60L)];

    [Fact]
    public void Alternating_week_is_0_87_monotony_and_779_4_strain()
    {
        var (monotony, strain) = WeeklyMonotony.Compute(Week(300, 0, 300, 0, 300, 0, 0));

        Assert.Equal(0.87, monotony);
        Assert.Equal(779.4, strain);
    }

    [Fact]
    public void A_week_with_one_big_day_is_hand_computed()
    {
        // mean 68.571, population sigma 20.996 -> 3.27; strain = 480 x 3.266 = 1567.7
        var (monotony, strain) = WeeklyMonotony.Compute(Week(60, 60, 60, 60, 60, 60, 120));

        Assert.Equal(3.27, monotony);
        Assert.Equal(1567.7, strain);
    }

    [Fact]
    public void A_constant_week_or_an_empty_week_has_no_monotony()
    {
        Assert.Equal((null, null), WeeklyMonotony.Compute(Week(100, 100, 100, 100, 100, 100, 100)));
        Assert.Equal((null, null), WeeklyMonotony.Compute(Week(0, 0, 0, 0, 0, 0, 0)));
    }

    [Fact]
    public void A_week_needs_seven_days() =>
        Assert.Throws<ArgumentException>(() => WeeklyMonotony.Compute(Week(1, 2, 3)));

    [Fact]
    public void Only_complete_weeks_get_a_value_the_current_week_stays_null()
    {
        // Mon 2026-11-02 .. Sun 11-08 is the current week (today = Wed 11-04); the week before is complete.
        var facts = new List<RunFact>
        {
            Run("2026-10-26", "easy", seconds: 3600, rpe: 5, id: 1),
            Run("2026-10-28", "easy", seconds: 1800, rpe: 5, id: 2),
            Run("2026-10-30", "easy", seconds: 5400, rpe: 5, id: 3),
            Run("2026-11-02", "easy", seconds: 3600, rpe: 5, id: 4),
        };

        var report = Build("2026-10-26", "2026-11-08", "2026-11-04", facts);

        Assert.Equal(2, report.Weeks.Count);
        Assert.True(report.Weeks[0].Complete);
        Assert.NotNull(report.Weeks[0].Monotony);
        Assert.NotNull(report.Weeks[0].Strain);
        Assert.False(report.Weeks[1].Complete);
        Assert.Null(report.Weeks[1].Monotony);
        Assert.Null(report.Weeks[1].Strain);
        // Daily loads 300, 0, 150, 0, 450, 0, 0 AU: 900 / sqrt(7 x 315000 - 900^2) = 0.76; strain 900 x 0.762 = 685.8
        Assert.Equal(900.0, report.Weeks[0].Load);
        Assert.Equal(0.76, report.Weeks[0].Monotony);
        Assert.Equal(685.8, report.Weeks[0].Strain);
        Assert.Equal(300.0, report.Weeks[1].Load);
    }
}
