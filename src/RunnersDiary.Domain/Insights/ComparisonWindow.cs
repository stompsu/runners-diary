namespace RunnersDiary.Domain.Insights;

/// <summary>A comparison window (ADR 0024 §5). <see cref="To"/> already reflects partial-period alignment.</summary>
public sealed record ComparisonWindow(ComparisonKind Kind, DateOnly From, DateOnly To)
{
    /// <summary>
    /// The window to compare <paramref name="period"/> with, or null for <see cref="ComparisonKind.None"/> or when no day of a
    /// partial period has started yet (nothing to compare).
    /// </summary>
    public static ComparisonWindow? For(InsightsPeriod period, ComparisonKind kind, DateOnly today)
    {
        if (kind == ComparisonKind.None)
        {
            return null;
        }

        var (from, to) = kind == ComparisonKind.Previous ? Previous(period) : PreviousYear(period);

        if (period.IsPartial(today))
        {
            var elapsed = period.ElapsedDays(today);
            if (elapsed == 0)
            {
                return null;
            }

            var aligned = from.AddDays(elapsed - 1);
            to = aligned < to ? aligned : to;
        }

        return new ComparisonWindow(kind, from, to);
    }

    private static (DateOnly From, DateOnly To) Previous(InsightsPeriod period)
    {
        if (period.From.Day == 1 && period.To == EndOfMonth(period.To))
        {
            var months = ((period.To.Year - period.From.Year) * 12) + period.To.Month - period.From.Month + 1;
            return (period.From.AddMonths(-months), EndOfMonth(period.To.AddMonths(-months)));
        }

        var length = period.Days;
        return (period.From.AddDays(-length), period.To.AddDays(-length));
    }

    private static (DateOnly From, DateOnly To) PreviousYear(InsightsPeriod period)
    {
        if (period.From.DayOfWeek == DayOfWeek.Monday && period.Days % 7 == 0)
        {
            return (period.From.AddDays(-364), period.To.AddDays(-364));
        }

        return (period.From.AddYears(-1), period.To.AddYears(-1));
    }

    internal static DateOnly EndOfMonth(DateOnly date) => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}

/// <summary>Change against the comparison period. <see cref="Percent"/> is null when the previous value is 0.</summary>
public sealed record Delta(double Absolute, double? Percent)
{
    internal static Delta Of(double current, double previous, int decimals)
    {
        var absolute = Rounding.Round(current - previous, decimals);
        double? percent = previous == 0 ? null : Rounding.Round(absolute / previous * 100, 1);
        return new Delta(absolute, percent);
    }
}

public sealed record Deltas(Delta RunCount, Delta DistanceMeters, Delta DurationSeconds, Delta ElevationGainMeters, Delta Load, Delta ActiveDays)
{
    public static Deltas Between(PeriodTotals current, PeriodTotals previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);
        return new Deltas(
            Delta.Of(current.RunCount, previous.RunCount, 0),
            Delta.Of(current.DistanceMeters, previous.DistanceMeters, 1),
            Delta.Of(current.DurationSeconds, previous.DurationSeconds, 0),
            Delta.Of(current.ElevationGainMeters, previous.ElevationGainMeters, 1),
            Delta.Of(current.Load, previous.Load, 1),
            Delta.Of(current.ActiveDays, previous.ActiveDays, 0));
    }
}
