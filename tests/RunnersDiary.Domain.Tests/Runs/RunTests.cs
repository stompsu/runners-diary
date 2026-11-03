using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Runs;

public sealed class RunTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, 123, TimeSpan.Zero);

    private static RunValues Values() => new()
    {
        Date = new DateOnly(2026, 3, 29),
        StartTime = new TimeOnly(2, 30),
        TimeZoneId = "Europe/Warsaw",
        Status = RunStatus.Planned,
        RunTypeId = 22,
        DistanceMeters = 20000,
    };

    [Fact]
    public void Create_derives_start_utc_and_audit_timestamps()
    {
        var userId = Guid.NewGuid();

        var run = Run.Create(userId, Values(), Now);

        Assert.Equal(userId, run.UserId);
        Assert.NotEqual(Guid.Empty, run.Id);
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc), run.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc), run.CreatedAt);
        Assert.Equal(run.CreatedAt, run.UpdatedAt);
    }

    [Fact]
    public void Update_recomputes_start_utc_and_clears_it_without_start_time()
    {
        var run = Run.Create(Guid.NewGuid(), Values(), Now);

        run.Update(Values() with { StartTime = null }, Now.AddMinutes(5));

        Assert.Null(run.StartUtc);
        Assert.Null(run.StartTime);
        Assert.True(run.UpdatedAt > run.CreatedAt);
    }

    [Fact]
    public void Create_enforces_rules_as_a_safety_net()
    {
        var ex = Assert.Throws<DomainRuleException>(() => Run.Create(Guid.NewGuid(), Values() with { Rating = new RunRating(3) }, Now));

        Assert.Contains(ex.Errors, e => e.Path == "rating");
    }

    [Fact]
    public void Create_requires_an_owner() =>
        Assert.Throws<DomainRuleException>(() => Run.Create(Guid.Empty, Values(), Now));
}
