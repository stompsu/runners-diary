using RunnersDiary.Domain.Time;

namespace RunnersDiary.Domain.Tests.Time;

public sealed class IanaTimeZoneTests
{
    [Theory]
    [InlineData("Europe/Warsaw")]
    [InlineData("Etc/UTC")]
    [InlineData("America/New_York")]
    [InlineData("UTC")]
    public void Accepts_iana_ids(string id) => Assert.True(IanaTimeZone.IsValid(id));

    [Theory]
    [InlineData("Central European Standard Time")]
    [InlineData("W. Europe Standard Time")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("Not/AZone")]
    [InlineData("Europe/Warsaw; DROP TABLE")]
    public void Rejects_everything_else(string? id) => Assert.False(IanaTimeZone.IsValid(id));

    [Fact]
    public void Rejects_overlong_id() => Assert.False(IanaTimeZone.IsValid("Europe/" + new string('a', 80)));

    [Fact]
    public void Europe_Warsaw_resolves_on_this_host()
    {
        // Guards against container images without tzdata (plan §5).
        Assert.Equal(TimeSpan.FromHours(1), IanaTimeZone.Get("Europe/Warsaw").GetUtcOffset(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
    }
}
