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
/// AC4 for the one route <see cref="GoalIsolationTests"/> does not walk: <c>POST /api/goals/{goalId}/plan</c>. For every ordered pair of callers
/// (two guests, two Entra users, guest vs user both ways) generating against another account's goal is a 404 that never reaches the model,
/// never spends the intruder's quota or writes a ledger row, and never changes the owner's suggestions. The intruder's own generation with
/// <c>replaceOtherGoals=true</c> never replaces the owner's suggestions either.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GoalPlanIsolationTests(ContainersApiFactory factory) : IAsyncLifetime
{
    private readonly List<TestActor> _actors = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Pairs() => GoalIsolationTests.Pairs();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        var ids = _actors.Where(a => a.UserId is not null).Select(a => a.UserId!.Value).ToList();
        if (ids.Count > 0)
        {
            await factory.WithDbAsync(db => db.AiGenerations.Where(g => ids.Contains(g.UserId)).ExecuteDeleteAsync(CancellationToken.None));
        }
    }

    private async Task<TestActor> ActorAsync(WebApplicationFactory<Program> host, string kind)
    {
        var name = kind + "-" + Guid.NewGuid().ToString("N")[..6];
        var actor = kind.StartsWith("guest", StringComparison.Ordinal) ? await host.GuestAsync(name) : host.EntraUser(name);
        await actor.GetUserIdAsync();
        _actors.Add(actor);
        for (var i = 0; i < 4; i++)
        {
            var response = await actor.Client.PostAsJsonAsync(U("/api/runs"), Json.Run(Ahead(-3 - (4 * i))), Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        return actor;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Generating_against_a_foreign_goal_is_404_and_changes_nothing_for_either_account(string ownerKind, string intruderKind)
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string>
        {
            ["Ai:Quotas:GlobalPer24h"] = "100000",
            ["Ai:Quotas:GuestGlobalPer24h"] = "100000",
        });
        var owner = await ActorAsync(host, ownerKind);
        var intruder = await ActorAsync(host, intruderKind);
        var ownerGoal = (await owner.CreateGoalAsync()).GetProperty("id").GetGuid();
        var ownerIds = await factory.SeedSuggestionsAsync(owner, ownerGoal, Session(10), Session(12));
        await factory.SeedPlanAsync(owner, ownerGoal);

        foreach (var body in new object?[] { new { }, new { replaceOtherGoals = true } })
        {
            var response = await intruder.Client.PostAsJsonAsync(U($"/api/goals/{ownerGoal}/plan"), body, Ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(0, client.CallCount);
        Assert.Empty(await factory.LedgerAsync(intruder.UserId!.Value));
        Assert.Empty(await factory.LedgerAsync(owner.UserId!.Value));
        Assert.Equal(2, await factory.SuggestionRowsAsync(owner.UserId!.Value));
        Assert.Equal(0, await factory.SuggestionRowsAsync(intruder.UserId!.Value));
        var listed = (await owner.ListSuggestionsAsync(0, 60)).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid());
        Assert.Equal(ownerIds.Order(), listed.Order());
    }

    [Fact]
    public async Task An_intruders_own_generation_with_replace_other_goals_never_touches_the_owners_suggestions()
    {
        factory.SkipIfDockerUnavailable();
        var client = new ScriptedChatClient().ReplyValid();
        using var host = AiTestHost.Create(factory, client, new Dictionary<string, string>
        {
            ["Ai:Quotas:GlobalPer24h"] = "100000",
            ["Ai:Quotas:GuestGlobalPer24h"] = "100000",
        });
        var owner = await ActorAsync(host, "guestA");
        var intruder = await ActorAsync(host, "userA");
        var ownerGoal = (await owner.CreateGoalAsync()).GetProperty("id").GetGuid();
        await factory.SeedSuggestionsAsync(owner, ownerGoal, Session(10), Session(12));
        await factory.SeedPlanAsync(owner, ownerGoal);
        var intruderGoal = (await intruder.CreateGoalAsync()).GetProperty("id").GetGuid();
        await factory.SeedSuggestionsAsync(intruder, intruderGoal, Session(11));

        var response = await intruder.Client.PostAsJsonAsync(U($"/api/goals/{intruderGoal}/plan"), new { replaceOtherGoals = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync();

        // Only the intruder's own other goals count; the owner's goal id is never reported or replaced.
        Assert.Equal(0, body.GetProperty("replacedGoalIds").GetArrayLength());
        Assert.Equal(2, await factory.SuggestionRowsAsync(owner.UserId!.Value));
        Assert.Equal(2, (await owner.GetGoalJsonAsync(ownerGoal)).GetProperty("plan").GetProperty("pendingCount").GetInt32());
        Assert.Empty(await factory.LedgerAsync(owner.UserId!.Value));
    }
}
