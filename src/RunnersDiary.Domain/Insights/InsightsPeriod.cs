namespace RunnersDiary.Domain.Insights;

public enum ComparisonKind
{
    None = 0,
    Previous = 1,
    PreviousYear = 2,
}

/// <summary>The inclusive local-date range of a report. Everything "done" is cut at <see cref="AsOf"/> = min(to, today).</summary>
public readonly record struct InsightsPeriod
{
    public const int MaxDays = 366;

    public InsightsPeriod(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("The period must not end before it starts.", nameof(to));
        }

        From = from;
        To = to;
    }

    public DateOnly From { get; }

    public DateOnly To { get; }

    public int Days => To.DayNumber - From.DayNumber + 1;

    public DateOnly AsOf(DateOnly today) => To < today ? To : today;

    /// <summary>Days of the period that have started (0 when the whole period is in the future).</summary>
    public int ElapsedDays(DateOnly today) => Math.Max(0, AsOf(today).DayNumber - From.DayNumber + 1);

    public bool IsPartial(DateOnly today) => To > today;
}
