using System.Net;
using System.Net.Http.Json;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>
/// AC4: for every ordered pair of callers (two guests, two Entra users, guest vs user in both directions) the goals, suggestions and estimates of one
/// never change what the other sees: foreign ids answer 404, a foreign <c>raceRunId</c> is a 400, nothing leaks into lists.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GoalIsolationTests(ContainersApiFactory factory)
{
    private static readonly string[] Kinds = ["guestA", "guestB", "userA", "userB"];

    public static TheoryData<string, string> Pairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var owner in Kinds)
        {
            foreach (var intruder in Kinds.Where(k => k != owner))
            {
                data.Add(owner, intruder);
            }
        }

        return data;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<TestActor> ActorAsync(string kind) => kind.StartsWith("guest", StringComparison.Ordinal)
        ? await factory.GuestAsync(kind + "-" + Guid.NewGuid().ToString("N")[..6])
        : factory.EntraUser(kind + "-" + Guid.NewGuid().ToString("N")[..6]);

    private static async Task<string> SnapshotAsync(TestActor actor) =>
        string.Join(
            '\n',
            await (await actor.Client.GetAsync(U("/api/goals"), Ct)).Content.ReadAsStringAsync(Ct),
            (await actor.ListSuggestionsAsync(0, 90, includeDismissed: true)).ToString(),
            await (await actor.Client.GetAsync(U($"/api/runs?from={Ahead(-30)}&to={Ahead(60)}"), Ct)).Content.ReadAsStringAsync(Ct));

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Goals_suggestions_and_estimates_of_the_owner_are_invisible_and_untouchable_for_the_intruder(string ownerKind, string intruderKind)
    {
        factory.SkipIfDockerUnavailable();
        var owner = await ActorAsync(ownerKind);
        var intruder = await ActorAsync(intruderKind);
        var race = await owner.CreatePlannedRaceRunAsync(45);
        var goal = await owner.CreateGoalAsync(g =>
        {
            g["type"] = "Race";
            g["eventName"] = "Private Race";
            g["targetDate"] = Ahead(45);
            g["raceRunId"] = race.GetProperty("id").GetGuid();
        });
        var goalId = goal.GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(owner, goalId, Session(10), Session(12));
        await factory.SeedPlanAsync(owner, goalId);
        await owner.CreateRaceResultAsync();
        var before = await SnapshotAsync(intruder);

        // The owner's goal: not readable, writable or deletable.
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.GetAsync(U($"/api/goals/{goalId}"), Ct)).StatusCode);
        var put = await intruder.Client.PutAsJsonAsync(U($"/api/goals/{goalId}"),
            Goal(g => { g["status"] = "Archived"; g["version"] = goal.GetProperty("version").GetString(); }), Ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.DeleteAsync(U($"/api/goals/{goalId}"), Ct)).StatusCode);

        // The owner's suggestions: not accepted, not dismissed, not listed.
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.AcceptAsync(ids[0])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.DismissAsync(ids[1])).StatusCode);
        Assert.Empty((await intruder.ListSuggestionsAsync(0, 90, includeDismissed: true)).GetProperty("items").EnumerateArray());
        Assert.Empty((await (await intruder.Client.GetAsync(U("/api/goals"), Ct)).ReadAsync()).GetProperty("items").EnumerateArray());

        // The owner's run as raceRunId is unknown to the intruder (400, not 404: it is a body member).
        var steal = await intruder.PostGoalAsync(Goal(g =>
        {
            g["type"] = "Race";
            g["eventName"] = "Mine";
            g["targetDate"] = Ahead(45);
            g["raceRunId"] = race.GetProperty("id").GetGuid();
        }));
        var problem = await steal.ProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("raceRunId", out _));

        // The owner's results never feed the intruder's estimate.
        var estimate = await (await intruder.Client.PostAsJsonAsync(U("/api/goals/estimate"),
            new { type = "Distance", distanceMeters = 10_000, targetDate = Ahead(60) }, Ct)).ReadAsync();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, estimate.GetProperty("prediction").ValueKind);

        // Nothing changed for the intruder, and nothing happened to the owner's rows.
        Assert.Equal(before, await SnapshotAsync(intruder));
        var owned = await owner.GetGoalJsonAsync(goalId);
        Assert.Equal("Active", owned.GetProperty("status").GetString());
        Assert.Equal(2, owned.GetProperty("plan").GetProperty("pendingCount").GetInt32());
        Assert.Equal(2, await factory.SuggestionRowsAsync(await owner.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_goal_created_by_the_intruder_never_shows_for_the_owner_and_suggestion_accepts_create_runs_for_the_caller_only()
    {
        factory.SkipIfDockerUnavailable();
        var a = await ActorAsync("guestA");
        var b = await ActorAsync("userA");
        var goalA = (await a.CreateGoalAsync(g => g["title"] = "A's goal")).GetProperty("id").GetGuid();
        var goalB = (await b.CreateGoalAsync(g => g["title"] = "B's goal")).GetProperty("id").GetGuid();
        var idsA = await factory.SeedSuggestionsAsync(a, goalA, Session(10));
        var idsB = await factory.SeedSuggestionsAsync(b, goalB, Session(10));

        var runA = await (await a.AcceptAsync(idsA[0])).ReadAsync();

        Assert.Equal(goalA, runA.GetProperty("goalId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.GetAsync(U($"/api/runs/{runA.GetProperty("id").GetGuid()}"), Ct)).StatusCode);
        Assert.Single((await b.ListSuggestionsAsync(0, 30)).GetProperty("items").EnumerateArray());
        Assert.Equal(idsB[0], (await b.ListSuggestionsAsync(0, 30)).GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal("A's goal", (await a.GetGoalJsonAsync(goalA)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync(U($"/api/goals/{goalB}"), Ct)).StatusCode);
    }
}
