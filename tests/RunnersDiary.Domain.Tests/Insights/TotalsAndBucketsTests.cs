using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

public sealed class TotalsAndBucketsTests
{
    [Fact]
    public void Average_pace_uses_only_runs_with_both_distance_and_duration()
    {
        var facts = new[]
        {
            Run("2026-11-02", meters: 10_000, seconds: 3600, id: 1),
            Run("2026-11-03", meters: 5_000, seconds: 1500, id: 2),
            Run("2026-11-04", meters: 8_000, id: 3),          // no duration: counted, not in the pace
            Run("2026-11-05", seconds: 1800, id: 4),          // no distance: counted, not in the pace
        };

        var totals = PeriodTotals.Compute(facts);

        Assert.Equal(4, totals.RunCount);
        Assert.Equal(23_000, totals.DistanceMeters);
        Assert.Equal(6900, totals.DurationSeconds);
        Assert.Equal(340.0, totals.AveragePaceSecondsPerKm); // 5100 s / 15 km
        Assert.Equal(10_000, totals.LongestRunMeters);
    }

    [Fact]
    public void Active_days_are_distinct_and_rest_days_are_counted_separately()
    {
        var facts = new[]
        {
            Run("2026-11-02", seconds: 1800, rpe: 3, id: 1),
            Run("2026-11-02", "crossTraining", seconds: 1800, rpe: 3, id: 2), // same day
            Run("2026-11-03", "rest", id: 3),
            Run("2026-11-04", "tempo", seconds: 1800, rpe: 7, id: 4),
        };

        var totals = PeriodTotals.Compute(facts);

        Assert.Equal(2, totals.ActiveDays);
        Assert.Equal(1, totals.RestDays);
        Assert.Equal(2, totals.RunCount); // cross-training is not a running session
    }

    [Fact]
    public void No_runs_give_empty_totals_with_null_pace_and_longest()
    {
        var totals = PeriodTotals.Compute([]);

        Assert.Equal(0, totals.RunCount);
        Assert.Null(totals.AveragePaceSecondsPerKm);
        Assert.Null(totals.LongestRunMeters);
        Assert.Equal(0.0, totals.Load);
    }

    [Fact]
    public void Week_buckets_are_whole_iso_weeks_across_a_year_boundary()
    {
        // 2026 has 53 ISO weeks: 2026-12-28 starts 2026-W53, which ends on Sunday 2027-01-03; 2027-W01 starts on 2027-01-04.
        var facts = new[]
        {
            Run("2026-12-30", meters: 5000, seconds: 1800, rpe: 3, id: 1),
            Run("2027-01-02", meters: 6000, seconds: 2100, rpe: 3, id: 2),
            Run("2027-01-05", meters: 7000, seconds: 2400, rpe: 3, id: 3),
        };

        var report = Build("2026-12-31", "2027-01-05", "2027-02-01", facts);

        Assert.Equal(2, report.Weeks.Count);
        Assert.Equal((2026, 53, D("2026-12-28"), D("2027-01-03")), (report.Weeks[0].IsoYear, report.Weeks[0].IsoWeek, report.Weeks[0].Start, report.Weeks[0].End));
        Assert.Equal((2027, 1, D("2027-01-04"), D("2027-01-10")), (report.Weeks[1].IsoYear, report.Weeks[1].IsoWeek, report.Weeks[1].Start, report.Weeks[1].End));
        Assert.Equal(2, report.Weeks[0].Completed.RunCount); // the bucket extends before the period start
        Assert.Equal(11_000, report.Weeks[0].Completed.DistanceMeters);
        Assert.Equal(1, report.Weeks[1].Completed.RunCount);
        Assert.Equal(2, report.Totals.RunCount); // the period totals start on 31 Dec
    }

    [Fact]
    public void Month_buckets_cover_whole_calendar_months_across_december_and_january()
    {
        var facts = new[]
        {
            Run("2026-12-01", meters: 5000, seconds: 1800, rpe: 5, id: 1),
            Run("2026-12-31", meters: 6000, seconds: 2100, rpe: 5, id: 2),
            Run("2027-01-10", meters: 7000, seconds: 2400, rpe: 5, id: 3),
            Run("2027-01-20", meters: 8000, seconds: 3000, rpe: 5, id: 4, status: RunStatus.Planned),
        };

        var report = Build("2026-12-15", "2027-01-15", "2027-02-01", facts);

        Assert.Equal(2, report.Months.Count);
        Assert.Equal((2026, 12, D("2026-12-01"), D("2026-12-31")), (report.Months[0].Year, report.Months[0].Month, report.Months[0].Start, report.Months[0].End));
        Assert.Equal((2027, 1, D("2027-01-01"), D("2027-01-31")), (report.Months[1].Year, report.Months[1].Month, report.Months[1].Start, report.Months[1].End));
        Assert.Equal(11_000, report.Months[0].Completed.DistanceMeters);
        Assert.Equal(2, report.Months[0].Completed.RunCount);
        Assert.Equal((5 * 1800 + 5 * 2100) / 60.0, report.Months[0].Load);
        Assert.Equal(7000, report.Months[1].Completed.DistanceMeters);
        Assert.Equal(8000, report.Months[1].Planned.DistanceMeters);
        Assert.Equal(1, report.Months[1].Planned.RunCount);
    }

