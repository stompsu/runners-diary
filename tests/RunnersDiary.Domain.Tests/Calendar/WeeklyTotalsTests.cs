using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Calendar;

public sealed class WeeklyTotalsTests
{
    private const short Easy = 20;
    private const short Long = 22;
    private const short CrossTraining = 40;
    private const short Rest = 41;

    [Fact]
    public void Splits_planned_and_completed_and_excludes_non_running_types()
    {
        var grid = CalendarGrid.ForMonth(2026, 11); // 2026-10-26 .. 2026-12-06
        var runs = new[]
        {
            new RunSummary(new DateOnly(2026, 11, 2), RunStatus.Completed, Easy, 10000, 3000, 50),
            new RunSummary(new DateOnly(2026, 11, 8), RunStatus.Completed, Easy, 5000, 1500, null),
            new RunSummary(new DateOnly(2026, 11, 4), RunStatus.Planned, Long, 20000, 7000, 120),
            new RunSummary(new DateOnly(2026, 11, 3), RunStatus.Completed, CrossTraining, 5000, 1800, 10),
            new RunSummary(new DateOnly(2026, 11, 5), RunStatus.Completed, Rest, null, null, null),
            new RunSummary(new DateOnly(2026, 12, 20), RunStatus.Completed, Easy, 99999, 99, 99),
        };

        var weeks = WeeklyTotals.Compute(grid, runs);

        Assert.Equal(grid.WeekCount, weeks.Count);
        var w = weeks.Single(x => x.Week == new IsoWeek(2026, 45));
        Assert.Equal(new DateOnly(2026, 11, 2), w.Start);
        Assert.Equal(new DateOnly(2026, 11, 8), w.End);
        Assert.Equal(new Totals(2, 15000, 4500, 50), w.Completed);
        Assert.Equal(new Totals(1, 20000, 7000, 120), w.Planned);
        Assert.All(weeks.Where(x => x.Week != w.Week), x =>
        {
            Assert.Equal(Totals.Empty, x.Completed);
            Assert.Equal(Totals.Empty, x.Planned);
        });
    }

    [Fact]
    public void Sums_durations_beyond_int_range_without_wrapping()
    {
        var grid = CalendarGrid.ForMonth(2026, 11);
        var runs = new[]
        {
            new RunSummary(new DateOnly(2026, 11, 2), RunStatus.Completed, Easy, 1, int.MaxValue, 1),
            new RunSummary(new DateOnly(2026, 11, 3), RunStatus.Completed, Easy, 1, int.MaxValue, 1),
            new RunSummary(new DateOnly(2026, 11, 4), RunStatus.Completed, Easy, 1, int.MaxValue, 1),
        };

        var week = WeeklyTotals.Compute(grid, runs).Single(x => x.Week == new IsoWeek(2026, 45));

        Assert.Equal(3L * int.MaxValue, week.Completed.DurationSeconds);
    }
}
