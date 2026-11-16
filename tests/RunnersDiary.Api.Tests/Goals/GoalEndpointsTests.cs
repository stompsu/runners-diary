using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Persistence;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>AC3: goal CRUD behaves as the plan §4.1 specifies: validation matrix, 404s, 409s, cascades, caps and the 503 switch.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GoalEndpointsTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_get_list_update_delete_round_trip()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-crud");

        var response = await user.PostGoalAsync(Performance(2_700, g =>
        {
            g["title"] = "Gliwice 10K under 45";
            g["elevationGainMeters"] = 50;
            g["surface"] = "Road";
            g["location"] = new { latitude = 50.2945, longitude = 18.6714, name = "Gliwice" };
            g["notes"] = "First sub-45 attempt.";
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertNoStore(response);
        var goal = await response.ReadAsync();
        var id = goal.GetProperty("id").GetGuid();
        Assert.Equal($"/api/goals/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Performance", goal.GetProperty("type").GetString());
        Assert.Equal("Active", goal.GetProperty("status").GetString());
        Assert.Equal("ShortTerm", goal.GetProperty("term").GetString()); // 60 days <= 84
        Assert.Equal(2_700, goal.GetProperty("targetDurationSeconds").GetInt32());
        Assert.Equal(50, goal.GetProperty("elevationGainMeters").GetDouble());
        Assert.Equal("Road", goal.GetProperty("surface").GetString());
        Assert.Equal(50.2945, goal.GetProperty("location").GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(JsonValueKind.Null, goal.GetProperty("plan").ValueKind);
        Assert.Equal(JsonValueKind.Null, goal.GetProperty("raceRunId").ValueKind);
        Assert.Equal(JsonValueKind.Null, goal.GetProperty("raceRunDate").ValueKind);
        Assert.False(string.IsNullOrEmpty(goal.GetProperty("version").GetString()));
        Assert.EndsWith("Z", goal.GetProperty("createdAt").GetString(), StringComparison.Ordinal);

        var fetched = await user.GetGoalJsonAsync(id);
        Assert.Equal(goal.GetProperty("version").GetString(), fetched.GetProperty("version").GetString());

        var list = await (await user.Client.GetAsync(U("/api/goals"), Ct)).ReadAsync();
        Assert.Equal(id, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        var put = await user.PutGoalAsync(fetched, g =>
        {
            g["title"] = "Renamed";
            g["targetDurationSeconds"] = 2_650;
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await put.ReadAsync();
        Assert.Equal("Renamed", updated.GetProperty("title").GetString());
        Assert.Equal(2_650, updated.GetProperty("targetDurationSeconds").GetInt32());
        Assert.NotEqual(goal.GetProperty("version").GetString(), updated.GetProperty("version").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync(U($"/api/goals/{id}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync(U($"/api/goals/{id}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.DeleteAsync(U($"/api/goals/{id}"), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_goal_within_twelve_weeks_is_short_term()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-term");

        var near = await user.CreateGoalAsync(g => g["targetDate"] = Ahead(84));
        var far = await user.CreateGoalAsync(g => g["targetDate"] = Ahead(85));

        Assert.Equal("ShortTerm", near.GetProperty("term").GetString());
        Assert.Equal("LongTerm", far.GetProperty("term").GetString());
    }

    [Fact]
    public async Task The_list_is_active_first_then_by_target_date()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-order");
        var archived = await user.CreateGoalAsync(g =>
        {
            g["title"] = "archived soon";
            g["targetDate"] = Ahead(20);
            g["status"] = "Archived";
        });
        var late = await user.CreateGoalAsync(g =>
        {
            g["title"] = "active late";
            g["targetDate"] = Ahead(200);
        });
        var early = await user.CreateGoalAsync(g =>
        {
            g["title"] = "active early";
            g["targetDate"] = Ahead(30);
        });

        var list = await (await user.Client.GetAsync(U("/api/goals"), Ct)).ReadAsync();

        Assert.Equal(
            [early.GetProperty("id").GetGuid(), late.GetProperty("id").GetGuid(), archived.GetProperty("id").GetGuid()],
            list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
    }

    public static TheoryData<string, string, Action<Dictionary<string, object?>>> InvalidBodies() => new()
    {
        { "distance below", "distanceMeters", g => g["distanceMeters"] = 999 },
        { "distance above", "distanceMeters", g => g["distanceMeters"] = 250_001 },
        { "elevation negative", "elevationGainMeters", g => g["elevationGainMeters"] = -1 },
        { "elevation above", "elevationGainMeters", g => g["elevationGainMeters"] = 10_001 },
        { "date too soon", "targetDate", g => g["targetDate"] = Ahead(6) },
        { "date too far", "targetDate", g => g["targetDate"] = Ahead(731) },
        { "title empty", "title", g => g["title"] = "" },
        { "title blank", "title", g => g["title"] = "   " },
        { "title long", "title", g => g["title"] = new string('x', 121) },
        { "title null", "title", g => g["title"] = null },
        { "event name long", "eventName", g => g["eventName"] = new string('x', 151) },
        { "notes long", "notes", g => g["notes"] = new string('x', 1_001) },
        { "race without event name", "eventName", g => g["type"] = "Race" },
        { "performance without target", "targetDurationSeconds", g => g["type"] = "Performance" },
        { "distance with target", "targetDurationSeconds", g => g["targetDurationSeconds"] = 3_000 },
        { "pace too fast", "targetDurationSeconds", g => { g["type"] = "Performance"; g["targetDurationSeconds"] = 1_490; } },
        { "pace too slow", "targetDurationSeconds", g => { g["type"] = "Performance"; g["targetDurationSeconds"] = 9_010; } },
        { "target zero", "targetDurationSeconds", g => { g["type"] = "Performance"; g["targetDurationSeconds"] = 0; } },
        { "latitude", "location.latitude", g => g["location"] = new { latitude = 91, longitude = 18 } },
        { "location name long", "location.name", g => g["location"] = new { latitude = 50, longitude = 18, name = new string('x', 201) } },
        { "unknown race run", "raceRunId", g => g["raceRunId"] = Guid.NewGuid() },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_rejects_invalid_bodies_with_the_camelCase_path(string reason, string path, Action<Dictionary<string, object?>> change)
    {
        factory.SkipIfDockerUnavailable();
        _ = reason;
        var user = factory.EntraUser("goal-invalid");

        var response = await user.PostGoalAsync(Goal(change));

        var problem = await response.ProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty(path, out _), problem.ToString());
    }

    [Theory]
    [InlineData("{\"type\":\"Distance\",\"title\":\"x\",\"targetDate\":\"2099-01-01\",\"distanceMeters\":10000,\"userId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"type\":\"Marathon\",\"title\":\"x\",\"targetDate\":\"2099-01-01\",\"distanceMeters\":10000}")]
    [InlineData("{\"title\":\"x\",\"targetDate\":\"2099-01-01\",\"distanceMeters\":10000}")]
    [InlineData("{ not json")]
    public async Task Create_rejects_unknown_members_unknown_enums_missing_members_and_malformed_json(string json)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-shape");
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await user.Client.PostAsync(U("/api/goals"), content, Ct);

        await response.ProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_documented_boundary_values_are_accepted()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-bounds");

        await user.CreateGoalAsync(g =>
        {
            g["distanceMeters"] = 1_000;
            g["targetDate"] = Ahead(7);
            g["elevationGainMeters"] = 0;
        });
        await user.CreateGoalAsync(g =>
        {
            g["distanceMeters"] = 250_000;
            g["targetDate"] = Ahead(730);
            g["elevationGainMeters"] = 10_000;
        });
        await user.CreateGoalAsync(g =>
        {
            g["type"] = "Performance";
            g["targetDurationSeconds"] = 1_500; // 150 s/km
        });
        await user.CreateGoalAsync(g =>
        {
            g["type"] = "Performance";
            g["targetDurationSeconds"] = 9_000; // 900 s/km
        });
        await user.CreateGoalAsync(g =>
        {
            g["type"] = "Race";
            g["eventName"] = "Gliwice 10K";
            g["title"] = new string('x', 120);
        });
    }

    [Fact]
    public async Task Update_needs_status_and_version_and_a_stale_version_conflicts()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-version");
        var goal = await user.CreateGoalAsync();
        var id = goal.GetProperty("id").GetGuid();

        var noVersion = await user.PutGoalAsync(goal, g => g.Remove("version"));
        var noStatus = await user.PutGoalAsync(goal, g => g.Remove("status"));
        var badVersion = await user.PutGoalAsync(goal, g => g["version"] = "***");

        Assert.True((await noVersion.ProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("version", out _));
        Assert.True((await noStatus.ProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("status", out _));
        Assert.True((await badVersion.ProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("version", out _));

        // Two editors: the second PUT carries the old version.
        Assert.Equal(HttpStatusCode.OK, (await user.PutGoalAsync(goal, g => g["title"] = "First editor")).StatusCode);
        var stale = await user.PutGoalAsync(goal, g => g["title"] = "Second editor");

        var problem = await stale.ProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal("urn:runnersdiary:problem:concurrency-conflict", problem.ProblemType());
        Assert.Equal("First editor", (await user.GetGoalJsonAsync(id)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Update_of_an_unknown_goal_is_404_and_invalid_values_are_400()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-update-errors");
        var goal = await user.CreateGoalAsync();

        var missing = await user.Client.PutAsJsonAsync(U($"/api/goals/{Guid.NewGuid()}"),
            Goal(g => { g["status"] = "Active"; g["version"] = goal.GetProperty("version").GetString(); }), Ct);
        var invalid = await user.PutGoalAsync(goal, g => g["distanceMeters"] = 500);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.True((await invalid.ProblemAsync(HttpStatusCode.BadRequest)).GetProperty("errors").TryGetProperty("distanceMeters", out _));
    }

    [Fact]
    public async Task A_user_is_capped_by_Goals_MaxGoalsPerUser_with_409_goal_limit()
    {
        factory.SkipIfDockerUnavailable();
        using var capped = factory.WithWebHostBuilder(b => b.UseSetting("Goals:MaxGoalsPerUser", "2"));
        var user = capped.EntraUser("goal-cap-user");

        await user.CreateGoalAsync();
        await user.CreateGoalAsync();
        var third = await user.PostGoalAsync(Goal());

        var problem = await third.ProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal("urn:runnersdiary:problem:goal-limit", problem.ProblemType());
        // Deleting one makes room again.
        var list = await (await user.Client.GetAsync(U("/api/goals"), Ct)).ReadAsync();
        var first = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync(U($"/api/goals/{first}"), Ct)).StatusCode);
        await user.CreateGoalAsync();
    }

    [Fact]
    public async Task A_guest_is_capped_at_ten_goals_by_default()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("goal-cap-guest");

        for (var i = 0; i < 10; i++)
        {
            await guest.CreateGoalAsync();
        }

        var problem = await (await guest.PostGoalAsync(Goal())).ProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal("urn:runnersdiary:problem:goal-limit", problem.ProblemType());
    }

    [Fact]
    public async Task A_body_that_is_invalid_is_400_even_at_the_cap()
    {
        factory.SkipIfDockerUnavailable();
        using var capped = factory.WithWebHostBuilder(b => b.UseSetting("Goals:MaxGoalsPerUser", "1"));
        var user = capped.EntraUser("goal-cap-order");
        await user.CreateGoalAsync();

        var response = await user.PostGoalAsync(Goal(g => g["distanceMeters"] = 1));

        await response.ProblemAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Leaving_Active_deletes_the_suggestions_but_a_plan_field_change_only_marks_the_plan_outdated()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-status");
        var goal = await user.CreateGoalAsync();
        var id = goal.GetProperty("id").GetGuid();
        await factory.SeedSuggestionsAsync(user, id, Session(10), Session(12), Session(14));
        await factory.SeedPlanAsync(user, id);
        var withPlan = await user.GetGoalJsonAsync(id);
        var plan = withPlan.GetProperty("plan");
        Assert.Equal("Model", plan.GetProperty("source").GetString());
        Assert.Equal(3, plan.GetProperty("pendingCount").GetInt32());
        Assert.False(plan.GetProperty("outdated").GetBoolean());

        // A title change keeps the plan current.
        var renamed = await (await user.PutGoalAsync(withPlan, g => g["title"] = "Renamed")).ReadAsync();
        Assert.False(renamed.GetProperty("plan").GetProperty("outdated").GetBoolean());

        // A plan field changes: the suggestions stay, the plan is outdated.
        var moved = await (await user.PutGoalAsync(renamed, g => g["targetDate"] = Ahead(70))).ReadAsync();
        Assert.True(moved.GetProperty("plan").GetProperty("outdated").GetBoolean());
        Assert.Equal(3, moved.GetProperty("plan").GetProperty("pendingCount").GetInt32());
        Assert.Equal(3, await factory.SuggestionRowsAsync(await user.GetUserIdAsync()));

        // Leaving Active removes the suggestions (Pending and Dismissed) in the same transaction.
        var archived = await (await user.PutGoalAsync(moved, g => g["status"] = "Archived")).ReadAsync();
        Assert.Equal("Archived", archived.GetProperty("status").GetString());
        Assert.Equal(0, archived.GetProperty("plan").GetProperty("pendingCount").GetInt32());
        Assert.Equal(0, await factory.SuggestionRowsAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_stale_status_change_does_not_delete_suggestions()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-status-stale");
        var goal = await user.CreateGoalAsync();
        var id = goal.GetProperty("id").GetGuid();
        await factory.SeedSuggestionsAsync(user, id, Session(10), Session(12));
        Assert.Equal(HttpStatusCode.OK, (await user.PutGoalAsync(goal, g => g["title"] = "Edited elsewhere")).StatusCode);

        var stale = await user.PutGoalAsync(goal, g => g["status"] = "Achieved");

        await stale.ProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal(2, await factory.SuggestionRowsAsync(await user.GetUserIdAsync()));
        Assert.Equal("Active", (await user.GetGoalJsonAsync(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_demo_source_label_follows_the_plan_metadata()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-demo-plan");
        var goal = await user.CreateGoalAsync();
        var id = goal.GetProperty("id").GetGuid();
        await factory.SeedPlanAsync(user, id, demo: true);

        var plan = (await user.GetGoalJsonAsync(id)).GetProperty("plan");

        Assert.Equal("Demo", plan.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("model").ValueKind);
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("promptVersion").ValueKind);
    }

    [Fact]
    public async Task Race_run_links_are_checked_and_the_goal_shows_the_runs_current_date()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-racerun");
        var other = factory.EntraUser("goal-racerun-other");
        var race = await user.CreatePlannedRaceRunAsync(45);
        var raceId = race.GetProperty("id").GetGuid();
        var easy = await user.CreatePlannedRaceRunAsync(45, runTypeId: 20);
        var completed = await user.CreateRaceResultAsync(daysAgo: 5);
        var foreign = await other.CreatePlannedRaceRunAsync(45);

        async Task<HttpResponseMessage> Post(Guid runId, int days = 45) =>
            await user.PostGoalAsync(Goal(g =>
            {
                g["type"] = "Race";
                g["eventName"] = "Gliwice 10K";
                g["targetDate"] = Ahead(days);
                g["raceRunId"] = runId;
            }));

        foreach (var (runId, days) in new[]
        {
            (Guid.NewGuid(), 45), // unknown
            (foreign.GetProperty("id").GetGuid(), 45), // another user's run looks unknown
            (easy.GetProperty("id").GetGuid(), 45), // not a race type
            (completed.GetProperty("id").GetGuid(), 45), // completed (and on another date)
            (raceId, 46), // wrong date
        })
        {
            var problem = await (await Post(runId, days)).ProblemAsync(HttpStatusCode.BadRequest);
            Assert.True(problem.GetProperty("errors").TryGetProperty("raceRunId", out _), problem.ToString());
        }

        var created = await (await Post(raceId)).ReadAsync();
        Assert.Equal(raceId, created.GetProperty("raceRunId").GetGuid());
        Assert.Equal(Ahead(45), created.GetProperty("raceRunDate").GetString());

        // The run moves: the goal keeps its own date and reports the run's current one.
        var runBody = Json.Run(Ahead(52), r =>
        {
            r["status"] = "Planned";
            r["runTypeId"] = 1;
            r["durationSeconds"] = null;
            r["rpe"] = null;
            r["race"] = new { name = "Gliwice 10K", distanceCategory = "TenK" };
            r["version"] = race.GetProperty("version").GetString();
        });
        Assert.Equal(HttpStatusCode.OK, (await user.Client.PutAsJsonAsync(U($"/api/runs/{raceId}"), runBody, Ct)).StatusCode);
        var afterMove = await user.GetGoalJsonAsync(created.GetProperty("id").GetGuid());
        Assert.Equal(Ahead(45), afterMove.GetProperty("targetDate").GetString());
        Assert.Equal(Ahead(52), afterMove.GetProperty("raceRunDate").GetString());
    }

    [Fact]
    public async Task Deleting_the_linked_run_clears_the_goals_race_run_link()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-racerun-delete");
        var race = await user.CreatePlannedRaceRunAsync(45);
        var raceId = race.GetProperty("id").GetGuid();
        var goal = await user.CreateGoalAsync(g =>
        {
            g["type"] = "Race";
            g["eventName"] = "Gliwice 10K";
            g["targetDate"] = Ahead(45);
            g["raceRunId"] = raceId;
        });
        var goalId = goal.GetProperty("id").GetGuid();

        var delete = await user.Client.DeleteAsync(U($"/api/runs/{raceId}"), Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var after = await user.GetGoalJsonAsync(goalId);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("raceRunId").ValueKind);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("raceRunDate").ValueKind);
    }

    [Fact]
    public async Task Deleting_a_goal_removes_its_suggestions_and_keeps_the_runs_accepted_from_it_with_a_null_goalId()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-delete");
        var goal = await user.CreateGoalAsync();
        var goalId = goal.GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(user, goalId, Session(10), Session(12), Session(14));
        var accepted = await (await user.AcceptAsync(ids[0])).ReadAsync();
        var runId = accepted.GetProperty("id").GetGuid();
        Assert.Equal(goalId, accepted.GetProperty("goalId").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync(U($"/api/goals/{goalId}"), Ct)).StatusCode);

        Assert.Equal(0, await factory.SuggestionRowsAsync(await user.GetUserIdAsync()));
        var run = await (await user.Client.GetAsync(U($"/api/runs/{runId}"), Ct)).ReadAsync();
        Assert.Equal(JsonValueKind.Null, run.GetProperty("goalId").ValueKind);
        Assert.Equal(0, await factory.GoalRowsAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task The_goal_counts_follow_accept_and_dismiss()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-counts");
        var goal = await user.CreateGoalAsync();
        var goalId = goal.GetProperty("id").GetGuid();
        var ids = await factory.SeedSuggestionsAsync(user, goalId, Session(10), Session(12), Session(14), Session(16));
        await factory.SeedPlanAsync(user, goalId);

        await user.DismissAsync(ids[0]);
        await user.DismissAsync(ids[1]);
        await user.AcceptAsync(ids[2]);

        var plan = (await user.GetGoalJsonAsync(goalId)).GetProperty("plan");
        Assert.Equal(1, plan.GetProperty("pendingCount").GetInt32());
        Assert.Equal(2, plan.GetProperty("dismissedCount").GetInt32());
        Assert.Equal(1, plan.GetProperty("acceptedCount").GetInt32());
    }

    [Fact]
    public async Task Both_groups_answer_503_while_the_feature_is_off_but_after_authentication()
    {
        factory.SkipIfDockerUnavailable();
        using var off = factory.WithWebHostBuilder(b => b.UseSetting("Goals:Enabled", "false"));
        var user = off.EntraUser("goals-off");
        var id = Guid.NewGuid();

        var calls = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/api/goals", null),
            (HttpMethod.Get, $"/api/goals/{id}", null),
            (HttpMethod.Post, "/api/goals", Goal()),
            (HttpMethod.Put, $"/api/goals/{id}", Goal(g => { g["status"] = "Active"; g["version"] = "AAAA"; })),
            (HttpMethod.Delete, $"/api/goals/{id}", null),
            (HttpMethod.Post, "/api/goals/estimate", new { type = "Distance", distanceMeters = 10_000, targetDate = Ahead(60) }),
            (HttpMethod.Post, $"/api/goals/{id}/plan", null),
            (HttpMethod.Get, $"/api/training-suggestions?from={Ahead(0)}&to={Ahead(10)}", null),
            (HttpMethod.Post, $"/api/training-suggestions/{id}/accept", null),
            (HttpMethod.Post, $"/api/training-suggestions/{id}/dismiss", null),
        };

        foreach (var (method, path, body) in calls)
        {
            using var request = new HttpRequestMessage(method, U(path));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            var problem = await (await user.Client.SendAsync(request, Ct)).ProblemAsync(HttpStatusCode.ServiceUnavailable);
            Assert.Equal("urn:runnersdiary:problem:feature-unavailable", problem.ProblemType());

            using var anonymous = new HttpRequestMessage(method, U(path));
            Assert.Equal(HttpStatusCode.Unauthorized, (await off.CreateClient().SendAsync(anonymous, Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Goals_never_leak_the_owner_id_or_internal_columns()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("goal-shape-out");
        var goal = await user.CreateGoalAsync();

        var names = goal.EnumerateObject().Select(p => p.Name).Order().ToList();

        Assert.DoesNotContain("userId", names);
        Assert.DoesNotContain("rowVersion", names);
        Assert.Equal(
            ["createdAt", "distanceMeters", "elevationGainMeters", "eventName", "id", "location", "notes", "plan", "raceRunDate", "raceRunId", "status", "surface",
                "targetDate", "targetDurationSeconds", "term", "title", "type", "updatedAt", "version"],
            names);
    }
}