    [Fact]
    public void A_past_month_viewed_later_has_its_last_week_complete_up_to_the_bucket_end()
    {
        // October 2026 viewed on 2026-11-04: the last week 2026-10-26..11-01 ends after the period but is already past.
        var facts = new[]
        {
            Run("2026-08-20", meters: 5000, seconds: 1800, rpe: 4, id: 5), // gives the ACWR its 35 days of history
            Run("2026-10-12", meters: 5000, seconds: 1800, rpe: 4, id: 1),
            Run("2026-10-26", meters: 6000, seconds: 2100, rpe: 5, id: 2),
            Run("2026-10-28", meters: 7000, seconds: 2400, rpe: 6, id: 3),
            Run("2026-11-01", meters: 9000, seconds: 3000, rpe: 5, id: 4),
        };

        var report = Build("2026-10-01", "2026-10-31", "2026-11-04", facts);

        var last = report.Weeks[^1];
        Assert.Equal((D("2026-10-26"), D("2026-11-01")), (last.Start, last.End));
        Assert.True(last.Complete);
        Assert.Equal(3, last.Completed.RunCount); // includes Sunday 1 Nov
        Assert.Equal(22_000, last.Completed.DistanceMeters);
        Assert.NotNull(last.Monotony);
        Assert.NotNull(last.Strain);
        Assert.Equal(D("2026-10-31"), report.Period.AsOf);
        Assert.Equal(3, report.Totals.RunCount); // period totals stay on [from, asOf]: 12, 26 and 28 Oct
        Assert.Equal(D("2026-10-31"), report.Load.Daily[^1].Date);
        Assert.Equal(last.Load, Math.Round((5 * 2100 + 6 * 2400 + 5 * 3000) / 60.0, 1));
        Assert.NotNull(last.AcwrAtEnd);
    }

    [Fact]
    public void The_current_week_is_cut_at_today_and_stays_incomplete()
    {
        var facts = new[]
        {
            Run("2026-11-02", meters: 5000, seconds: 1800, rpe: 4, id: 1),
            Run("2026-11-04", meters: 6000, seconds: 2100, rpe: 5, id: 2),
            Run("2026-11-06", meters: 7000, seconds: 2400, rpe: 6, id: 3, status: RunStatus.Planned),
        };

        var report = Build("2026-10-01", "2026-11-30", "2026-11-04", facts);

        var week = report.Weeks.Single(w => w.Start == D("2026-11-02"));
        Assert.False(week.Complete);
        Assert.Equal(2, week.Completed.RunCount);
        Assert.Equal(1, week.Planned.RunCount);
        Assert.Null(week.Monotony);
        Assert.Null(week.Strain);
        var future = report.Weeks[^1];
        Assert.False(future.Complete);
        Assert.Equal(0, future.Completed.RunCount);
        Assert.Null(future.AcwrAtEnd);
    }

    [Fact]
    public void A_past_month_bucket_includes_days_after_the_period_end_up_to_the_month_end()
    {
        var facts = new[]
        {
            Run("2026-10-10", meters: 5000, seconds: 1800, rpe: 5, id: 1),
            Run("2026-10-30", meters: 6000, seconds: 2100, rpe: 5, id: 2),
            Run("2026-11-02", meters: 7000, seconds: 2400, rpe: 5, id: 3),
        };

        // The period ends 10 Oct; the October bucket still holds the whole month (it is past), November stops at today.
        var report = Build("2026-10-01", "2026-11-03", "2026-11-04", facts);
        Assert.Equal(11_000, report.Months[0].Completed.DistanceMeters);
        Assert.Equal(7000, report.Months[1].Completed.DistanceMeters);

        var shortPeriod = Build("2026-10-01", "2026-10-15", "2026-11-04", facts);
        Assert.Single(shortPeriod.Months);
        Assert.Equal(11_000, shortPeriod.Months[0].Completed.DistanceMeters); // 30 Oct is after the period but inside the bucket
        Assert.Equal(1, shortPeriod.Totals.RunCount); // period totals stay on [from, asOf]
    }

