using System.Globalization;

namespace RunnersDiary.Domain.Calendar;

/// <summary>An ISO 8601 week (Monday-first). The ISO year can differ from the calendar year around New Year.</summary>
public readonly record struct IsoWeek(int Year, int Week)
{
    public static IsoWeek Of(DateOnly date)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        return new IsoWeek(ISOWeek.GetYear(dt), ISOWeek.GetWeekOfYear(dt));
    }

    /// <summary>The Monday on or before <paramref name="date"/> (a Sunday belongs to the preceding Monday's week).</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public DateOnly Start => DateOnly.FromDateTime(ISOWeek.ToDateTime(Year, Week, DayOfWeek.Monday));

    public DateOnly End => Start.AddDays(6);
}
