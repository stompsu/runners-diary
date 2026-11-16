using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Api.Tests.Insights;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>
/// One-active-goal rule (plan §4.1, ADR 0041): only another goal's <em>Pending</em> suggestions block a generation. Dismissed-only,
/// suggestion-less and non-Active goals do not, and a replacement never reaches goals that were not holding pending suggestions.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GeneratePlanOtherGoalRulesTests(ContainersApiFactory factory) : IAsyncLifetime
{
    private TestActor? _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_user?.UserId is { } id)
        {
            await factory.WithDbAsync(db => db.AiGenerations.Where(g => g.UserId == id).ExecuteDeleteAsync(CancellationToken.None));
        }
    }

    private WebApplicationFactory<Program> Host(ScriptedChatClient client) => AiTestHost.Create(factory, client, new Dictionary<string, string>
    {
        ["Ai:Quotas:GlobalPer24h"] = "100000",
        ["Ai:Quotas:GuestGlobalPer24h"] = "100000",
    });

    private async Task<TestActor> UserAsync(WebApplicationFactory<Program> host)
    {
        var actor = host.EntraUser("other-rules-" + Guid.NewGuid().ToString("N")[..6]);
        await actor.GetUserIdAsync();
        _user = actor;
        for (var i = 0; i < 4; i++)
        {
            var response = await actor.Client.PostAsJsonAsync(U("/api/runs"), Json.Run(Ahead(-3 - (4 * i))), Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        return actor;
    }

    private static Task<HttpResponseMessage> GenerateAsync(TestActor actor, Guid goalId, object? body = null) =>
        actor.Client.PostAsJsonAsync(U($"/api/goals/{goalId}/plan"), body ?? new { }, Ct);

    [Fact]
    public async Task Another_goal_holding_only_dismissed_suggestions_does_not_block_and_is_not_replaced()
    {
        factory.SkipIfDockerUnavailable();
        using var host = Host(new ScriptedChatClient().ReplyValid());
        var user = await UserAsync(host);
        var a = (await user.CreateGoalAsync(g => g["title"] = "Goal A")).GetProperty("id").GetGuid();
        var b = (await user.CreateGoalAsync(g =>
        {
            g["title"] = "Goal B";
            g["targetDate"] = Ahead(70);
        })).GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(user, a, Session(10), Session(12));
        await factory.SeedPlanAsync(user, a);
        foreach (var id in ids)
        {
            Assert.Equal(HttpStatusCode.OK, (await user.DismissAsync(id)).StatusCode);
        }

        var response = await GenerateAsync(user, b);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var body = await response.ReadAsync();
        Assert.Equal(0, body.GetProperty("replacedGoalIds").GetArrayLength());
        var plan = (await user.GetGoalJsonAsync(a)).GetProperty("plan");
        Assert.Equal((0, 2), (plan.GetProperty("pendingCount").GetInt32(), plan.GetProperty("dismissedCount").GetInt32()));
    }

    [Fact]
    public async Task A_goal_without_a_plan_or_a_non_active_goal_never_blocks()
    {
        factory.SkipIfDockerUnavailable();
        using var host = Host(new ScriptedChatClient().ReplyValid());
        var user = await UserAsync(host);
        await user.CreateGoalAsync(g => g["title"] = "Never planned");
        var archived = await user.CreateGoalAsync(g =>
        {
            g["title"] = "Archived";
            g["targetDate"] = Ahead(40);
        });
        var archivedId = archived.GetProperty("id").GetGuid();
        await factory.SeedSuggestionsAsync(user, archivedId, Session(10));
        var put = await user.PutGoalAsync(archived, g => g["status"] = "Archived");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var target = (await user.CreateGoalAsync(g =>
        {
            g["title"] = "Target";
            g["targetDate"] = Ahead(70);
        })).GetProperty("id").GetGuid();

        var response = await GenerateAsync(user, target);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(0, (await response.ReadAsync()).GetProperty("replacedGoalIds").GetArrayLength());
    }
}
