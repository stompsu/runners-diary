using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Insights;

/// <summary>
/// Builds an <see cref="InsightsReport"/> from projected facts (ADR 0024). Pure: "today" is an input and nothing is read from a clock or a store.
/// </summary>
public static class InsightsReportBuilder
{
    /// <param name="facts">Completed and planned facts covering the lead-in (see <see cref="RequiredFactsRange"/>); also used for the buckets and the load history.</param>
    /// <param name="comparisonFacts">Completed facts of the comparison window (may include other dates; they are filtered).</param>
    /// <param name="races">The user's race-category runs up to the end of the period (for "PB at the time").</param>
    /// <param name="historyStart">The date of the user's first completed load-bearing session ever; null derives it from <paramref name="facts"/>.</param>
    public static InsightsReport Build(
        InsightsPeriod period,
        ComparisonKind compare,
        DateOnly today,
        IReadOnlyList<RunFact> facts,
        IReadOnlyList<RunFact> comparisonFacts,
        IReadOnlyList<RaceFact> races,
        DateOnly? historyStart)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(comparisonFacts);
        ArgumentNullException.ThrowIfNull(races);

        var asOf = period.AsOf(today);
        var elapsed = period.ElapsedDays(today);

        var completed = facts
            .Where(f => f.Status == RunStatus.Completed && f.Date <= asOf)
            .OrderBy(f => f.Date).ThenBy(f => f.Id)
            .ToList();
        var planned = facts.Where(f => f.Status == RunStatus.Planned).ToList();

        // Buckets are whole ISO weeks / calendar months cut at today (not at asOf): every completed day up to today counts.
        var bucketCompleted = facts
            .Where(f => f.Status == RunStatus.Completed && f.Date <= today)
            .OrderBy(f => f.Date).ThenBy(f => f.Id)
            .ToList();
        var inPeriod = completed.Where(f => f.Date >= period.From).ToList();

        var effectiveHistoryStart = historyStart ?? completed
            .Where(f => SessionLoad.Of(f) is not null)
            .Select(f => (DateOnly?)f.Date)
            .Min();

        var totals = PeriodTotals.Compute(inPeriod);
        var comparison = BuildComparison(period, compare, today, comparisonFacts, totals);
        var load = BuildLoad(period, today, asOf, bucketCompleted, effectiveHistoryStart, out var series);

