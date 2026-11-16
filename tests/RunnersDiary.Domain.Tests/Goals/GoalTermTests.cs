using RunnersDiary.Domain.Goals;

namespace RunnersDiary.Domain.Tests.Goals;

public sealed class GoalTermTests
{
    [Theory]
    [InlineData("2026-11-16", "2026-11-23", GoalTerm.ShortTerm)]
    [InlineData("2026-11-16", "2027-02-08", GoalTerm.ShortTerm)] // 84 days
    [InlineData("2026-11-16", "2027-02-09", GoalTerm.LongTerm)] // 85 days
    public void Twelve_weeks_or_less_is_short_term(string today, string target, GoalTerm expected) =>
        Assert.Equal(expected, GoalTerms.Of(DateOnly.Parse(target, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("2027-03-20", "2027-06-12", GoalTerm.ShortTerm)] // 84 days across the Europe/US spring DST changes
    [InlineData("2027-03-20", "2027-06-13", GoalTerm.LongTerm)]
    [InlineData("2027-10-23", "2028-01-15", GoalTerm.ShortTerm)] // 84 days across the autumn change
    [InlineData("2027-10-23", "2028-01-16", GoalTerm.LongTerm)]
    public void Calendar_days_decide_so_daylight_saving_never_shifts_the_boundary(string today, string target, GoalTerm expected) =>
        Assert.Equal(expected, GoalTerms.Of(DateOnly.Parse(target, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));
}
