using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Persistence;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>AC3: training-suggestion list, accept and dismiss (ADR 0041 §2) over real SQL.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class TrainingSuggestionEndpointTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(TestActor User, Guid GoalId, List<Guid> Ids)> SeededAsync(string name, params int[] days)
    {
        var user = factory.EntraUser(name);
        var goal = await user.CreateGoalAsync();
        var goalId = goal.GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(user, goalId, [.. days.Select(d => Session(d))]);
        return (user, goalId, ids);
    }

    [Fact]
    public async Task List_returns_the_pending_suggestions_in_range_by_date_and_hides_dismissed_ones_by_default()
    {
        factory.SkipIfDockerUnavailable();
        var (user, goalId, ids) = await SeededAsync("sug-list", 14, 10, 12, 100);
        await user.DismissAsync(ids[2]);

        var pending = await user.ListSuggestionsAsync(0, 30);
        var all = await user.ListSuggestionsAsync(0, 30, includeDismissed: true);

        Assert.Equal(Ahead(0), pending.GetProperty("from").GetString());
        Assert.Equal(Ahead(30), pending.GetProperty("to").GetString());
        Assert.Equal([Ahead(10), Ahead(14)], pending.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("date").GetString()));
        Assert.Equal([Ahead(10), Ahead(12), Ahead(14)], all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("date").GetString()));
        var first = pending.GetProperty("items")[0];
        Assert.Equal(goalId, first.GetProperty("goalId").GetGuid());
        Assert.Equal(20, first.GetProperty("runTypeId").GetInt32());
        Assert.Equal(8_000, first.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(2_700, first.GetProperty("durationSeconds").GetInt32());
        Assert.Equal("Easy", first.GetProperty("intensity").GetString());
        Assert.Equal(330, first.GetProperty("targetPaceMinSecondsPerKm").GetInt32());
        Assert.Equal(360, first.GetProperty("targetPaceMaxSecondsPerKm").GetInt32());
        Assert.Equal("Base", first.GetProperty("phase").GetString());
        Assert.Equal("Pending", first.GetProperty("status").GetString());
        Assert.Equal("Dismissed", all.GetProperty("items")[1].GetProperty("status").GetString());
        Assert.DoesNotContain("userId", first.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("from=2026-01-01", "to")]
    [InlineData("to=2026-01-01", "from")]
    [InlineData("from=1949-12-31&to=2026-01-01", "from")]
    [InlineData("from=2026-01-01&to=2101-01-01", "to")]
    [InlineData("from=2026-02-01&to=2026-01-01", "to")]
    [InlineData("from=2026-01-01&to=2026-04-03", "to")] // 93 days
    [InlineData("from=2026-01-01&to=2026-01-31&includeDismissed=maybe", "includeDismissed")]
    public async Task List_validates_the_range_and_the_flag(string query, string key)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("sug-list-invalid");

        var problem = await (await user.Client.GetAsync(U($"/api/training-suggestions?{query}"), Ct)).ProblemAsync(HttpStatusCode.BadRequest);

        Assert.True(problem.GetProperty("errors").TryGetProperty(key, out _), problem.ToString());
    }

    [Fact]
    public async Task List_accepts_the_longest_allowed_span()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("sug-list-span");

        var response = await user.Client.GetAsync(U("/api/training-suggestions?from=2026-01-01&to=2026-04-02"), Ct); // 92 days

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
    }

    [Fact]
    public async Task Accept_without_a_body_creates_the_default_planned_run_linked_to_the_goal_and_removes_the_suggestion()
    {
        factory.SkipIfDockerUnavailable();
        var (user, goalId, ids) = await SeededAsync("sug-accept", 10, 12);

        var response = await user.AcceptAsync(ids[0]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await response.ReadAsync();
        var runId = run.GetProperty("id").GetGuid();
        Assert.Equal($"/api/runs/{runId}", response.Headers.Location?.OriginalString);
        Assert.Equal(Ahead(10), run.GetProperty("date").GetString());
        Assert.Equal("Planned", run.GetProperty("status").GetString());
        Assert.Equal(20, run.GetProperty("runTypeId").GetInt32());
        Assert.Equal(8_000, run.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(2_700, run.GetProperty("durationSeconds").GetInt32());
        Assert.Equal("Easy run, keep it relaxed.", run.GetProperty("notes").GetString());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("startTime").ValueKind);
        Assert.Equal("Etc/UTC", run.GetProperty("timeZoneId").GetString()); // the profile zone of a new Entra user
        Assert.Equal(JsonValueKind.Null, run.GetProperty("title").ValueKind);
        Assert.Equal(goalId, run.GetProperty("goalId").GetGuid());

        var remaining = await user.ListSuggestionsAsync(0, 60, includeDismissed: true);
        Assert.Equal(ids[1], Assert.Single(remaining.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var stored = await (await user.Client.GetAsync(U($"/api/runs/{runId}"), Ct)).ReadAsync();
        Assert.Equal(goalId, stored.GetProperty("goalId").GetGuid());
    }

    [Fact]
    public async Task Accept_of_a_rest_suggestion_creates_a_planned_rest_run()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("sug-accept-rest");
        var goalId = (await user.CreateGoalAsync()).GetProperty("id").GetGuid();
        var id = (await factory.SeedSuggestionsAsync(user, goalId, Session(10, runTypeId: 41, description: "Rest.")))[0];

        var run = await (await user.AcceptAsync(id)).ReadAsync();

        Assert.Equal(41, run.GetProperty("runTypeId").GetInt32());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("distanceMeters").ValueKind);
    }

    [Fact]
    public async Task Accept_with_a_run_body_uses_exactly_those_values_and_still_links_the_goal()
    {
        factory.SkipIfDockerUnavailable();
        var (user, goalId, ids) = await SeededAsync("sug-accept-edit", 10);
        var edited = Json.Run(Ahead(11), r =>
        {
            r["status"] = "Planned";
            r["title"] = "Edited session";
            r["distanceMeters"] = 9_500;
            r["durationSeconds"] = 3_100;
            r["rpe"] = null;
            r["startTime"] = "18:00";
            r["notes"] = "Moved to the evening.";
        });

        var response = await user.AcceptAsync(ids[0], new { run = edited });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await response.ReadAsync();
        Assert.Equal(Ahead(11), run.GetProperty("date").GetString());
        Assert.Equal("Edited session", run.GetProperty("title").GetString());
        Assert.Equal(9_500, run.GetProperty("distanceMeters").GetDouble());
        Assert.Equal("18:00", run.GetProperty("startTime").GetString());
        Assert.Equal("Europe/Warsaw", run.GetProperty("timeZoneId").GetString());
        Assert.Equal(goalId, run.GetProperty("goalId").GetGuid());
        Assert.Empty((await user.ListSuggestionsAsync(0, 60, includeDismissed: true)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Accept_with_an_invalid_run_is_400_with_run_prefixed_paths_and_keeps_the_suggestion()
    {
        factory.SkipIfDockerUnavailable();
        var (user, _, ids) = await SeededAsync("sug-accept-invalid", 10);
        var invalid = Json.Run(Ahead(11), r =>
        {
            r["status"] = "Planned";
            r["distanceMeters"] = -5;
            r["rpe"] = 11;
        });

        var problem = await (await user.AcceptAsync(ids[0], new { run = invalid })).ProblemAsync(HttpStatusCode.BadRequest);

        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("run.distanceMeters", out _), problem.ToString());
        Assert.True(errors.TryGetProperty("run.rpe", out _), problem.ToString());
        Assert.Single((await user.ListSuggestionsAsync(0, 60)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Accept_works_on_a_dismissed_suggestion_and_a_second_accept_is_404()
    {
        factory.SkipIfDockerUnavailable();
        var (user, _, ids) = await SeededAsync("sug-accept-dismissed", 10);
        await user.DismissAsync(ids[0]);

        Assert.Equal(HttpStatusCode.Created, (await user.AcceptAsync(ids[0])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.AcceptAsync(ids[0])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.DismissAsync(ids[0])).StatusCode);
    }

    [Fact]
    public async Task Concurrent_accepts_create_exactly_one_run()
    {
        factory.SkipIfDockerUnavailable();
        var (user, _, ids) = await SeededAsync("sug-accept-race", 10);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => user.AcceptAsync(ids[0])));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.NotFound));
        var runs = await (await user.Client.GetAsync(U($"/api/runs?from={Ahead(10)}&to={Ahead(10)}"), Ct)).ReadAsync();
        Assert.Single(runs.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Accept_respects_the_run_caps_with_409_run_limit_and_keeps_the_suggestion()
    {
        factory.SkipIfDockerUnavailable();
        using var capped = factory.WithWebHostBuilder(b => b.UseSetting("RunLimits:UserMaxRuns", "1"));
        var user = capped.EntraUser("sug-accept-limit");
        var goalId = (await user.CreateGoalAsync()).GetProperty("id").GetGuid();
        var ids = await capped.SeedSuggestionsAsync(user, goalId, Session(10));
        Assert.Equal(HttpStatusCode.Created, (await user.Client.PostAsJsonAsync(U("/api/runs"), Json.Run(Ahead(20), r => r["status"] = "Planned"), Ct)).StatusCode);

        var problem = await (await user.AcceptAsync(ids[0])).ProblemAsync(HttpStatusCode.Conflict);

        Assert.Equal("urn:runnersdiary:problem:run-limit", problem.ProblemType());
        Assert.Equal(1, await capped.SuggestionRowsAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task Dismiss_is_idempotent_and_returns_the_suggestion()
    {
        factory.SkipIfDockerUnavailable();
        var (user, goalId, ids) = await SeededAsync("sug-dismiss", 10);

        var first = await user.DismissAsync(ids[0]);
        var second = await user.DismissAsync(ids[0]);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var dto = await second.ReadAsync();
        Assert.Equal("Dismissed", dto.GetProperty("status").GetString());
        Assert.Equal(ids[0], dto.GetProperty("id").GetGuid());
        Assert.Equal(goalId, dto.GetProperty("goalId").GetGuid());
        Assert.Empty((await user.ListSuggestionsAsync(0, 60)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task An_accepted_run_keeps_its_goal_link_through_an_update()
    {
        factory.SkipIfDockerUnavailable();
        var (user, goalId, ids) = await SeededAsync("sug-accept-update", 10);
        var run = await (await user.AcceptAsync(ids[0])).ReadAsync();
        var runId = run.GetProperty("id").GetGuid();

        var body = Json.Run(Ahead(11), r =>
        {
            r["status"] = "Planned";
            r["rpe"] = null;
            r["version"] = run.GetProperty("version").GetString();
        });
        var put = await user.Client.PutAsJsonAsync(U($"/api/runs/{runId}"), body, Ct);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(goalId, (await put.ReadAsync()).GetProperty("goalId").GetGuid());
        // goalId is read-only: sending it is an unknown member.
        var smuggle = Json.Run(Ahead(11), r => r["goalId"] = Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync(U("/api/runs"), smuggle, Ct)).StatusCode);
    }

    [Fact]
    public async Task Suggestions_are_in_no_run_list_calendar_total_or_insight_until_accepted()
    {
        factory.SkipIfDockerUnavailable();
        var (user, _, ids) = await SeededAsync("sug-invisible", 10, 11, 12);

        var runs = await (await user.Client.GetAsync(U($"/api/runs?from={Ahead(0)}&to={Ahead(30)}"), Ct)).ReadAsync();
        Assert.Empty(runs.GetProperty("items").EnumerateArray());

        await user.AcceptAsync(ids[0]);
        runs = await (await user.Client.GetAsync(U($"/api/runs?from={Ahead(0)}&to={Ahead(30)}"), Ct)).ReadAsync();
        Assert.Single(runs.GetProperty("items").EnumerateArray());
    }
}