        return new InsightsReport(
            MetricsVersion.Current,
            today,
            new PeriodInfo(period.From, period.To, asOf, period.IsPartial(today), period.Days, elapsed),
            InsightsThresholds.V1,
            totals,
            BuildPlanned(period, today, planned),
            comparison,
            load,
            BuildWeeks(period, today, planned, bucketCompleted, series),
            BuildMonths(period, today, bucketCompleted, planned),
            TypeDistribution.Compute(inPeriod),
            PaceTrend.Compute(inPeriod),
            RaceHistory.Compute(races, period, today),
            BuildDataQuality(inPeriod));
    }

    /// <summary>The first date the facts query must cover: the 35-day look-back of the earliest weekly/monthly bucket or of the as-of date.</summary>
    public static DateOnly RequiredFactsStart(InsightsPeriod period, DateOnly today)
    {
        var monday = IsoWeek.MondayOf(period.From);
        var monthStart = new DateOnly(period.From.Year, period.From.Month, 1);
        var earliest = new[] { monday, monthStart, period.AsOf(today) }.Min();
        return earliest.AddDays(-(InsightsThresholds.AcwrMinHistoryDaysValue - 1));
    }

    /// <summary>The last date the facts query must cover: the end of the last ISO week and of the last month of the period.</summary>
    public static DateOnly RequiredFactsEnd(InsightsPeriod period)
    {
        var sunday = IsoWeek.MondayOf(period.To).AddDays(6);
        var monthEnd = ComparisonWindow.EndOfMonth(period.To);
        return sunday > monthEnd ? sunday : monthEnd;
    }

    private static ComparisonResult? BuildComparison(
        InsightsPeriod period, ComparisonKind compare, DateOnly today, IReadOnlyList<RunFact> comparisonFacts, PeriodTotals current)
    {
        if (ComparisonWindow.For(period, compare, today) is not { } window)
        {
            return null;
        }

        var previous = PeriodTotals.Compute(comparisonFacts
            .Where(f => f.Status == RunStatus.Completed && f.Date >= window.From && f.Date <= window.To && f.Date <= today));
        return new ComparisonResult(window.Kind, window.From, window.To, previous, Deltas.Between(current, previous));
    }

    private static PlannedSummary BuildPlanned(InsightsPeriod period, DateOnly today, List<RunFact> planned)
    {
        var inRange = planned
            .Where(f => f.Date >= period.From && f.Date <= period.To && RunTypeCatalog.Find(f.RunTypeId) is { IsRestDay: false })
            .OrderBy(f => f.Date).ThenBy(f => f.Id)
            .ToList();
        return new PlannedSummary(
            inRange.Count,
            Rounding.Round(inRange.Sum(f => f.DistanceMeters ?? 0), 1),
            inRange.Sum(f => (long)(f.DurationSeconds ?? 0)),
            inRange.Count(f => f.Date >= today),
            inRange.Count(f => f.Date < today));
    }

    private static LoadSection BuildLoad(
        InsightsPeriod period, DateOnly today, DateOnly asOf, List<RunFact> completed, DateOnly? historyStart, out DailyLoadSeries series)
    {
        var minLookBack = InsightsThresholds.AcwrMinHistoryDaysValue - 1;
        var monthStart = new DateOnly(period.From.Year, period.From.Month, 1);
        var origin = new[] { IsoWeek.MondayOf(period.From), monthStart, asOf }.Min().AddDays(-minLookBack);
        // The series runs to the end of the last ISO week of the period, cut at today, so the week buckets are complete when past.
        var lastSunday = IsoWeek.MondayOf(period.To).AddDays(6);
        var seriesEnd = asOf > (lastSunday < today ? lastSunday : today) ? asOf : (lastSunday < today ? lastSunday : today);
        series = new DailyLoadSeries(completed, origin, seriesEnd, historyStart);

        var atAsOf = series.At(asOf);

        var daily = new List<DailyLoadPoint>();
        var bands = new int[4];
        AcwrPoint? peak = null;
        for (var date = IsoWeek.MondayOf(period.From); date <= asOf; date = date.AddDays(1))
        {
            var point = series.At(date);
            daily.Add(new DailyLoadPoint(date, Rounding.Au(series.Daily(date)), point.Acute, point.Chronic, point.Value, point.Band));

            if (date < period.From)
            {
                continue;
            }

            bands[point.Band is { } band ? (int)band : 3]++;
            if (point.Value is { } value && (peak?.Value is not { } best || value > best))
            {
                peak = point;
            }
        }

        return new LoadSection(historyStart, atAsOf, peak, new BandDays(bands[0], bands[1], bands[2], bands[3]), daily);
    }

    private static List<WeekBucket> BuildWeeks(
        InsightsPeriod period, DateOnly today, List<RunFact> planned, List<RunFact> completed, DailyLoadSeries series)
    {
        var weeks = new List<WeekBucket>();
        for (var monday = IsoWeek.MondayOf(period.From); monday <= period.To; monday = monday.AddDays(7))
        {
            var sunday = monday.AddDays(6);
            var cutoff = sunday < today ? sunday : today;
            var done = completed.Where(f => f.Date >= monday && f.Date <= cutoff);
            var plans = planned.Where(f => f.Date >= monday && f.Date <= sunday);
            var complete = sunday <= today;

            double? monotony = null;
            double? strain = null;
            if (complete)
            {
                var days = Enumerable.Range(0, 7).Select(i => series.Daily(monday.AddDays(i))).ToList();
                (monotony, strain) = WeeklyMonotony.Compute(days);
            }

            var week = IsoWeek.Of(monday);
            weeks.Add(new WeekBucket(
                week.Year,
                week.Week,
                monday,
                sunday,
                PeriodTotals.Bucket(done, RunStatus.Completed),
                PeriodTotals.Bucket(plans, RunStatus.Planned),
                Rounding.Au(series.Sum(monday, cutoff)),
                monotony,
                strain,
                cutoff >= monday ? series.At(cutoff).Value : null,
                complete));
        }

        return weeks;
    }

    private static List<MonthBucket> BuildMonths(InsightsPeriod period, DateOnly today, List<RunFact> completed, List<RunFact> planned)
    {
        var months = new List<MonthBucket>();
        for (var start = new DateOnly(period.From.Year, period.From.Month, 1); start <= period.To; start = start.AddMonths(1))
        {
            var end = ComparisonWindow.EndOfMonth(start);
            var cutoff = end < today ? end : today;
            var done = completed.Where(f => f.Date >= start && f.Date <= cutoff).ToList();
            var plans = planned.Where(f => f.Date >= start && f.Date <= end);
            var rpeSeconds = done.Sum(f => SessionLoad.Of(f)?.RpeSeconds ?? 0);

            months.Add(new MonthBucket(
                start.Year,
                start.Month,
                start,
                end,
                PeriodTotals.Bucket(done, RunStatus.Completed),
                PeriodTotals.Bucket(plans, RunStatus.Planned),
                Rounding.Au(rpeSeconds)));
        }

        return months;
    }

    private static DataQuality BuildDataQuality(List<RunFact> inPeriod)
    {
        var sessions = 0;
        var estimated = 0;
        var withoutDuration = 0;
        var withoutDistance = 0;
        long totalRpeSeconds = 0;
        long estimatedRpeSeconds = 0;

        foreach (var fact in inPeriod)
        {
            if (RunTypeCatalog.Find(fact.RunTypeId) is not { IsRestDay: false } type)
            {
                continue;
            }

            sessions++;
            if (type.CountsTowardRunningDistance && fact.DistanceMeters is not > 0)
            {
                withoutDistance++;
            }

            var load = SessionLoad.Of(fact, type);
            if (load.MissingDuration)
            {
                withoutDuration++;
                continue;
            }

            totalRpeSeconds += load.RpeSeconds;
            if (load.Estimated)
            {
                estimated++;
                estimatedRpeSeconds += load.RpeSeconds;
            }
        }

        var share = totalRpeSeconds == 0 ? 0 : Rounding.Round((decimal)estimatedRpeSeconds / totalRpeSeconds, 2);
        return new DataQuality(sessions, estimated, share, withoutDuration, withoutDistance);
    }
}
