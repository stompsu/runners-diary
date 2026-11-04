using RunnersDiary.Domain.Insights;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

/// <summary>QA pass for M5 step 6: ADR 0024 edge cases the per-metric suites do not reach (ISO week 53, 366-day span, estimated share, empty comparison).</summary>
public sealed class InsightsEdgeCaseTests
{
    [Fact]
    public void A_366_day_range_has_one_daily_point_per_day_whole_iso_weeks_and_thirteen_months()
    {
        // 2027-01-01 .. 2028-01-01 inclusive is 366 days (2027 has 365).
        var report = Build("2027-01-01", "2028-01-01", "2030-01-01", Daily(D("2026-11-01"), 800, 60, "easy"), ComparisonKind.Previous, historyStart: D("2026-11-01"));

        Assert.Equal(366, report.Period.Days);
        Assert.Equal(366, report.Period.ElapsedDays);
        Assert.False(report.Period.IsPartial);
        // Daily runs Monday(from)..min(Sunday(to), asOf): 2027-01-01 is a Friday, 2028-01-01 a Saturday and asOf = to.
        Assert.Equal(D("2026-12-28"), report.Load.Daily[0].Date);
        Assert.Equal(D("2028-01-01"), report.Load.Daily[^1].Date);
        Assert.Equal(370, report.Load.Daily.Count); // 2026-12-28 .. 2028-01-01
        Assert.All(report.Weeks, w => Assert.Equal(DayOfWeek.Monday, w.Start.DayOfWeek));
        Assert.Equal(53, report.Weeks.Count); // W53 2026 .. W52 2027 .. includes 2027-12-27..2028-01-02 (W52)
        Assert.Equal(13, report.Months.Count);
        Assert.NotNull(report.Comparison);
        Assert.Equal(D("2025-12-31"), report.Comparison.From); // 366 days back (own length), not a whole-month shift
    }

    [Fact]
    public void Iso_week_53_is_a_normal_complete_week_with_monotony_and_a_364_day_previous_year_window()
    {
        // 2026-W53 = Mon 2026-12-28 .. Sun 2027-01-03. Alternating 300/0 gives 0.87 monotony (see MonotonyTests).
        var facts = new[] { Run("2026-12-28", seconds: 3600, rpe: 5, id: 1), Run("2026-12-30", seconds: 3600, rpe: 5, id: 2), Run("2027-01-01", seconds: 3600, rpe: 5, id: 3) };
        var report = Build("2026-12-28", "2027-01-03", "2027-02-01", facts, ComparisonKind.PreviousYear);

        var week = Assert.Single(report.Weeks);
        Assert.Equal((2026, 53), (week.IsoYear, week.IsoWeek));
        Assert.True(week.Complete);
        Assert.NotNull(week.Monotony);
        Assert.NotNull(report.Comparison);
        Assert.Equal(D("2025-12-29"), report.Comparison.From); // 2026-W01 start
        Assert.Equal(D("2026-01-04"), report.Comparison.To);
    }

    [Fact]
    public void The_first_iso_week_of_2027_compares_with_the_same_weekdays_of_2026_w02()
    {
        var window = ComparisonWindow.For(new InsightsPeriod(D("2027-01-04"), D("2027-01-10")), ComparisonKind.PreviousYear, D("2030-01-01"));

        Assert.NotNull(window);
        Assert.Equal(D("2026-01-05"), window.From);
        Assert.Equal(D("2026-01-11"), window.To);
    }

    [Fact]
    public void Daily_points_are_consecutive_across_dst_changes()
    {
        // Dates are plain local dates (ADR 0008): the Warsaw changes on 2026-03-29 and 2026-10-25 must not drop or repeat a day.
        foreach (var (from, to) in new[] { ("2026-03-23", "2026-03-29"), ("2026-10-19", "2026-10-25"), ("2026-03-30", "2026-04-05") })
        {
            var report = Build(from, to, "2026-12-01", [Run(to, seconds: 3600, rpe: 5, id: 1)]);

            Assert.Equal(7, report.Load.Daily.Count);
            for (var i = 1; i < 7; i++)
            {
                Assert.Equal(report.Load.Daily[i - 1].Date.AddDays(1), report.Load.Daily[i].Date);
            }
        }
    }

