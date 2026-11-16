using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>Helpers of the goal and training-suggestion endpoint tests.</summary>
internal static class GoalTestSupport
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Today in UTC: Entra test users start in Etc/UTC. Tests place dates well inside the windows, except the explicit boundary tests.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static string Ahead(int days) => Today.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Uri U(string path) => new(path, UriKind.Relative);

    /// <summary>A valid Distance goal (10 km, 60 days ahead); override via the dictionary.</summary>
    public static Dictionary<string, object?> Goal(Action<Dictionary<string, object?>>? change = null)
    {
        var goal = new Dictionary<string, object?>
        {
            ["type"] = "Distance",
            ["title"] = "Autumn 10K",
            ["targetDate"] = Ahead(60),
            ["distanceMeters"] = 10_000,
        };
        change?.Invoke(goal);
        return goal;
    }

    public static Dictionary<string, object?> Performance(int seconds = 2_700, Action<Dictionary<string, object?>>? change = null) =>
        Goal(g =>
        {
            g["type"] = "Performance";
            g["targetDurationSeconds"] = seconds;
            change?.Invoke(g);
        });

    public static async Task<HttpResponseMessage> PostGoalAsync(this TestActor actor, object body) =>
        await actor.Client.PostAsJsonAsync(U("/api/goals"), body, Ct);

    public static async Task<JsonElement> CreateGoalAsync(this TestActor actor, Action<Dictionary<string, object?>>? change = null)
    {
        var response = await actor.PostGoalAsync(Goal(change));
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync();
    }

    public static async Task<JsonElement> GetGoalJsonAsync(this TestActor actor, Guid goalId)
    {
        var response = await actor.Client.GetAsync(U($"/api/goals/{goalId}"), Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync();
    }

    /// <summary>PUT the goal as the dictionary says (status and version are filled from <paramref name="current"/> unless given).</summary>
    public static async Task<HttpResponseMessage> PutGoalAsync(this TestActor actor, JsonElement current, Action<Dictionary<string, object?>> change)
    {
        var body = Goal(g =>
        {
            g["type"] = current.GetProperty("type").GetString();
            g["title"] = current.GetProperty("title").GetString();
            g["targetDate"] = current.GetProperty("targetDate").GetString();
            g["distanceMeters"] = current.GetProperty("distanceMeters").GetDouble();
            if (current.GetProperty("targetDurationSeconds").ValueKind == JsonValueKind.Number)
            {
                g["targetDurationSeconds"] = current.GetProperty("targetDurationSeconds").GetInt32();
            }

            g["status"] = current.GetProperty("status").GetString();
            g["version"] = current.GetProperty("version").GetString();
        });
        change(body);
        return await actor.Client.PutAsJsonAsync(U($"/api/goals/{current.GetProperty("id").GetGuid()}"), body, Ct);
    }

    public static async Task<JsonElement> ProblemAsync(this HttpResponseMessage response, System.Net.HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.ReadAsync();
    }

    /// <summary>The per-caller responses are marked <c>private, no-store</c> (the framework may reorder the directives).</summary>
    public static void AssertNoStore(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl is { NoStore: true, Private: true }, response.Headers.CacheControl?.ToString());
    }

    public static string ProblemType(this JsonElement problem) => problem.GetProperty("type").GetString()!;

    public static async Task<JsonElement> ListSuggestionsAsync(this TestActor actor, int fromDays = 0, int toDays = 60, bool includeDismissed = false)
    {
        var url = $"/api/training-suggestions?from={Ahead(fromDays)}&to={Ahead(toDays)}&includeDismissed={includeDismissed.ToString().ToLowerInvariant()}";
        var response = await actor.Client.GetAsync(U(url), Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync();
    }

    public static async Task<HttpResponseMessage> AcceptAsync(this TestActor actor, Guid suggestionId, object? body = null) =>
        body is null
            ? await actor.Client.PostAsync(U($"/api/training-suggestions/{suggestionId}/accept"), content: null, Ct)
            : await actor.Client.PostAsJsonAsync(U($"/api/training-suggestions/{suggestionId}/accept"), body, Ct);

    public static async Task<HttpResponseMessage> DismissAsync(this TestActor actor, Guid suggestionId) =>
        await actor.Client.PostAsync(U($"/api/training-suggestions/{suggestionId}/dismiss"), content: null, Ct);

    /// <summary>Runs <paramref name="action"/> on a context whose caller is the actor (the ownership guard allows writes of that user only).</summary>
    public static async Task<T> WithUserDbAsync<T>(
        this WebApplicationFactory<Program> factory, TestActor actor, Func<RunnersDiaryDbContext, Guid, Task<T>> action)
    {
        var userId = await actor.GetUserIdAsync();
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>();
        await using var db = new RunnersDiaryDbContext(options, new FixedCurrentUser(userId));
        return await action(db, userId);
    }

    public static PlannedSession Session(int daysAhead, short runTypeId = 20, string description = "Easy run, keep it relaxed.") =>
        new(Today.AddDays(daysAhead), runTypeId, runTypeId == 41 ? null : 8_000, runTypeId == 41 ? null : 2_700,
            runTypeId == 41 ? TrainingIntensity.Rest : TrainingIntensity.Easy,
            runTypeId == 41 ? null : 330, runTypeId == 41 ? null : 360, description, TrainingPhase.Base);

    /// <summary>Inserts suggestions for the goal directly (the plan generation endpoint is a later step) and returns their ids.</summary>
    public static async Task<List<Guid>> SeedSuggestionsAsync(
        this WebApplicationFactory<Program> factory, TestActor actor, Guid goalId, params PlannedSession[] sessions) =>
        await factory.WithUserDbAsync(actor, async (db, userId) =>
        {
            var generation = Guid.NewGuid();
            var rows = sessions.Select(s => TrainingSuggestion.Create(userId, goalId, generation, s, DateTimeOffset.UtcNow)).ToList();
            db.TrainingSuggestions.AddRange(rows);
            await db.SaveChangesAsync(Ct);
            return rows.Select(r => r.Id).ToList();
        });

    /// <summary>Writes plan metadata on the goal as a finished generation would (model source unless <paramref name="demo"/>).</summary>
    public static async Task SeedPlanAsync(this WebApplicationFactory<Program> factory, TestActor actor, Guid goalId, bool demo = false) =>
        await factory.WithUserDbAsync(actor, async (db, _) =>
        {
            var goal = await db.Goals.SingleAsync(g => g.Id == goalId, Ct);
            goal.RecordPlan(
                new GoalPlanRecord(Guid.NewGuid(), DateTimeOffset.UtcNow, "A short plan.", demo ? null : 1, demo ? RunnersDiary.Domain.Goals.Goal.DemoModelDeployment : "test-deployment", demo ? null : "test-model-1", Today.AddDays(50), false),
                DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
            return 0;
        });

    public static async Task<int> SuggestionRowsAsync(this WebApplicationFactory<Program> factory, Guid userId) =>
        await factory.WithDbAsync(db => db.TrainingSuggestions.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).CountAsync(s => s.UserId == userId, Ct));

    public static async Task<int> GoalRowsAsync(this WebApplicationFactory<Program> factory, Guid userId) =>
        await factory.WithDbAsync(db => db.Goals.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).CountAsync(g => g.UserId == userId, Ct));

    /// <summary>Creates a completed 10K race run (3000 s) on a date in the past, so estimates have a basis.</summary>
    public static async Task<JsonElement> CreateRaceResultAsync(this TestActor actor, int daysAgo = 20, int seconds = 3_000)
    {
        var body = Json.Run(Ahead(-daysAgo), r =>
        {
            r["runTypeId"] = 1;
            r["title"] = null;
            r["distanceMeters"] = 10_000;
            r["durationSeconds"] = seconds;
            r["rpe"] = 9;
            r["race"] = new { name = "Test 10K", distanceCategory = "TenK", resultTimeSeconds = seconds };
        });
        var response = await actor.Client.PostAsJsonAsync(U("/api/runs"), body, Ct);
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync();
    }

    public static async Task<JsonElement> CreatePlannedRaceRunAsync(this TestActor actor, int daysAhead, short runTypeId = 1)
    {
        var body = Json.Run(Ahead(daysAhead), r =>
        {
            r["status"] = "Planned";
            r["runTypeId"] = runTypeId;
            r["durationSeconds"] = null;
            r["rpe"] = null;
            r["race"] = runTypeId == 1 ? new { name = "Gliwice 10K", distanceCategory = "TenK" } : null;
        });
        var response = await actor.Client.PostAsJsonAsync(U("/api/runs"), body, Ct);
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.ReadAsync();
    }
}