    [Fact]
    public void An_empty_period_still_returns_zeros_and_empty_lists_where_applicable()
    {
        var report = Build("2026-11-02", "2026-11-08", "2026-11-04", []);

        Assert.Equal(MetricsVersion.Current, report.MetricsVersion);
        Assert.Equal(0, report.Totals.RunCount);
        Assert.Equal(0, report.DataQuality.CompletedSessions);
        Assert.Empty(report.Races);
        Assert.Empty(report.TypeDistribution);
        Assert.Empty(report.PaceTrend.Weeks);
        Assert.Null(report.PaceTrend.SlopeSecondsPerKmPerWeek);
        Assert.Null(report.Load.HistoryStart);
        Assert.Equal(AcwrUnavailableReason.InsufficientHistory, report.Load.AtAsOf.UnavailableReason);
        Assert.Single(report.Weeks);
        Assert.Single(report.Months);
    }

    [Fact]
    public void A_partial_period_reports_asof_elapsed_days_and_cuts_everything_at_today()
    {
        var report = Build("2026-11-01", "2026-11-30", "2026-11-04", []);

        Assert.True(report.Period.IsPartial);
        Assert.Equal(D("2026-11-04"), report.Period.AsOf);
        Assert.Equal(30, report.Period.Days);
        Assert.Equal(4, report.Period.ElapsedDays);
        Assert.Equal(D("2026-11-04"), report.Load.AtAsOf.Date);
        Assert.Equal(D("2026-11-04"), report.Load.Daily[^1].Date);
        Assert.Equal(D("2026-10-26"), report.Load.Daily[0].Date); // Monday of the first week
    }

    [Fact]
    public void A_period_entirely_in_the_past_is_not_partial()
    {
        var report = Build("2026-10-01", "2026-10-31", "2026-11-04", []);

        Assert.False(report.Period.IsPartial);
        Assert.Equal(D("2026-10-31"), report.Period.AsOf);
        Assert.Equal(31, report.Period.ElapsedDays);
    }

    [Fact]
    public void A_period_entirely_in_the_future_has_no_elapsed_days_but_still_lists_plans()
    {
        var plan = Run("2026-12-05", meters: 10_000, seconds: 3600, status: RunStatus.Planned, id: 1);

        var report = Build("2026-12-01", "2026-12-31", "2026-11-04", [plan], ComparisonKind.Previous);

        Assert.Equal(0, report.Period.ElapsedDays);
        Assert.Equal(0, report.Totals.RunCount);
        Assert.Equal(1, report.Planned.RunCount);
        Assert.Equal(1, report.Planned.UpcomingCount);
        Assert.Null(report.Comparison);
        Assert.Empty(report.Load.Daily);
    }

    [Fact]
    public void Planned_summary_splits_missed_and_upcoming_at_today_and_ignores_rest()
    {
        var facts = new[]
        {
            Run("2026-11-02", meters: 5000, seconds: 1800, status: RunStatus.Planned, id: 1),
            Run("2026-11-04", meters: 6000, seconds: 2000, status: RunStatus.Planned, id: 2),
            Run("2026-11-06", meters: 7000, seconds: 2400, status: RunStatus.Planned, id: 3),
            Run("2026-11-07", "rest", status: RunStatus.Planned, id: 4),
        };

        var report = Build("2026-11-02", "2026-11-08", "2026-11-04", facts);

        Assert.Equal(3, report.Planned.RunCount);
        Assert.Equal(18_000, report.Planned.DistanceMeters);
        Assert.Equal(6200, report.Planned.DurationSeconds);
        Assert.Equal(1, report.Planned.MissedCount);
        Assert.Equal(2, report.Planned.UpcomingCount);
    }

    [Fact]
    public void Data_quality_counts_estimated_rpe_missing_durations_and_missing_distances()
    {
        var facts = new[]
        {
            Run("2026-11-02", "easy", meters: 5000, seconds: 1800, rpe: 4, id: 1),    // 7200 RPE·s
            Run("2026-11-03", "tempo", meters: 5000, seconds: 1800, id: 2),           // estimated: 7 x 1800 = 12600
            Run("2026-11-04", "easy", meters: 5000, id: 3),                           // no duration
            Run("2026-11-04", "long", seconds: 3600, rpe: 5, id: 4),                  // no distance
            Run("2026-11-04", "rest", id: 5),
        };

        var quality = Build("2026-11-02", "2026-11-08", "2026-11-04", facts).DataQuality;

        Assert.Equal(4, quality.CompletedSessions);
        Assert.Equal(1, quality.SessionsWithEstimatedRpe);
        Assert.Equal(1, quality.SessionsWithoutDuration);
        Assert.Equal(1, quality.RunningSessionsWithoutDistance);
        Assert.Equal(0.33, quality.EstimatedLoadShare); // 12600 / (7200 + 12600 + 18000)
    }
}
