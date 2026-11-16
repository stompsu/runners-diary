using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Runs;

public sealed class RunGoalLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    private static RunValues Values() => new()
    {
        Date = new DateOnly(2026, 11, 24),
        TimeZoneId = "Europe/Warsaw",
        Status = RunStatus.Planned,
        RunTypeId = 20,
        DistanceMeters = 8_000,
    };

    [Fact]
    public void New_runs_are_not_linked_to_a_goal() => Assert.Null(Run.Create(Guid.NewGuid(), Values(), Now).GoalId);

    [Fact]
    public void Link_sets_the_goal_once()
    {
        var run = Run.Create(Guid.NewGuid(), Values(), Now);
        var goalId = Guid.NewGuid();

        run.LinkToGoal(goalId);

        Assert.Equal(goalId, run.GoalId);
        Assert.Throws<DomainRuleException>(() => run.LinkToGoal(Guid.NewGuid()));
        Assert.Throws<DomainRuleException>(() => Run.Create(Guid.NewGuid(), Values(), Now).LinkToGoal(Guid.Empty));
    }

    [Fact]
    public void Update_keeps_the_link_and_the_link_has_no_public_setter()
    {
        var run = Run.Create(Guid.NewGuid(), Values(), Now);
        var goalId = Guid.NewGuid();
        run.LinkToGoal(goalId);

        run.Update(Values() with { Date = new DateOnly(2026, 11, 25), Title = "Moved" }, Now.AddDays(1));

        Assert.Equal(goalId, run.GoalId);
        Assert.False(typeof(Run).GetProperty(nameof(Run.GoalId))!.SetMethod!.IsPublic);
    }
}
