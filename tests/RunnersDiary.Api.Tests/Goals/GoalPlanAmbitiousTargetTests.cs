using RunnersDiary.Api.Tests.Infrastructure;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>M9.1: <c>GoalDto.plan.ambitiousTarget</c> / <c>trainsTowardSeconds</c> are computed from the stored prediction and the current target (ADR 0040 amendment 2026-11-21).</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GoalPlanAmbitiousTargetTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(TestActor User, Guid GoalId)> SeededAsync(string name, int target, int? predicted)
    {
        var user = factory.EntraUser(name + "-" + Guid.NewGuid().ToString("N")[..6]);
        var goal = await user.CreateGoalAsync(g =>
        {
            g["type"] = "Performance";
            g["targetDurationSeconds"] = target;
        });
        var id = goal.GetProperty("id").GetGuid();
        await factory.SeedPlanAsync(user, id, predictedDurationSeconds: predicted);
        return (user, id);
    }

    [Theory]
    [InlineData(2_849, 3_000, true)]  // 2849 < 0.95 x 3000 = 2850
    [InlineData(2_850, 3_000, false)]
    [InlineData(2_700, 2_700, false)]
    public async Task The_flag_follows_the_ninety_five_percent_rule_on_the_stored_prediction(int target, int predicted, bool ambitious)
    {
        factory.SkipIfDockerUnavailable();
        var (user, id) = await SeededAsync("ambitious-rule", target, predicted);

        var plan = (await user.GetGoalJsonAsync(id)).GetProperty("plan");

        Assert.Equal(ambitious, plan.GetProperty("ambitiousTarget").GetBoolean());
        if (ambitious)
        {
            Assert.Equal(predicted, plan.GetProperty("trainsTowardSeconds").GetInt32());
        }
        else
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, plan.GetProperty("trainsTowardSeconds").ValueKind);
        }
    }

    [Fact]
    public async Task A_plan_without_a_stored_prediction_is_never_ambitious()
    {
        factory.SkipIfDockerUnavailable();
        var (user, id) = await SeededAsync("ambitious-legacy", 1_500, predicted: null); // a plan from before M9.1: NULL column

        var plan = (await user.GetGoalJsonAsync(id)).GetProperty("plan");

        Assert.False(plan.GetProperty("ambitiousTarget").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, plan.GetProperty("trainsTowardSeconds").ValueKind);
    }

    [Fact]
    public async Task Changing_the_target_marks_the_plan_outdated_and_the_flag_follows_the_new_target()
    {
        factory.SkipIfDockerUnavailable();
        var (user, id) = await SeededAsync("ambitious-edit", 2_700, 3_000);
        var current = await user.GetGoalJsonAsync(id);
        Assert.True(current.GetProperty("plan").GetProperty("ambitiousTarget").GetBoolean());

        // "Use as target": the target becomes trainsTowardSeconds.
        var updated = await (await user.PutGoalAsync(current, g => g["targetDurationSeconds"] = 3_000)).ReadAsync();

        var plan = updated.GetProperty("plan");
        Assert.True(plan.GetProperty("outdated").GetBoolean());
        Assert.False(plan.GetProperty("ambitiousTarget").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, plan.GetProperty("trainsTowardSeconds").ValueKind);
    }

    [Fact]
    public async Task The_list_carries_the_same_fields_and_a_foreign_goal_stays_not_found()
    {
        factory.SkipIfDockerUnavailable();
        var (owner, id) = await SeededAsync("ambitious-list", 2_700, 3_000);
        var other = factory.EntraUser("ambitious-other-" + Guid.NewGuid().ToString("N")[..6]);

        var list = await (await owner.Client.GetAsync(U("/api/goals"), Ct)).ReadAsync();
        var item = list.GetProperty("items").EnumerateArray().Single(g => g.GetProperty("id").GetGuid() == id);
        Assert.True(item.GetProperty("plan").GetProperty("ambitiousTarget").GetBoolean());
        Assert.Equal(3_000, item.GetProperty("plan").GetProperty("trainsTowardSeconds").GetInt32());

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await other.Client.GetAsync(U($"/api/goals/{id}"), Ct)).StatusCode);
    }
}