    [Fact]
    public void Estimated_load_share_is_the_estimated_part_of_all_load_rounded_to_two_decimals()
    {
        // easy default RPE 3 x 3600 s = 10800 estimated; RPE 9 x 3600 s = 32400 explicit -> 10800 / 43200 = 0.25.
        var facts = new[] { Run("2026-11-02", seconds: 3600, id: 1), Run("2026-11-03", seconds: 3600, rpe: 9, id: 2) };
        var quality = Build("2026-11-02", "2026-11-08", "2026-11-08", facts).DataQuality;

        Assert.Equal(2, quality.CompletedSessions);
        Assert.Equal(1, quality.SessionsWithEstimatedRpe);
        Assert.Equal(0.25, quality.EstimatedLoadShare);

        // 1/3 rounds to 0.33.
        var third = new[] { Run("2026-11-02", seconds: 3600, id: 1), Run("2026-11-03", seconds: 7200, rpe: 3, id: 2) };
        Assert.Equal(0.33, Build("2026-11-02", "2026-11-08", "2026-11-08", third).DataQuality.EstimatedLoadShare);
    }

    [Fact]
    public void A_session_without_duration_never_counts_as_estimated_and_does_not_move_the_share()
    {
        var facts = new[] { Run("2026-11-02", meters: 5000, id: 1), Run("2026-11-03", seconds: 3600, rpe: 5, id: 2) };
        var quality = Build("2026-11-02", "2026-11-08", "2026-11-08", facts).DataQuality;

        Assert.Equal(1, quality.SessionsWithoutDuration);
        Assert.Equal(0, quality.SessionsWithEstimatedRpe);
        Assert.Equal(0.0, quality.EstimatedLoadShare);
    }

    [Fact]
    public void A_report_with_no_load_at_all_has_zero_estimated_share_not_nan()
    {
        var quality = Build("2026-11-02", "2026-11-08", "2026-11-08", []).DataQuality;

        Assert.Equal(0.0, quality.EstimatedLoadShare);
        Assert.False(double.IsNaN(quality.EstimatedLoadShare));
    }

    [Fact]
    public void A_previous_year_comparison_with_no_runs_then_has_null_percent_everywhere_and_absolute_equals_current()
    {
        var current = new[] { Run("2026-11-02", meters: 10000, seconds: 3000, rpe: 5, gain: 100, id: 1) };
        var report = Build("2026-11-01", "2026-11-30", "2026-12-15", current, ComparisonKind.PreviousYear);

        Assert.NotNull(report.Comparison);
        Assert.Equal(D("2025-11-01"), report.Comparison.From);
        var d = report.Comparison.Deltas;
        Assert.All(new[] { d.RunCount, d.DistanceMeters, d.DurationSeconds, d.ElevationGainMeters, d.Load, d.ActiveDays }, delta => Assert.Null(delta.Percent));
        Assert.Equal(10000, d.DistanceMeters.Absolute);
        Assert.Equal(1, d.RunCount.Absolute);
    }

    [Fact]
    public void The_feb_29_partial_year_to_date_compares_the_same_number_of_elapsed_days()
    {
        // 2028-02-29 is day 60 of the year; the 2027 window must also be 60 days (1 Jan .. 1 Mar), not 59.
        var window = ComparisonWindow.For(new InsightsPeriod(D("2028-01-01"), D("2028-12-31")), ComparisonKind.PreviousYear, D("2028-02-29"));

        Assert.NotNull(window);
        Assert.Equal(D("2027-01-01"), window.From);
        Assert.Equal(60, window.To.DayNumber - window.From.DayNumber + 1);
    }
}
