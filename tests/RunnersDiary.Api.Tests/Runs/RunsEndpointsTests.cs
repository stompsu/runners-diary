using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Runs;

[Collection(ContainersFixtureGroup.Name)]
public sealed class RunsEndpointsTests(ContainersApiFactory factory)
{
    private static readonly Uri RunsUri = new("/api/runs", UriKind.Relative);

    private static Uri RunUri(Guid id) => new($"/api/runs/{id}", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> CreateAsync(TestActor actor, Dictionary<string, object?> body)
    {
        var response = await actor.Client.PostAsJsonAsync(RunsUri, body, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync();
    }

    [Fact]
    public async Task Create_returns_201_with_location_and_the_stored_shape()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("create");

        var response = await user.Client.PostAsJsonAsync(RunsUri, Json.Run("2026-10-25", r =>
        {
            r["startTime"] = "02:30"; // ambiguous local time on the DST end day in Warsaw -> earlier occurrence
            r["location"] = new { latitude = 50.2945, longitude = 18.6714, name = "Park Chrobrego" };
            r["surface"] = "Road";
        }), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await response.ReadAsync();
        var id = run.GetProperty("id").GetGuid();
        Assert.Equal($"/api/runs/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("2026-10-25", run.GetProperty("date").GetString());
        Assert.Equal("02:30", run.GetProperty("startTime").GetString());
        Assert.Equal("2026-10-25T00:30:00Z", run.GetProperty("startUtc").GetString());
        Assert.Equal("Europe/Warsaw", run.GetProperty("timeZoneId").GetString());
        Assert.Equal("Completed", run.GetProperty("status").GetString());
        Assert.Equal("Road", run.GetProperty("surface").GetString());
        Assert.Equal(10000, run.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(50.2945, run.GetProperty("location").GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(0, run.GetProperty("tracks").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(run.GetProperty("version").GetString()));
        Assert.EndsWith("Z", run.GetProperty("createdAt").GetString(), StringComparison.Ordinal);

        var fetched = await (await user.Client.GetAsync(RunUri(id), Ct)).ReadAsync();
        Assert.Equal(run.GetProperty("version").GetString(), fetched.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Create_without_a_zone_uses_the_profile_zone_and_without_a_time_has_no_startUtc()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("defaultzone"); // new Entra users start in Etc/UTC

        var run = await CreateAsync(user, Json.Run("2026-11-02", r =>
        {
            r.Remove("timeZoneId");
            r.Remove("startTime");
        }));

        Assert.Equal("Etc/UTC", run.GetProperty("timeZoneId").GetString());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("startTime").ValueKind);
        Assert.Equal(JsonValueKind.Null, run.GetProperty("startUtc").ValueKind);
    }

    [Fact]
    public async Task Create_a_planned_race_with_race_details()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("race");

        var run = await CreateAsync(user, Json.Run("2027-04-11", r =>
        {
            r["status"] = "Planned";
            r["runTypeId"] = 1;
            r["durationSeconds"] = null;
            r["rpe"] = null;
            r["race"] = new { name = "Gliwice 10K", distanceCategory = "TenK", bibNumber = "123", targetTimeSeconds = 2700, resultsUrl = "https://example.org/r" };
        }));

        Assert.Equal("Planned", run.GetProperty("status").GetString());
        Assert.Equal("TenK", run.GetProperty("race").GetProperty("distanceCategory").GetString());
        Assert.Equal(2700, run.GetProperty("race").GetProperty("targetTimeSeconds").GetInt32());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("rating").ValueKind);
    }

    [Fact]
    public async Task Update_with_the_current_version_replaces_the_run_and_issues_a_new_version()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("update");
        var created = await CreateAsync(user, Json.Run("2026-11-02"));
        var id = created.GetProperty("id").GetGuid();

        var body = Json.Run("2026-11-03", r =>
        {
            r["title"] = "Changed";
            r["rating"] = new { stars = 4, comment = "Good" };
            r["version"] = created.GetProperty("version").GetString();
        });
        var response = await user.Client.PutAsJsonAsync(RunUri(id), body, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadAsync();
        Assert.Equal("Changed", updated.GetProperty("title").GetString());
        Assert.Equal("2026-11-03", updated.GetProperty("date").GetString());
        Assert.Equal(4, updated.GetProperty("rating").GetProperty("stars").GetInt32());
        Assert.NotEqual(created.GetProperty("version").GetString(), updated.GetProperty("version").GetString());
        Assert.Equal(created.GetProperty("createdAt").GetString(), updated.GetProperty("createdAt").GetString());
    }

    [Fact]
    public async Task Update_with_a_stale_version_returns_409_with_the_concurrency_problem_type()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("stale");
        var created = await CreateAsync(user, Json.Run("2026-11-02"));
        var id = created.GetProperty("id").GetGuid();
        var staleVersion = created.GetProperty("version").GetString();
        var first = await user.Client.PutAsJsonAsync(RunUri(id), Json.Run("2026-11-02", r => { r["title"] = "A"; r["version"] = staleVersion; }), Ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await user.Client.PutAsJsonAsync(RunUri(id), Json.Run("2026-11-02", r => { r["title"] = "B"; r["version"] = staleVersion; }), Ct);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var problem = await second.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:concurrency-conflict", problem.GetProperty("type").GetString());
        var current = await (await user.Client.GetAsync(RunUri(id), Ct)).ReadAsync();
        Assert.Equal("A", current.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Update_without_a_valid_version_returns_400()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("noversion");
        var created = await CreateAsync(user, Json.Run("2026-11-02"));
        var id = created.GetProperty("id").GetGuid();

        var missing = await user.Client.PutAsJsonAsync(RunUri(id), Json.Run("2026-11-02"), Ct);
        var garbage = await user.Client.PutAsJsonAsync(RunUri(id), Json.Run("2026-11-02", r => r["version"] = "***"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        Assert.True((await missing.ReadAsync()).GetProperty("errors").TryGetProperty("version", out _));
    }

    [Fact]
    public async Task Status_can_move_between_planned_and_completed_via_put()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("status");
        var created = await CreateAsync(user, Json.Run("2026-11-02", r => { r["status"] = "Planned"; r["rpe"] = null; }));
        var id = created.GetProperty("id").GetGuid();

        var completed = await user.Client.PutAsJsonAsync(RunUri(id), Json.Run("2026-11-02", r =>
        {
            r["status"] = "Completed";
            r["rating"] = new { stars = 5 };
            r["version"] = created.GetProperty("version").GetString();
        }), Ct);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var done = await completed.ReadAsync();

        var back = await user.Client.PutAsJsonAsync(RunUri(id), Json.Run("2026-11-02", r =>
        {
            r["status"] = "Planned";
            r["rating"] = null;
            r["version"] = done.GetProperty("version").GetString();
        }), Ct);
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    }

    [Fact]
    public async Task Delete_returns_204_then_404_and_the_run_is_gone_from_lists()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("delete");
        var created = await CreateAsync(user, Json.Run("2026-11-02"));
        var id = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync(RunUri(id), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.DeleteAsync(RunUri(id), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync(RunUri(id), Ct)).StatusCode);
        var list = await (await user.Client.GetAsync(new Uri("/api/runs?from=2026-11-01&to=2026-11-30", UriKind.Relative), Ct)).ReadAsync();
        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Get_and_put_of_an_unknown_id_return_404_problem()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("unknown");
        var id = Guid.NewGuid();

        var get = await user.Client.GetAsync(RunUri(id), Ct);
        var put = await user.Client.PutAsJsonAsync(RunUri(id), Json.Run("2026-11-02", r => r["version"] = "AAAAAAAAB9E="), Ct);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal("application/problem+json", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task List_returns_the_range_ordered_by_date_then_start_time_with_nulls_last()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("list");
        await CreateAsync(user, Json.Run("2026-11-10", r => { r["title"] = "c-no-time-day10"; r.Remove("startTime"); }));
        await CreateAsync(user, Json.Run("2026-11-10", r => { r["title"] = "b-evening"; r["startTime"] = "18:00"; }));
        await CreateAsync(user, Json.Run("2026-11-10", r => { r["title"] = "a-morning"; r["startTime"] = "06:00"; }));
        await CreateAsync(user, Json.Run("2026-11-09", r => r["title"] = "day9"));
        await CreateAsync(user, Json.Run("2026-12-31", r => r["title"] = "outside"));

        var list = await (await user.Client.GetAsync(new Uri("/api/runs?from=2026-11-01&to=2026-11-30", UriKind.Relative), Ct)).ReadAsync();

        Assert.Equal("2026-11-01", list.GetProperty("from").GetString());
        Assert.Equal("2026-11-30", list.GetProperty("to").GetString());
        Assert.Equal(
            ["day9", "a-morning", "b-evening", "c-no-time-day10"],
            list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToArray());
    }

    [Theory]
    [InlineData("from=2026-11-10&to=2026-11-09", "to")]
    [InlineData("from=2026-01-01&to=2026-04-03", "to")] // 93 days inclusive
    public async Task List_with_an_invalid_range_returns_400(string query, string key)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("range");

        var response = await user.Client.GetAsync(new Uri("/api/runs?" + query, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.ReadAsync()).GetProperty("errors").TryGetProperty(key, out _));
    }

    [Fact]
    public async Task List_accepts_exactly_92_days_and_rejects_missing_or_malformed_dates()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("range92");

        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(new Uri("/api/runs?from=2026-01-01&to=2026-04-02", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.GetAsync(new Uri("/api/runs?from=2026-01-01", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.GetAsync(new Uri("/api/runs?from=yesterday&to=today", UriKind.Relative), Ct)).StatusCode);
    }

    public static TheoryData<string, Action<Dictionary<string, object?>>, string> InvalidBodies() => new()
    {
        { "rating on a planned run", r => { r["status"] = "Planned"; r["rating"] = new { stars = 3 }; }, "rating" },
        { "race on a non-race type", r => r["race"] = new { name = "X", distanceCategory = "TenK" }, "race" },
        { "result time on a planned race", r => { r["status"] = "Planned"; r["runTypeId"] = 1; r["race"] = new { name = "X", distanceCategory = "TenK", resultTimeSeconds = 100 }; }, "race.resultTimeSeconds" },
        { "max heart rate below average", r => { r["avgHeartRate"] = 160; r["maxHeartRate"] = 150; }, "maxHeartRate" },
        { "Windows time zone id", r => r["timeZoneId"] = "Central European Standard Time", "timeZoneId" },
        { "unknown run type", r => r["runTypeId"] = 999, "runTypeId" },
        { "negative distance", r => r["distanceMeters"] = -5, "distanceMeters" },
        { "rest day with metrics", r => r["runTypeId"] = 41, "distanceMeters" },
        { "date out of range", r => r["date"] = "1900-01-01", "date" },
        { "rpe out of range", r => r["rpe"] = 11, "rpe" },
        { "stars out of range", r => r["rating"] = new { stars = 6 }, "rating.stars" },
        { "latitude out of range", r => r["location"] = new { latitude = 91, longitude = 10 }, "location.latitude" },
        { "title too long", r => r["title"] = new string('x', 121), "title" },
        { "relative results url", r => { r["runTypeId"] = 1; r["race"] = new { name = "X", distanceCategory = "TenK", resultsUrl = "javascript:alert(1)" }; }, "race.resultsUrl" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_with_domain_rule_violations_returns_400_keyed_by_json_path(string scenario, Action<Dictionary<string, object?>> change, string errorKey)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("invalid");

        var response = await user.Client.PostAsJsonAsync(RunsUri, Json.Run("2026-11-02", change), Ct);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, scenario + ": " + await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync();
        Assert.True(problem.GetProperty("errors").TryGetProperty(errorKey, out _), scenario + ": " + problem);
    }

    [Theory]
    [InlineData("""{"date":"2026-11-02","status":"Completed","runTypeId":20,"userId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""")]
    [InlineData("""{"date":"2026-11-02","status":"Completed","runTypeId":20,"id":"3f2504e0-4f89-11d3-9a0c-0305e82c3301"}""")]
    [InlineData("""{"date":"2026-11-02","status":"Completed","runTypeId":20,"startTime":"07:30:15"}""")]
    [InlineData("""{"date":"2026-11-02","status":"Completed","runTypeId":20,"startTime":"7:30"}""")]
    [InlineData("""{"date":"2026-11-02","status":5,"runTypeId":20}""")]
    [InlineData("""{"date":"2026-11-02","status":"Finished","runTypeId":20}""")]
    [InlineData("""{"date":"2026-11-02","status":"Completed","runTypeId":"20"}""")]
    [InlineData("""{"status":"Completed","runTypeId":20}""")]
    [InlineData("""{"date":"2026-11-02","status":"Completed","runTypeId":20""")]
    [InlineData("")]
    public async Task Create_with_unknown_members_or_malformed_json_returns_400_problem_and_stores_nothing(string body)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("shape");
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await user.Client.PostAsync(RunsUri, content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var list = await (await user.Client.GetAsync(new Uri("/api/runs?from=2026-11-01&to=2026-11-30", UriKind.Relative), Ct)).ReadAsync();
        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Wrong_content_type_is_not_a_server_error()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("ctype");
        using var content = new StringContent("date=2026-11-02", Encoding.UTF8, "text/plain");

        var response = await user.Client.PostAsync(RunsUri, content, Ct);

        Assert.True((int)response.StatusCode is >= 400 and < 500, response.StatusCode.ToString());
    }

    [Fact]
    public async Task Start_time_round_trips_as_HH_mm()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("hhmm");
        var run = await CreateAsync(user, Json.Run("2026-11-02", r => r["startTime"] = "05:05"));
        var raw = await (await user.Client.GetAsync(RunUri(run.GetProperty("id").GetGuid()), Ct)).Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"startTime\":\"05:05\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Spring_forward_gap_start_time_is_shifted_by_the_gap()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("dstgap");
        var run = await CreateAsync(user, Json.Run("2026-03-29", r => r["startTime"] = "02:30"));
        Assert.Equal("2026-03-29T01:30:00Z", run.GetProperty("startUtc").GetString());
    }
}
