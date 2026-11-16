using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RunnersDiary.Api.Tests.Infrastructure;
using static RunnersDiary.Api.Tests.Goals.GoalTestSupport;

namespace RunnersDiary.Api.Tests.Goals;

/// <summary>The realistic-target estimate (ADR 0039 §4) over real SQL: prediction, warnings, validation, no side effects and per-caller inputs.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GoalEstimateEndpointTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> EstimateAsync(TestActor actor, Action<Dictionary<string, object?>>? change = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = "Distance",
            ["distanceMeters"] = 10_000,
            ["targetDate"] = Ahead(60),
        };
        change?.Invoke(body);
        return await actor.Client.PostAsJsonAsync(U("/api/goals/estimate"), body, Ct);
    }

    [Fact]
    public async Task Without_history_the_estimate_warns_noRecentEffort_and_stores_nothing()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("estimate-empty");

        var response = await EstimateAsync(user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        var estimate = await response.ReadAsync();
        Assert.Equal(JsonValueKind.Null, estimate.GetProperty("prediction").ValueKind);
        Assert.Equal(60, estimate.GetProperty("daysToGoal").GetInt32());
        Assert.Equal(8, estimate.GetProperty("weeksToGoal").GetInt32());
        Assert.Equal("ShortTerm", estimate.GetProperty("term").GetString());
        Assert.Equal(4, estimate.GetProperty("minimumWeeks").GetInt32());
        Assert.Equal(0, estimate.GetProperty("recentWeeklyDistanceMeters").GetDouble());
        var warning = Assert.Single(estimate.GetProperty("warnings").EnumerateArray());
        Assert.Equal("noRecentEffort", warning.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, warning.GetProperty("value").ValueKind);
        Assert.Equal(0, await factory.GoalRowsAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_recent_race_gives_a_prediction_and_the_warnings_follow_the_target()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("estimate-race");
        var race = await user.CreateRaceResultAsync(daysAgo: 20, seconds: 3_000);

        var estimate = await (await EstimateAsync(user, b =>
        {
            b["type"] = "Performance";
            b["distanceMeters"] = 21_097.5;
            b["targetDurationSeconds"] = 6_000; // 9.4 % faster than the predicted 6619 s
            b["targetDate"] = Ahead(50); // 7 weeks, a half marathon needs 8
        })).ReadAsync();

        var prediction = estimate.GetProperty("prediction");
        Assert.Equal(6_619, prediction.GetProperty("durationSeconds").GetInt32());
        Assert.Equal(314, prediction.GetProperty("paceSecondsPerKm").GetInt32());
        Assert.Equal("Low", prediction.GetProperty("confidence").GetString()); // ratio 2.11 is beyond double the distance
        var basis = prediction.GetProperty("basis");
        Assert.Equal(race.GetProperty("id").GetGuid(), basis.GetProperty("runId").GetGuid());
        Assert.Equal("Race", basis.GetProperty("kind").GetString());
        Assert.Equal(3_000, basis.GetProperty("durationSeconds").GetInt32());
        var warnings = estimate.GetProperty("warnings").EnumerateArray().ToDictionary(w => w.GetProperty("code").GetString()!, w => w.GetProperty("value"));
        Assert.Equal(9.4, warnings["targetMuchFasterThanPredicted"].GetDouble());
        Assert.Equal(1, warnings["tooLittleTime"].GetDouble());
        Assert.Contains("lowWeeklyVolume", warnings.Keys);
    }

    [Fact]
    public async Task The_demo_history_of_a_guest_gives_a_prediction_from_a_demo_race()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("estimate-demo");

        var estimate = await (await EstimateAsync(guest, b => b["distanceMeters"] = 21_097.5)).ReadAsync();

        var prediction = estimate.GetProperty("prediction");
        Assert.Equal(JsonValueKind.Object, prediction.ValueKind);
        Assert.True(prediction.GetProperty("durationSeconds").GetInt32() > 0);
        Assert.True(estimate.GetProperty("recentWeeklyDistanceMeters").GetDouble() > 0);
    }

    [Fact]
    public async Task One_callers_history_never_feeds_another_callers_estimate()
    {
        factory.SkipIfDockerUnavailable();
        var runner = factory.EntraUser("estimate-iso-runner");
        var newcomer = factory.EntraUser("estimate-iso-new");
        await runner.CreateRaceResultAsync();

        var theirs = await (await EstimateAsync(runner)).ReadAsync();
        var mine = await (await EstimateAsync(newcomer)).ReadAsync();

        Assert.Equal(JsonValueKind.Object, theirs.GetProperty("prediction").ValueKind);
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("prediction").ValueKind);
    }

    public static TheoryData<string, string, Action<Dictionary<string, object?>>> Invalid() => new()
    {
        { "distance below", "distanceMeters", b => b["distanceMeters"] = 999 },
        { "distance above", "distanceMeters", b => b["distanceMeters"] = 250_001 },
        { "elevation", "elevationGainMeters", b => b["elevationGainMeters"] = 10_001 },
        { "date in the past", "targetDate", b => b["targetDate"] = Ahead(-1) },
        { "date too far", "targetDate", b => b["targetDate"] = Ahead(731) },
        { "pace too fast", "targetDurationSeconds", b => b["targetDurationSeconds"] = 1_490 },
        { "pace too slow", "targetDurationSeconds", b => b["targetDurationSeconds"] = 9_010 },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Invalid_inputs_are_400_with_the_camelCase_path(string reason, string path, Action<Dictionary<string, object?>> change)
    {
        factory.SkipIfDockerUnavailable();
        _ = reason;
        var user = factory.EntraUser("estimate-invalid");

        var problem = await (await EstimateAsync(user, change)).ProblemAsync(HttpStatusCode.BadRequest);

        Assert.True(problem.GetProperty("errors").TryGetProperty(path, out _), problem.ToString());
    }

    [Fact]
    public async Task The_estimate_works_while_a_form_is_incomplete_or_the_goal_is_close()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("estimate-lenient");

        // Performance without a target yet, and a date less than 7 days away: both fine for a hint.
        var response = await EstimateAsync(user, b =>
        {
            b["type"] = "Performance";
            b["targetDate"] = Ahead(3);
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
