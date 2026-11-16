using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Goals;

public sealed class GoalEntityTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();

    private static GoalValues Values(Func<GoalValues, GoalValues>? change = null)
    {
        var v = new GoalValues(
            GoalType.Performance, "10K under 50", Today.AddDays(60), 10_000, null, 3_000, Surface.Road, null, null, null, null, GoalStatus.Active);
        return change is null ? v : change(v);
    }

    private static GoalPlanRecord Plan(DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), at ?? Now, "Notes", 1, "gpt", "model-1", Today.AddDays(50), false);

    [Fact]
    public void Create_sets_owner_id_values_and_no_plan()
    {
        var goal = Goal.Create(User, Values(), Today, null, Now);

        Assert.Equal(User, goal.UserId);
        Assert.NotEqual(Guid.Empty, goal.Id);
        Assert.Equal(GoalType.Performance, goal.Type);
        Assert.Equal(3_000, goal.TargetDurationSeconds);
        Assert.False(goal.HasPlan);
        Assert.False(goal.PlanOutdated);
        Assert.Equal(DateTimeKind.Utc, goal.CreatedAt.Kind);
    }

    [Fact]
    public void Create_rejects_a_missing_owner_and_invalid_values()
    {
        Assert.Throws<DomainRuleException>(() => Goal.Create(Guid.Empty, Values(), Today, null, Now));
        Assert.Throws<DomainRuleException>(() => Goal.Create(User, Values(v => v with { Title = "" }), Today, null, Now));
    }

    [Fact]
    public void Update_of_a_plan_field_marks_an_existing_plan_outdated()
    {
        var goal = Goal.Create(User, Values(), Today, null, Now);
        goal.RecordPlan(Plan(), Now);

        goal.Update(Values(v => v with { Title = "Renamed", Notes = "x" }), Today, null, Now);
        Assert.False(goal.PlanOutdated);

        goal.Update(Values(v => v with { TargetDurationSeconds = 2_900 }), Today, null, Now);
        Assert.True(goal.PlanOutdated);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("date")]
    [InlineData("distance")]
    [InlineData("elevation")]
    [InlineData("surface")]
    public void Every_plan_relevant_field_marks_the_plan_outdated(string field)
    {
        var goal = Goal.Create(User, Values(), Today, null, Now);
        goal.RecordPlan(Plan(), Now);

        goal.Update(field switch
        {
            "type" => Values(v => v with { Type = GoalType.Race, EventName = "Race" }),
            "date" => Values(v => v with { TargetDate = v.TargetDate.AddDays(7) }),
            "distance" => Values(v => v with { DistanceMeters = 10_500, TargetDurationSeconds = 3_100 }),
            "elevation" => Values(v => v with { ElevationGainMeters = 100 }),
            _ => Values(v => v with { Surface = Surface.Trail }),
        }, Today, null, Now);

        Assert.True(goal.PlanOutdated);
    }

    [Fact]
    public void Update_without_a_plan_never_sets_the_outdated_flag()
    {
        var goal = Goal.Create(User, Values(), Today, null, Now);

        goal.Update(Values(v => v with { DistanceMeters = 12_000, TargetDurationSeconds = 3_600 }), Today, null, Now);

        Assert.False(goal.PlanOutdated);
    }

    [Fact]
    public void RecordPlan_clears_the_outdated_flag_and_stores_the_metadata()
    {
        var goal = Goal.Create(User, Values(), Today, null, Now);
        goal.RecordPlan(Plan(), Now);
        goal.Update(Values(v => v with { Surface = Surface.Trail }), Today, null, Now);
        var plan = Plan(Now.AddHours(1)) with { ModelId = new string('m', 150), HorizonCapped = true };

        goal.RecordPlan(plan, Now.AddHours(1));

        Assert.False(goal.PlanOutdated);
        Assert.Equal(plan.GenerationId, goal.PlanGenerationId);
        Assert.Equal(100, goal.PlanModelId!.Length);
        Assert.True(goal.PlanHorizonCapped);
        Assert.False(goal.IsDemoPlan);
        Assert.Throws<DomainRuleException>(() => goal.RecordPlan(plan with { Notes = new string('n', 601) }, Now));
    }

    [Fact]
    public void An_unchanged_target_date_inside_seven_days_stays_editable()
    {
        var goal = Goal.Create(User, Values(v => v with { TargetDate = Today.AddDays(8) }), Today, null, Now);
        var later = Today.AddDays(5); // the goal is now 3 days away

        goal.Update(Values(v => v with { TargetDate = Today.AddDays(8), Title = "Still editable" }), later, null, Now);

        Assert.Equal("Still editable", goal.Title);
        Assert.Throws<DomainRuleException>(() => goal.Update(Values(v => v with { TargetDate = Today.AddDays(9) }), Today.AddDays(5), null, Now));
    }

    [Fact]
    public void MarkPlanOutdated_and_ClearRaceRun()
    {
        var id = Guid.NewGuid();
        var goal = Goal.Create(
            User,
            Values(v => v with { Type = GoalType.Race, EventName = "E", TargetDurationSeconds = null, RaceRunId = id }),
            Today,
            new LinkedRaceRun(id, Today.AddDays(60), RunStatus.Planned, true),
            Now);

        goal.MarkPlanOutdated(Now);
        Assert.False(goal.PlanOutdated); // no plan yet
        goal.RecordPlan(Plan(), Now);
        goal.MarkPlanOutdated(Now);
        Assert.True(goal.PlanOutdated);

        goal.ClearRaceRun();
        Assert.Null(goal.RaceRunId);
    }
}
