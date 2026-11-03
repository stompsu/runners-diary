using RunnersDiary.Domain.Calendar;

namespace RunnersDiary.Domain.Tests.Calendar;

public sealed class IsoWeekTests
{
    [Theory]
    [InlineData(2026, 1, 1, 2026, 1)]
    [InlineData(2027, 1, 1, 2026, 53)]
    [InlineData(2024, 12, 30, 2025, 1)]
    [InlineData(2026, 11, 8, 2026, 45)]
    public void Of_returns_iso_year_and_week(int y, int m, int d, int isoYear, int isoWeek) =>
        Assert.Equal(new IsoWeek(isoYear, isoWeek), IsoWeek.Of(new DateOnly(y, m, d)));

    [Fact]
    public void Sunday_belongs_to_the_preceding_monday()
    {
        var sunday = new DateOnly(2026, 11, 8);

        Assert.Equal(new DateOnly(2026, 11, 2), IsoWeek.MondayOf(sunday));
        Assert.Equal(new DateOnly(2026, 11, 2), IsoWeek.MondayOf(new DateOnly(2026, 11, 2)));
        Assert.Equal(IsoWeek.Of(new DateOnly(2026, 11, 2)), IsoWeek.Of(sunday));
    }

    [Fact]
    public void Start_and_end_span_monday_to_sunday()
    {
        var week = new IsoWeek(2026, 53);

        Assert.Equal(new DateOnly(2026, 12, 28), week.Start);
        Assert.Equal(new DateOnly(2027, 1, 3), week.End);
        Assert.Equal(DayOfWeek.Monday, week.Start.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, week.End.DayOfWeek);
    }
}
