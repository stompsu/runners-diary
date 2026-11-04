using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

public sealed class ComparisonWindowTests
{
    private static readonly string Later = "2030-01-01"; // a "today" after every period below, so none is partial

    public static TheoryData<string, string, string, ComparisonKind, string, string> Windows() => new()
    {
        // whole month -> previous month (30 days vs 31 days)
        { "2026-11-01", "2026-11-30", Later, ComparisonKind.Previous, "2026-10-01", "2026-10-31" },
        { "2026-10-01", "2026-10-31", Later, ComparisonKind.Previous, "2026-09-01", "2026-09-30" },
        // January -> December of the previous year; several whole months shift by their count
        { "2027-01-01", "2027-01-31", Later, ComparisonKind.Previous, "2026-12-01", "2026-12-31" },
        { "2026-10-01", "2026-12-31", Later, ComparisonKind.Previous, "2026-07-01", "2026-09-30" },
        { "2028-03-01", "2028-03-31", Later, ComparisonKind.Previous, "2028-02-01", "2028-02-29" },
        // anything else shifts by its own length (12 weeks = 84 days, a week = 7)
        { "2026-08-17", "2026-11-08", Later, ComparisonKind.Previous, "2026-05-25", "2026-08-16" },
        { "2026-11-02", "2026-11-08", Later, ComparisonKind.Previous, "2026-10-26", "2026-11-01" },
        { "2026-11-05", "2026-11-14", Later, ComparisonKind.Previous, "2026-10-26", "2026-11-04" },
        // partial month: November viewed on 4 Nov is compared with 1-4 Oct, not all of October
        { "2026-11-01", "2026-11-30", "2026-11-04", ComparisonKind.Previous, "2026-10-01", "2026-10-04" },
        // plan example: 12 weeks viewed on Wed 4 Nov = 80 elapsed days
        { "2026-08-17", "2026-11-08", "2026-11-04", ComparisonKind.Previous, "2026-05-25", "2026-08-12" },
        // year to date -> the same calendar dates a year earlier
        { "2026-01-01", "2026-11-04", "2026-11-04", ComparisonKind.PreviousYear, "2025-01-01", "2025-11-04" },
        // whole weeks starting on a Monday -> 364 days (same weekdays)
        { "2026-11-02", "2026-11-08", Later, ComparisonKind.PreviousYear, "2025-11-03", "2025-11-09" },
        { "2026-08-17", "2026-11-08", Later, ComparisonKind.PreviousYear, "2025-08-18", "2025-11-09" },
        // a month that does not start on a Monday -> calendar year
        { "2026-11-01", "2026-11-30", Later, ComparisonKind.PreviousYear, "2025-11-01", "2025-11-30" },
        // 29 February -> 28 February
        { "2028-02-29", "2028-02-29", Later, ComparisonKind.PreviousYear, "2027-02-28", "2027-02-28" },
        { "2028-02-01", "2028-02-29", Later, ComparisonKind.PreviousYear, "2027-02-01", "2027-02-28" },
        // partial week previous year: Mon-Wed elapsed
        { "2026-11-02", "2026-11-08", "2026-11-04", ComparisonKind.PreviousYear, "2025-11-03", "2025-11-05" },
    };

    [Theory]
    [MemberData(nameof(Windows))]
    public void Windows_follow_the_adr_rules(string from, string to, string today, ComparisonKind kind, string expectedFrom, string expectedTo)
    {
        var window = ComparisonWindow.For(new InsightsPeriod(D(from), D(to)), kind, D(today));

        Assert.NotNull(window);
        Assert.Equal(kind, window.Kind);
        Assert.Equal(D(expectedFrom), window.From);
        Assert.Equal(D(expectedTo), window.To);
    }

    [Fact]
    public void None_has_no_window() =>
        Assert.Null(ComparisonWindow.For(new InsightsPeriod(D("2026-11-01"), D("2026-11-30")), ComparisonKind.None, D(Later)));

    [Fact]
    public void A_period_that_has_not_started_has_nothing_to_compare() =>
        Assert.Null(ComparisonWindow.For(new InsightsPeriod(D("2026-12-01"), D("2026-12-31")), ComparisonKind.Previous, D("2026-11-04")));

    [Fact]
    public void Deltas_are_signed_with_percent_rounded_to_one_decimal()
    {
        var current = PeriodTotals.Compute(Enumerable.Range(0, 12).Select(i => Run("2026-11-0" + ((i % 9) + 1), meters: 1000, seconds: 300, rpe: 4, id: i)));
        var previous = PeriodTotals.Compute(Enumerable.Range(0, 10).Select(i => Run("2026-10-0" + ((i % 9) + 1), meters: 1000, seconds: 300, rpe: 4, id: i)));

        var deltas = Deltas.Between(current, previous);

        Assert.Equal(new Delta(2, 20.0), deltas.RunCount);
        Assert.Equal(new Delta(2000, 20.0), deltas.DistanceMeters);
        Assert.Equal(new Delta(600, 20.0), deltas.DurationSeconds);
        Assert.Equal(new Delta(0, null), deltas.ElevationGainMeters); // previous elevation is 0
        Assert.Equal(new Delta(40.0, 20.0), deltas.Load);
        Assert.Equal(new Delta(0, 0.0), deltas.ActiveDays);
    }

    [Fact]
    public void A_previous_value_of_zero_gives_no_percent()
    {
        var current = PeriodTotals.Compute([Run("2026-11-02", meters: 5000, seconds: 1800, rpe: 4)]);

        var deltas = Deltas.Between(current, PeriodTotals.Empty);

        Assert.Equal(new Delta(1, null), deltas.RunCount);
        Assert.Equal(new Delta(5000, null), deltas.DistanceMeters);
        Assert.Equal(new Delta(120.0, null), deltas.Load);
    }

    [Fact]
    public void A_decrease_is_negative()
    {
        var current = PeriodTotals.Compute([Run("2026-11-02", meters: 6000, seconds: 1800, rpe: 5)]);
        var previous = PeriodTotals.Compute([Run("2026-10-02", meters: 8000, seconds: 2400, rpe: 5)]);

        var deltas = Deltas.Between(current, previous);

        Assert.Equal(new Delta(-2000, -25.0), deltas.DistanceMeters);
    }

    [Fact]
    public void The_report_totals_the_comparison_window_over_its_own_facts_only()
    {
        // November viewed on 4 Nov (partial): current 1-4 Nov, comparison 1-4 Oct. Runs on 20 Oct and 5 Nov are outside both.
        var facts = new[]
        {
            Run("2026-11-02", meters: 10_000, seconds: 3000, rpe: 5, id: 1),
            Run("2026-10-02", meters: 8_000, seconds: 2400, rpe: 5, id: 2),
            Run("2026-10-20", meters: 99_000, seconds: 9000, rpe: 5, id: 3),
            Run("2026-11-05", meters: 50_000, seconds: 9000, rpe: 5, id: 4, status: RunStatus.Planned),
        };

        var report = Build("2026-11-01", "2026-11-30", "2026-11-04", facts, ComparisonKind.Previous);

        Assert.NotNull(report.Comparison);
        Assert.Equal((D("2026-10-01"), D("2026-10-04")), (report.Comparison.From, report.Comparison.To));
        Assert.Equal(8_000, report.Comparison.Totals.DistanceMeters);
        Assert.Equal(new Delta(2000, 25.0), report.Comparison.Deltas.DistanceMeters);
    }
}
