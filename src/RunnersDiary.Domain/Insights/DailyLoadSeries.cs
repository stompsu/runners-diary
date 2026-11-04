using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Insights;

/// <summary>
/// Daily session load (exact RPE·s) from <c>origin</c> to <c>end</c> with O(1) window sums, and the ACWR on any date whose 35-day
/// look-back starts at or after <c>origin</c>. Days without sessions count as 0; time before the diary started is never rest.
/// </summary>
public sealed class DailyLoadSeries
{
    private readonly long[] _prefix; // _prefix[i] = sum of daily load over days origin .. origin + i - 1

    public DailyLoadSeries(IEnumerable<RunFact> facts, DateOnly origin, DateOnly end, DateOnly? historyStart)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (end < origin)
        {
            throw new ArgumentException("The series must not end before it starts.", nameof(end));
        }

        Origin = origin;
        End = end;
        HistoryStart = historyStart;

        var days = end.DayNumber - origin.DayNumber + 1;
        var daily = new long[days];
        foreach (var fact in facts.Where(f => f.Status == RunStatus.Completed && f.Date >= origin && f.Date <= end).OrderBy(f => f.Date).ThenBy(f => f.Id))
        {
            if (SessionLoad.Of(fact) is { } load)
            {
                daily[fact.Date.DayNumber - origin.DayNumber] += load.RpeSeconds;
            }
        }

        _prefix = new long[days + 1];
        for (var i = 0; i < days; i++)
        {
            _prefix[i + 1] = _prefix[i] + daily[i];
        }
    }

    public DateOnly Origin { get; }

    public DateOnly End { get; }

    public DateOnly? HistoryStart { get; }

    /// <summary>Load of one day in RPE·s (0 outside the series).</summary>
    public long Daily(DateOnly date) => Sum(date, date);

    /// <summary>Sum of the daily loads over [from, to], clipped to the series.</summary>
    public long Sum(DateOnly from, DateOnly to)
    {
        var lo = Math.Max(from.DayNumber, Origin.DayNumber) - Origin.DayNumber;
        var hi = Math.Min(to.DayNumber, End.DayNumber) - Origin.DayNumber;
        return hi < lo ? 0 : _prefix[hi + 1] - _prefix[lo];
    }

    /// <summary>The ACWR point on <paramref name="date"/> (which must lie within the series and be at least 34 days after its origin).</summary>
    public AcwrPoint At(DateOnly date)
    {
        if (date < Origin.AddDays(InsightsThresholds.AcwrMinHistoryDaysValue - 1) || date > End)
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, "The date needs a 35-day look-back inside the series.");
        }

        var acuteSum = Sum(date.AddDays(-6), date);
        var chronicSum = Sum(date.AddDays(-34), date.AddDays(-7));
        var acute = Rounding.Au(acuteSum);

        if (HistoryStart is not { } start || date.AddDays(-34) < start)
        {
            return new AcwrPoint(date, null, null, acute, null, AcwrUnavailableReason.InsufficientHistory);
        }

        var chronic = Rounding.Round(chronicSum / 4m / 60m, 1);
        if (Acwr.Ratio(acuteSum, chronicSum) is not { } ratio)
        {
            return new AcwrPoint(date, null, null, acute, chronic, AcwrUnavailableReason.NoChronicLoad);
        }

        return new AcwrPoint(date, (double)ratio, Acwr.BandOf(ratio), acute, chronic, null);
    }
}
