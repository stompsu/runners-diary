using RunnersDiary.Domain.Calendar;

namespace RunnersDiary.Domain.Tests.Calendar;

public sealed class CalendarGridTests
{
    [Fact]
    public void February_2021_is_exactly_four_weeks()
    {
        var grid = CalendarGrid.ForMonth(2021, 2);

        Assert.Equal(new DateOnly(2021, 2, 1), grid.Start);
        Assert.Equal(new DateOnly(2021, 2, 28), grid.End);
        Assert.Equal(4, grid.WeekCount);
    }

    [Fact]
    public void August_2021_needs_six_weeks()
    {
        var grid = CalendarGrid.ForMonth(2021, 8);

        Assert.Equal(new DateOnly(2021, 7, 26), grid.Start);
        Assert.Equal(new DateOnly(2021, 9, 5), grid.End);
        Assert.Equal(6, grid.WeekCount);
        Assert.Equal(6, grid.Weeks().Count);
    }

    [Fact]
    public void Every_month_of_several_years_is_a_monday_to_sunday_grid_of_4_to_6_weeks()
    {
        for (var year = 2020; year <= 2030; year++)
        {
            for (var month = 1; month <= 12; month++)
            {
                var grid = CalendarGrid.ForMonth(year, month);
                Assert.Equal(DayOfWeek.Monday, grid.Start.DayOfWeek);
                Assert.Equal(DayOfWeek.Sunday, grid.End.DayOfWeek);
                Assert.InRange(grid.WeekCount, 4, 6);
                Assert.True(grid.Start <= new DateOnly(year, month, 1));
                Assert.True(grid.End >= new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
            }
        }
    }
}
