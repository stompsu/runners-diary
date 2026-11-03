namespace RunnersDiary.Domain.Calendar;

/// <summary>The Monday-first grid for a month: from the Monday on/before the 1st to the Sunday on/after the last day (4-6 weeks).</summary>
public sealed record CalendarGrid
{
    private CalendarGrid(int year, int month, DateOnly start, DateOnly end)
    {
        Year = year;
        Month = month;
        Start = start;
        End = end;
    }

    public int Year { get; }

    public int Month { get; }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    public int WeekCount => ((End.DayNumber - Start.DayNumber) + 1) / 7;

    public static CalendarGrid ForMonth(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var start = IsoWeek.MondayOf(first);
        var end = IsoWeek.MondayOf(last).AddDays(6);
        return new CalendarGrid(year, month, start, end);
    }

    public IReadOnlyList<IsoWeek> Weeks()
    {
        var weeks = new List<IsoWeek>(WeekCount);
        for (var d = Start; d <= End; d = d.AddDays(7))
        {
            weeks.Add(IsoWeek.Of(d));
        }

        return weeks;
    }
}
