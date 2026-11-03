using RunnersDiary.Domain.Time;

namespace RunnersDiary.Domain.Tests.Time;

public sealed class LocalStartResolverTests
{
    private static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void Normal_time_uses_zone_offset()
    {
        var utc = LocalStartResolver.ResolveUtc(new DateOnly(2026, 7, 15), new TimeOnly(8, 0), "Europe/Warsaw");

        Assert.Equal(Utc(2026, 7, 15, 6, 0), utc);
        Assert.Equal(DateTimeKind.Utc, utc!.Value.Kind);
    }

    [Fact]
    public void Winter_time_uses_standard_offset()
    {
        var utc = LocalStartResolver.ResolveUtc(new DateOnly(2026, 1, 15), new TimeOnly(8, 0), "Europe/Warsaw");

        Assert.Equal(Utc(2026, 1, 15, 7, 0), utc);
    }

    [Fact]
    public void Spring_forward_gap_is_shifted_forward_by_the_gap()
    {
        // 2026-03-29 02:30 does not exist in Europe/Warsaw; it becomes 03:30 local = 01:30Z.
        var utc = LocalStartResolver.ResolveUtc(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), "Europe/Warsaw");

        Assert.Equal(Utc(2026, 3, 29, 1, 30), utc);
    }

    [Fact]
    public void Fall_back_ambiguous_time_resolves_to_the_earlier_occurrence()
    {
        // 2026-10-25 02:30 occurs twice; the earlier one is still CEST (+2) = 00:30Z.
        var utc = LocalStartResolver.ResolveUtc(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), "Europe/Warsaw");

        Assert.Equal(Utc(2026, 10, 25, 0, 30), utc);
    }

    [Fact]
    public void Missing_start_time_gives_null()
    {
        Assert.Null(LocalStartResolver.ResolveUtc(new DateOnly(2026, 7, 15), null, "Europe/Warsaw"));
    }

    [Fact]
    public void Other_zone_is_supported()
    {
        var utc = LocalStartResolver.ResolveUtc(new DateOnly(2026, 1, 15), new TimeOnly(7, 0), "America/New_York");

        Assert.Equal(Utc(2026, 1, 15, 12, 0), utc);
    }

    [Fact]
    public void Windows_zone_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LocalStartResolver.ResolveUtc(new DateOnly(2026, 7, 15), new TimeOnly(8, 0), "Central European Standard Time"));
    }
}
