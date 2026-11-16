using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Plans;

/// <summary>
/// AC6 / ADR 0041 §5: <c>seed/demo/training-plan.json</c> (the guest demo goal and its example plan) is valid against the pristine demo diary's own
/// envelope for every "today" across a year: phases, caps, sessions per week, hard days and collisions, with nothing dropped, and only in
/// weeks +6 to +10. A change to <c>runs.json</c> that breaks the example plan fails here.
/// </summary>
public sealed class DemoTrainingPlanFixtureTests
{
    private static readonly DemoTrainingPlanTemplate Template =
        DemoTrainingPlanTemplate.Parse(File.ReadAllText(Path.Combine(RepoPaths.DemoSeed, "training-plan.json")));

    internal static PlanFactSheet DemoSheet(DateOnly today)
    {
        var (facts, races) = DemoFacts.Facts(today);
        return PlanSheets.Build(Template.Goal.Spec(today), today, facts, races);
    }

    [Fact]
    public void The_fixture_describes_the_approved_demo_goal()
    {
        var goal = Template.Goal;
        Assert.Equal(
            (GoalType.Distance, "Demo Beskids Trail 25K", 25_000.0, (double?)900, (Surface?)Surface.Trail, 10, DayOfWeek.Sunday),
            (goal.Type, goal.Title, goal.DistanceMeters, goal.ElevationGainMeters, goal.Surface, goal.Week, goal.Day));
        Assert.All(Template.Sessions, s => Assert.InRange(s.Week, 6, 10));
        Assert.True(Template.Notes.Length is > 0 and <= TrainingPlanValidator.NotesMaxLength);
    }

    [Theory]
    [MemberData(nameof(DemoFacts.EveryDayOf2028), MemberType = typeof(DemoFacts))]
    public void The_example_plan_is_valid_for_the_demo_data_on_every_day(DateOnly today)
    {
        var sheet = DemoSheet(today);
        var target = Template.Goal.TargetDate(today);
        Assert.Equal(IsoWeek.MondayOf(today).AddDays(76), target);

        // The goal itself passes the goal rules as the seeder will create it.
        var values = new GoalValues(
            Template.Goal.Type, Template.Goal.Title, target, Template.Goal.DistanceMeters, Template.Goal.ElevationGainMeters, null,
            Template.Goal.Surface, null, null, null, null, GoalStatus.Active);
        Assert.Empty(GoalRules.Validate(values, today, null, null));

        var result = TrainingPlanValidator.Validate(Template.Draft(today), sheet, PlanValidationPolicy.Default);

        Assert.True(result.IsValid, $"{today:yyyy-MM-dd}: {string.Join(", ", result.Violations)}");
        Assert.Equal(0, result.DroppedCount);
        Assert.Empty(result.Repairs); // the fixture must not rely on the repair step
        Assert.Equal(Template.Sessions.Count, result.Kept.Count);
        Assert.All(result.Kept, s => Assert.InRange(s.Date, IsoWeek.MondayOf(today).AddDays(42), target.AddDays(-1)));
        foreach (var session in result.Kept)
        {
            Assert.Empty(RunRules.Validate(TrainingSuggestion.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), session, DateTimeOffset.UtcNow).ToRunValues("Europe/Warsaw")));
        }
    }

    [Theory]
    [MemberData(nameof(DemoFacts.EveryDayOf2028), MemberType = typeof(DemoFacts))]
    public void The_fake_plan_is_valid_for_the_demo_goal_on_every_day(DateOnly today)
    {
        var sheet = DemoSheet(today);
        var json = TrainingPlanDraftJson.ToJson(RuleBasedFakePlanWriter.Write(PlanFactSheetJson.ToJson(sheet)));

        var result = TrainingPlanValidator.Validate(json, sheet, PlanValidationPolicy.Default);

        Assert.True(result.IsValid, $"{today:yyyy-MM-dd}: {string.Join(", ", result.Violations)}");
        Assert.Equal(0, result.DroppedCount);
        Assert.Empty(result.Repairs); // the fake writer must not rely on the repair step
    }

    [Fact]
    public void A_broken_fixture_fails_to_parse()
    {
        var json = File.ReadAllText(Path.Combine(RepoPaths.DemoSeed, "training-plan.json"));
        Assert.Throws<Domain.Common.DomainRuleException>(() => DemoTrainingPlanTemplate.Parse(json.Replace("\"day\": \"Sun\"", "\"day\": \"Sunday\"", StringComparison.Ordinal)));
        Assert.Throws<Domain.Common.DomainRuleException>(() => DemoTrainingPlanTemplate.Parse(json.Replace("\"version\": 1", "\"version\": 2", StringComparison.Ordinal)));
        Assert.Throws<Domain.Common.DomainRuleException>(() => DemoTrainingPlanTemplate.Parse(json.Replace("\"notes\":", "\"note\":", StringComparison.Ordinal)));
    }
}
