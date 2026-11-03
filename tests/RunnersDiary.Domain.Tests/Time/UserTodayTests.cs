using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Domain.Time;

namespace RunnersDiary.Domain.Tests.Time;

public sealed class UserTodayTests
{
    [Fact]
    public void Today_is_computed_in_the_user_zone()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 23, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 11, 4), UserToday.Get(clock, "Europe/Warsaw"));
        Assert.Equal(new DateOnly(2026, 11, 3), UserToday.Get(clock, "Etc/UTC"));
        Assert.Equal(new DateOnly(2026, 11, 3), UserToday.Get(clock, "America/New_York"));
    }
}
