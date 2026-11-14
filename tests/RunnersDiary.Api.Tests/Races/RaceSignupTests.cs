using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Api.Tests.Races;

/// <summary>AC8 (server side): "I signed up" creates a planned race run, one per race, honouring the run caps; the run keeps its link.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class RaceSignupTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly string[] WritableRunMembers =
    [
        "date", "startTime", "timeZoneId", "status", "runTypeId", "title", "notes", "location", "distanceMeters", "durationSeconds",
        "elevationGainMeters", "elevationLossMeters", "avgHeartRate", "maxHeartRate", "rpe", "gear", "weather", "surface", "race", "rating", "version",
    ];

    private static async Task<(Race Race, string Token)> SeedAsync(
        ContainersApiFactory f, int daysAhead = 20, RaceKind kind = RaceKind.Road, double[]? distances = null)
    {
        var token = RaceTestSupport.NewToken();
        return (await f.SeedAsync(RaceTestSupport.Facts(token, daysAhead, "Race", kind, distances)), token);
    }

    [Fact]
    public async Task Signing_up_creates_a_planned_race_run_with_every_mapped_field()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-mapped");
        var token = RaceTestSupport.NewToken();
        var race = await factory.SeedAsync(RaceTestSupport.Facts(token, 20, "Trail", RaceKind.Trail, [21_097.5, 10_000, 5_000]));

        var response = await user.SignUpAsync(race.Id, new { distanceMeters = 21_097.5, startTime = "08:15", targetTimeSeconds = 6_000 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await response.ReadAsync();
        var id = run.GetProperty("id").GetGuid();
        Assert.Equal($"/api/runs/{id}", response.Headers.Location?.ToString());
        Assert.Equal("Planned", run.GetProperty("status").GetString());
        Assert.Equal(Iso(RaceTestSupport.Today.AddDays(20)), run.GetProperty("date").GetString());
        Assert.Equal("08:15", run.GetProperty("startTime").GetString());
        Assert.Equal("Europe/Warsaw", run.GetProperty("timeZoneId").GetString());
        Assert.Equal(2, run.GetProperty("runTypeId").GetInt32());
        Assert.Equal(21_097.5, run.GetProperty("distanceMeters").GetDouble());
        Assert.Equal("Trail", run.GetProperty("surface").GetString());
        Assert.Equal(120, run.GetProperty("elevationGainMeters").GetDouble());
        Assert.Equal(JsonValueKind.Null, run.GetProperty("title").ValueKind);
        Assert.Equal(JsonValueKind.Null, run.GetProperty("notes").ValueKind);
        Assert.Equal(JsonValueKind.Null, run.GetProperty("durationSeconds").ValueKind);
        Assert.Equal(race.Id, run.GetProperty("catalogRaceId").GetGuid());
        var details = run.GetProperty("race");
        Assert.Equal($"{token} Trail", details.GetProperty("name").GetString());
        Assert.Equal("HalfMarathon", details.GetProperty("distanceCategory").GetString());
        Assert.Equal(21_097.5, details.GetProperty("officialDistanceMeters").GetDouble());
        Assert.Equal(6_000, details.GetProperty("targetTimeSeconds").GetInt32());
        var location = run.GetProperty("location");
        Assert.Equal(50.2945, location.GetProperty("latitude").GetDouble());
        Assert.Equal(18.6714, location.GetProperty("longitude").GetDouble());
        Assert.Equal("Gliwice", location.GetProperty("name").GetString());

        // The run is an ordinary run of the caller: readable, listed and in the month grid.
        var date = RaceTestSupport.Today.AddDays(20);
        var read = await (await user.Client.GetAsync(U($"/api/runs/{id}"), Ct)).ReadAsync();
        Assert.Equal(race.Id, read.GetProperty("catalogRaceId").GetGuid());
        var list = await (await user.Client.GetAsync(U($"/api/runs?from={Iso(date)}&to={Iso(date)}"), Ct)).ReadAsync();
        Assert.Equal([id], list.GetProperty("items").Ids().ToArray());
        var month = await (await user.Client.GetAsync(U($"/api/calendar/months/{date.Year}/{date.Month}"), Ct)).ReadAsync();
        Assert.Contains(id, month.GetProperty("runs").Ids());
    }

    [Theory]
    [InlineData(RaceKind.Road, 1)]
    [InlineData(RaceKind.Other, 1)]
    [InlineData(RaceKind.Trail, 2)]
    [InlineData(RaceKind.Mountain, 2)]
    [InlineData(RaceKind.Ocr, 2)]
    [InlineData(RaceKind.CrossCountry, 5)]
    [InlineData(RaceKind.Ultra, 3)]
    public async Task The_run_type_follows_the_race_kind(RaceKind kind, int expectedRunTypeId)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-kind");
        var (race, _) = await SeedAsync(factory, kind: kind);

        var run = await (await user.SignUpAsync(race.Id)).ReadAsync();

        Assert.Equal(expectedRunTypeId, run.GetProperty("runTypeId").GetInt32());
    }

    [Fact]
    public async Task An_empty_body_is_fine_for_a_race_with_one_distance_and_the_distance_and_time_default_from_the_race()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-emptybody");
        var (race, _) = await SeedAsync(factory, distances: [10_000]);

        var response = await user.Client.PostAsync(U($"/api/races/{race.Id}/signup"), content: null, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await response.ReadAsync();
        Assert.Equal(10_000, run.GetProperty("distanceMeters").GetDouble());
        Assert.Equal("09:00", run.GetProperty("startTime").GetString());
        Assert.Equal("TenK", run.GetProperty("race").GetProperty("distanceCategory").GetString());
    }

    [Fact]
    public async Task A_race_without_listed_distances_gives_a_run_without_distance()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-nodistance");
        var (race, _) = await SeedAsync(factory, distances: []);

        var rejected = await user.SignUpAsync(race.Id, new { distanceMeters = 10_000 });
        var created = await user.SignUpAsync(race.Id);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("distanceMeters", (await rejected.ReadAsync()).GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await created.ReadAsync()).GetProperty("distanceMeters").ValueKind);
    }

    [Fact]
    public async Task With_several_distances_the_choice_is_required_and_must_be_one_of_them()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-distances");
        var (race, _) = await SeedAsync(factory, distances: [21_097.5, 10_000]);

        var missing = await user.SignUpAsync(race.Id);
        var wrong = await user.SignUpAsync(race.Id, new { distanceMeters = 12_345 });
        var ok = await user.SignUpAsync(race.Id, new { distanceMeters = 10_000 });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("distanceMeters", (await missing.ReadAsync()).GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("distanceMeters", (await wrong.ReadAsync()).GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(10_000, (await ok.ReadAsync()).GetProperty("distanceMeters").GetDouble());
    }

    [Fact]
    public async Task A_race_run_type_may_be_chosen_but_other_types_are_400()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-runtype");
        var (race, _) = await SeedAsync(factory);

        var easy = await user.SignUpAsync(race.Id, new { runTypeId = 20 });
        var unknown = await user.SignUpAsync(race.Id, new { runTypeId = 999 });
        var ultra = await user.SignUpAsync(race.Id, new { runTypeId = 3 });

        Assert.Equal(HttpStatusCode.BadRequest, easy.StatusCode);
        Assert.Contains("runTypeId", (await easy.ReadAsync()).GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ultra.StatusCode);
        Assert.Equal(3, (await ultra.ReadAsync()).GetProperty("runTypeId").GetInt32());
    }

    [Theory]
    [InlineData("{\"startTime\":\"25:00\"}", "startTime")]
    [InlineData("{\"startTime\":\"abc\"}", "startTime")]
    [InlineData("{\"targetTimeSeconds\":0}", "targetTimeSeconds")]
    [InlineData("{\"targetTimeSeconds\":604801}", "targetTimeSeconds")]
    [InlineData("{\"userId\":\"3f2504e0-4f89-41d3-9a0c-0305e82c3301\"}", "userId")]
    [InlineData("{\"distanceMeters\":\"far\"}", "distanceMeters")]
    public async Task Invalid_signup_input_is_400_on_the_offending_member(string json, string key)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-invalid");
        var (race, _) = await SeedAsync(factory);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await user.Client.PostAsync(U($"/api/races/{race.Id}/signup"), content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(key, (await response.ReadAsync()).GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(0, await factory.RunCountAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task The_edges_of_the_target_time_are_accepted()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-target-edges");
        var (first, _) = await SeedAsync(factory);
        var (second, _) = await SeedAsync(factory, 21);

        var low = await user.SignUpAsync(first.Id, new { targetTimeSeconds = 1 });
        var high = await user.SignUpAsync(second.Id, new { targetTimeSeconds = 604_800 });

        Assert.Equal(HttpStatusCode.Created, low.StatusCode);
        Assert.Equal(HttpStatusCode.Created, high.StatusCode);
    }

    [Fact]
    public async Task A_race_that_already_took_place_is_400_on_raceId()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-past");
        var (race, _) = await SeedAsync(factory, daysAhead: -5);

        var response = await user.SignUpAsync(race.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("raceId", (await response.ReadAsync()).GetProperty("errors").EnumerateObject().Select(e => e.Name));
    }

    [Fact]
    public async Task Unknown_removed_and_duplicate_races_are_404()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-404");
        var token = RaceTestSupport.NewToken();
        var removed = await factory.SeedAsync(RaceTestSupport.Facts(token, 20, "Removed"), r => r.Remove(DateTimeOffset.UtcNow));
        var canonical = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "Canonical"));
        var duplicate = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "Duplicate"), r => r.SetDuplicateOf(canonical.Id));

        foreach (var id in new[] { Guid.NewGuid(), removed.Id, duplicate.Id })
        {
            var response = await user.SignUpAsync(id);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        Assert.Equal(0, await factory.RunCountAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_second_signup_is_409_with_the_existing_run_id_and_creates_nothing()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-twice");
        var (race, _) = await SeedAsync(factory);
        var first = await (await user.SignUpAsync(race.Id)).ReadAsync();

        var second = await user.SignUpAsync(race.Id);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:race-already-linked", problem.GetProperty("type").GetString());
        Assert.Equal(first.GetProperty("id").GetGuid(), problem.GetProperty("runId").GetGuid());
        Assert.Equal(1, await factory.RunCountAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_concurrent_double_signup_creates_exactly_one_run()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-concurrent");
        var (race, _) = await SeedAsync(factory);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => user.SignUpAsync(race.Id)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await factory.RunCountAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task The_run_cap_per_date_is_409_run_limit_and_nothing_is_created()
    {
        factory.SkipIfDockerUnavailable();
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("RunLimits:MaxRunsPerDate", "1"));
        var user = host.EntraUser("signup-cap-date");
        var (race, _) = await SeedAsync(factory);
        var date = Iso(RaceTestSupport.Today.AddDays(20));
        Assert.Equal(HttpStatusCode.Created, (await user.Client.PostAsJsonAsync(U("/api/runs"), Json.Run(date), Ct)).StatusCode);

        var response = await user.SignUpAsync(race.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:run-limit", (await response.ReadAsync()).GetProperty("type").GetString());
        Assert.Equal(1, await host.RunCountAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task The_total_run_cap_applies_to_signups()
    {
        factory.SkipIfDockerUnavailable();
        // The guest cap cannot go below the demo dataset size (startup check), so the total cap is exercised with the user cap.
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("RunLimits:UserMaxRuns", "1"));
        var guest = host.EntraUser("signup-cap-total");
        var (first, _) = await SeedAsync(factory, 20);
        var (second, _) = await SeedAsync(factory, 21);

        Assert.Equal(HttpStatusCode.Created, (await guest.SignUpAsync(first.Id)).StatusCode);
        var response = await guest.SignUpAsync(second.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:run-limit", (await response.ReadAsync()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_signed_up_race_shows_as_signed_up_even_beyond_the_horizon_and_after_the_interests_changed()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-state");
        var token = RaceTestSupport.NewToken();
        var race = await factory.SeedAsync(RaceTestSupport.Facts(token, 25));
        await user.ConfigureAsync(token, b => b["horizonDays"] = 7);
        Assert.False((await user.ProposalsAsync()).Contains(race.Id));
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(race.Id, "Dismissed")).StatusCode);

        var run = await (await user.SignUpAsync(race.Id)).ReadAsync();
        await user.ConfigureAsync(token, b => { b["horizonDays"] = 7; b["kinds"] = new List<string> { "Trail" }; });

        var item = (await user.ProposalsAsync()).ItemFor(race.Id);
        Assert.Equal("SignedUp", item.GetProperty("state").GetString());
        Assert.Equal(run.GetProperty("id").GetGuid(), item.GetProperty("linkedRunId").GetGuid());
        Assert.False(item.GetProperty("dateChanged").GetBoolean());
        var details = await (await user.GetRaceAsync(race.Id)).ReadAsync();
        Assert.Equal("SignedUp", details.GetProperty("state").GetString());
        Assert.Equal(run.GetProperty("id").GetGuid(), details.GetProperty("linkedRunId").GetGuid());
        var state = await (await user.SetStateAsync(race.Id, "Interested")).ReadAsync();
        Assert.Equal("SignedUp", state.GetProperty("state").GetString());
        Assert.Equal(run.GetProperty("id").GetGuid(), state.GetProperty("linkedRunId").GetGuid());
    }

    [Fact]
    public async Task Updating_the_run_keeps_the_link_and_the_link_cannot_be_written()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-update");
        var (race, _) = await SeedAsync(factory);
        var run = await (await user.SignUpAsync(race.Id)).ReadAsync();
        var id = run.GetProperty("id").GetGuid();
        var body = run.EnumerateObject().Where(p => WritableRunMembers.Contains(p.Name)).ToDictionary(p => p.Name, p => p.Value);
        body["title"] = JsonSerializer.SerializeToElement("My own title");

        var put = await user.Client.PutAsJsonAsync(U($"/api/runs/{id}"), body, Ct);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = await put.ReadAsync();
        Assert.Equal("My own title", updated.GetProperty("title").GetString());
        Assert.Equal(race.Id, updated.GetProperty("catalogRaceId").GetGuid());
        Assert.Equal(race.Id, (await (await user.Client.GetAsync(U($"/api/runs/{id}"), Ct)).ReadAsync()).GetProperty("catalogRaceId").GetGuid());

        body["version"] = updated.GetProperty("version");
        body["catalogRaceId"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PutAsJsonAsync(U($"/api/runs/{id}"), body, Ct)).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_linked_run_makes_the_proposal_none_again_and_allows_a_new_signup()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-delete");
        var token = RaceTestSupport.NewToken();
        var race = await factory.SeedAsync(RaceTestSupport.Facts(token, 20));
        await user.ConfigureAsync(token);
        var run = await (await user.SignUpAsync(race.Id)).ReadAsync();
        var id = run.GetProperty("id").GetGuid();
        Assert.Equal("SignedUp", (await user.ProposalsAsync()).ItemFor(race.Id).GetProperty("state").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync(U($"/api/runs/{id}"), Ct)).StatusCode);

        var item = (await user.ProposalsAsync()).ItemFor(race.Id);
        Assert.Equal("None", item.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("linkedRunId").ValueKind);
        var details = await (await user.GetRaceAsync(race.Id)).ReadAsync();
        Assert.Equal("None", details.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Created, (await user.SignUpAsync(race.Id)).StatusCode);
    }

    [Fact]
    public async Task A_moved_race_date_flags_the_proposal_and_leaves_the_run_untouched()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-datechange");
        var token = RaceTestSupport.NewToken();
        var race = await factory.SeedAsync(RaceTestSupport.Facts(token, 20));
        await user.ConfigureAsync(token);
        var run = await (await user.SignUpAsync(race.Id)).ReadAsync();
        var id = run.GetProperty("id").GetGuid();
        var originalDate = Iso(RaceTestSupport.Today.AddDays(20));

        await factory.MutateRaceAsync(race.Id, r => r.ChangeDate(r.Date.AddDays(7), DateTimeOffset.UtcNow));

        var item = (await user.ProposalsAsync()).ItemFor(race.Id);
        Assert.Equal("SignedUp", item.GetProperty("state").GetString());
        Assert.Equal(Iso(RaceTestSupport.Today.AddDays(27)), item.GetProperty("date").GetString());
        Assert.True(item.GetProperty("dateChanged").GetBoolean());
        Assert.True((await (await user.GetRaceAsync(race.Id)).ReadAsync()).GetProperty("dateChanged").GetBoolean());
        Assert.Equal(originalDate, (await (await user.Client.GetAsync(U($"/api/runs/{id}"), Ct)).ReadAsync()).GetProperty("date").GetString());
        // Moving the run onto the new date by the ordinary update (the "Move my run" action) clears the flag.
        var read = await (await user.Client.GetAsync(U($"/api/runs/{id}"), Ct)).ReadAsync();
        var body = read.EnumerateObject().Where(p => WritableRunMembers.Contains(p.Name)).ToDictionary(p => p.Name, p => p.Value);
        body["date"] = JsonSerializer.SerializeToElement(Iso(RaceTestSupport.Today.AddDays(27)));
        Assert.Equal(HttpStatusCode.OK, (await user.Client.PutAsJsonAsync(U($"/api/runs/{id}"), body, Ct)).StatusCode);
        Assert.False((await user.ProposalsAsync()).ItemFor(race.Id).GetProperty("dateChanged").GetBoolean());
    }

    [Fact]
    public async Task A_purged_race_leaves_the_run_with_a_null_link()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("signup-purged");
        var (race, _) = await SeedAsync(factory);
        var id = (await (await user.SignUpAsync(race.Id)).ReadAsync()).GetProperty("id").GetGuid();

        await factory.WithDbAsync(db => db.Races.Where(r => r.Id == race.Id).ExecuteDeleteAsync(Ct));

        var run = await (await user.Client.GetAsync(U($"/api/runs/{id}"), Ct)).ReadAsync();
        Assert.Equal(JsonValueKind.Null, run.GetProperty("catalogRaceId").ValueKind);
        Assert.Equal("Planned", run.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetRaceAsync(race.Id)).StatusCode);
    }

    [Fact]
    public async Task A_guest_can_sign_up_and_the_run_belongs_to_the_guest()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("signup-guest");
        var (race, _) = await SeedAsync(factory);

        var response = await guest.SignUpAsync(race.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await factory.RunCountAsync(guest.UserId!.Value));
        var owner = await factory.WithDbAsync(db => db.Runs.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).AsNoTracking()
            .Where(r => r.CatalogRaceId == race.Id).Select(r => r.UserId).SingleAsync(Ct));
        Assert.Equal(guest.UserId, owner);
    }
}
