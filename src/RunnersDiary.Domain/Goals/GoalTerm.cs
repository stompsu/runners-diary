namespace RunnersDiary.Domain.Goals;

/// <summary>Short- or long-term goal; derived from the days left, never stored (ADR 0039 §1).</summary>
public enum GoalTerm
{
    ShortTerm = 0,
    LongTerm = 1,
}

public static class GoalTerms
{
    /// <summary>A goal at most 12 weeks (84 days) away is short-term.</summary>
    public const int ShortTermMaxDays = 84;

    /// <param name="targetDate">The goal's local date.</param>
    /// <param name="today">The caller's local today (ADR 0008); calendar-day arithmetic, so DST shifts do not matter.</param>
    public static GoalTerm Of(DateOnly targetDate, DateOnly today) =>
        targetDate.DayNumber - today.DayNumber <= ShortTermMaxDays ? GoalTerm.ShortTerm : GoalTerm.LongTerm;
}
