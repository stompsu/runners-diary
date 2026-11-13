using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Api.Tests.Races;

/// <summary>M8 endpoints (AC3, AC5): interests, proposals, details, state and the seen marker, against real SQL Server.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class RaceEndpointTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> TodayOfAsync(TestActor actor) =>
        (await (await actor.Client.GetAsync(U("/api/me"), Ct)).ReadAsync()).GetProperty("today");

    // ---------- interests ----------

    [Fact]
    public async Task Without_saved_interests_the_defaults_are_returned_unconfigured_and_never_cached()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("interests-default");

        var response = await user.Client.GetAsync(U("/api/races/interests"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        var body = await response.ReadAsync();
        Assert.False(body.GetProperty("configured").GetBoolean());
        Assert.Equal(["Road", "Trail"], body.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()).ToArray());
        Assert.Equal(0, body.GetProperty("regionCodes").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("center").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("radiusMeters").ValueKind);
        Assert.Equal(5_000, body.GetProperty("minDistanceMeters").GetDouble());
        Assert.Equal(42_195, body.GetProperty("maxDistanceMeters").GetDouble());
        Assert.Equal(120, body.GetProperty("horizonDays").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("keywords").ValueKind);
        Assert.True(body.GetProperty("showInCalendar").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Saving_interests_round_trips_and_a_second_save_replaces_the_first()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("interests-roundtrip");

        var put = await user.PutInterestsAsync(new
        {
            kinds = new List<string> { "Trail", "Ultra" },
            regionCodes = new List<string> { "PL-24", "PL-12" },
            center = new { latitude = 50.2945, longitude = 18.6714, name = "Gliwice" },
            radiusMeters = 60_000,
            minDistanceMeters = 5_000,
            maxDistanceMeters = 50_000,
            horizonDays = 90,
            keywords = "  gliwice   night ",
            showInCalendar = false,
        });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = await put.ReadAsync();
        var get = await (await user.Client.GetAsync(U("/api/races/interests"), Ct)).ReadAsync();
        Assert.Equal(saved.ToString(), get.ToString());
        Assert.True(get.GetProperty("configured").GetBoolean());
        Assert.Equal(["Trail", "Ultra"], get.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()).ToArray());
        Assert.Equal(["PL-12", "PL-24"], get.GetProperty("regionCodes").EnumerateArray().Select(k => k.GetString()).ToArray());
        Assert.Equal("Gliwice", get.GetProperty("center").GetProperty("name").GetString());
        Assert.Equal(50.2945, get.GetProperty("center").GetProperty("latitude").GetDouble());
        Assert.Equal(60_000, get.GetProperty("radiusMeters").GetInt32());
        Assert.Equal(90, get.GetProperty("horizonDays").GetInt32());
        Assert.Equal("gliwice night", get.GetProperty("keywords").GetString());
        Assert.False(get.GetProperty("showInCalendar").GetBoolean());
        Assert.Equal(JsonValueKind.String, get.GetProperty("updatedAt").ValueKind);

        // Full replace: the omitted/null members are cleared, not kept.
        Assert.Equal(HttpStatusCode.OK, (await user.PutInterestsAsync(RaceTestSupport.Interests(null, b => b["kinds"] = new List<string> { "Road" }))).StatusCode);
        var replaced = await (await user.Client.GetAsync(U("/api/races/interests"), Ct)).ReadAsync();
        Assert.Equal(["Road"], replaced.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()).ToArray());
        Assert.Equal(0, replaced.GetProperty("regionCodes").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, replaced.GetProperty("center").ValueKind);
        Assert.Equal(JsonValueKind.Null, replaced.GetProperty("keywords").ValueKind);
    }

    private static readonly Dictionary<string, (Action<Dictionary<string, object?>> Change, string Key)> InvalidInterests = new()
    {
        ["kinds empty"] = (b => b["kinds"] = Array.Empty<string>(), "kinds"),
        ["kind unknown"] = (b => b["kinds"] = new[] { "Foo" }, "kinds"),
        ["kind as number"] = (b => b["kinds"] = new[] { 1 }, "kinds"),
        ["17 regions"] = (b => b["regionCodes"] = Enumerable.Range(0, 17).Select(i => i < 16 ? $"PL-{(i * 2) + 2:00}" : "PL-02").ToArray(), "regionCodes"),
        ["region unknown"] = (b => b["regionCodes"] = new[] { "PL-99" }, "regionCodes"),
        ["region duplicate"] = (b => b["regionCodes"] = new[] { "PL-24", "PL-24" }, "regionCodes"),
        ["center without radius"] = (b => b["center"] = new { latitude = 50.0, longitude = 18.0 }, "radiusMeters"),
        ["radius without center"] = (b => b["radiusMeters"] = 10_000, "center"),
        ["radius 4999"] = (b => { b["center"] = new { latitude = 50.0, longitude = 18.0 }; b["radiusMeters"] = 4_999; }, "radiusMeters"),
        ["radius 300001"] = (b => { b["center"] = new { latitude = 50.0, longitude = 18.0 }; b["radiusMeters"] = 300_001; }, "radiusMeters"),
        ["latitude 91"] = (b => { b["center"] = new { latitude = 91.0, longitude = 18.0 }; b["radiusMeters"] = 10_000; }, "center"),
        ["center name 201"] = (b => { b["center"] = new { latitude = 50.0, longitude = 18.0, name = new string('x', 201) }; b["radiusMeters"] = 10_000; }, "center.name"),
        ["min greater than max"] = (b => { b["minDistanceMeters"] = 20_000; b["maxDistanceMeters"] = 10_000; }, "maxDistanceMeters"),
        ["min 999"] = (b => b["minDistanceMeters"] = 999, "minDistanceMeters"),
        ["max 250001"] = (b => b["maxDistanceMeters"] = 250_001, "maxDistanceMeters"),
        ["horizon 6"] = (b => b["horizonDays"] = 6, "horizonDays"),
        ["horizon 366"] = (b => b["horizonDays"] = 366, "horizonDays"),
        ["6 keyword terms"] = (b => b["keywords"] = "aa bb cc dd ee ff", "keywords"),
        ["1 character term"] = (b => b["keywords"] = "a", "keywords"),
        ["31 character term"] = (b => b["keywords"] = new string('a', 31), "keywords"),
        ["keywords over 100"] = (b => b["keywords"] = string.Join(' ', Enumerable.Repeat(new string('a', 30), 4)), "keywords"),
        ["unknown member"] = (b => b["userId"] = Guid.NewGuid(), "userId"),
        // A missing required member is a body-level binding error (ADR 0012), like in the run endpoints.
        ["missing kinds"] = (b => b.Remove("kinds"), "body"),
    };

    public static TheoryData<string> InvalidInterestNames() => [.. InvalidInterests.Keys];

    [Theory]
    [MemberData(nameof(InvalidInterestNames))]
    public async Task Invalid_interests_are_400_with_camelCase_error_keys(string name)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("interests-invalid");
        var (change, key) = InvalidInterests[name];

        var response = await user.PutInterestsAsync(RaceTestSupport.Interests("ok", change));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var errors = (await response.ReadAsync()).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => e.Name.StartsWith(key, StringComparison.Ordinal));
        // Nothing was stored.
        Assert.False((await (await user.Client.GetAsync(U("/api/races/interests"), Ct)).ReadAsync()).GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task Boundary_values_of_the_interest_rules_are_accepted()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("interests-boundaries");

        var response = await user.PutInterestsAsync(new
        {
            kinds = new List<string> { "Other" },
            regionCodes = Array.Empty<string>(),
            center = new { latitude = -90.0, longitude = 180.0, name = (string?)null },
            radiusMeters = 5_000,
            minDistanceMeters = 1_000,
            maxDistanceMeters = 250_000,
            horizonDays = 7,
            keywords = "ab " + new string('c', 30),
            showInCalendar = true,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.PutInterestsAsync(RaceTestSupport.Interests(null, b =>
        {
            b["horizonDays"] = 365;
            b["radiusMeters"] = 300_000;
            b["center"] = new { latitude = 90.0, longitude = -180.0 };
        }))).StatusCode);
    }

    // ---------- proposals ----------

    private static readonly (string Query, string Key)[] InvalidWindows =
    [
        ("", "from"),
        ("from=2026-11-01", "to"),
        ("from=2026-11-10&to=2026-11-09", "to"),
        ("from=1949-12-31&to=2026-01-01", "from"),
        ("from=2026-01-01&to=2101-01-01", "to"),
        ("from=2026-01-01&to=2026-04-03", "to"),
        ("from=not-a-date&to=2026-04-03", "from"),
        ("from=2026-11-01&to=2026-11-30&includeDismissed=maybe", "includeDismissed"),
    ];

    public static TheoryData<int> InvalidWindowIndexes() => [.. Enumerable.Range(0, InvalidWindows.Length)];

    [Theory]
    [MemberData(nameof(InvalidWindowIndexes))]
    public async Task An_invalid_proposal_window_is_400_on_the_offending_parameter(int index)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("window-invalid");
        var (query, key) = InvalidWindows[index];

        var response = await user.Client.GetAsync(U("/api/races/proposals?" + query), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, (await response.ReadAsync()).GetProperty("errors").EnumerateObject().Select(e => e.Name));
    }

    [Fact]
    public async Task The_window_may_span_exactly_92_days_but_not_93()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("window-span");
        var from = new DateOnly(2026, 11, 1);

        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(U($"/api/races/proposals?from={Iso(from)}&to={Iso(from.AddDays(91))}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.GetAsync(U($"/api/races/proposals?from={Iso(from)}&to={Iso(from.AddDays(92))}"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Without_interests_the_list_is_empty_and_says_the_profile_is_not_configured()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("proposals-noprofile");
        await factory.SeedAsync(RaceTestSupport.Facts(RaceTestSupport.NewToken(), 15));

        var response = await user.Client.GetAsync(U($"/api/races/proposals?from={Iso(RaceTestSupport.Today)}&to={Iso(RaceTestSupport.Today.AddDays(60))}"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.ReadAsync();
        Assert.False(body.GetProperty("profileConfigured").GetBoolean());
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
        Assert.False(body.GetProperty("truncated").GetBoolean());
        Assert.Equal(JsonValueKind.String, body.GetProperty("catalogueUpdatedUtc").ValueKind);
    }

    [Fact]
    public async Task Proposals_are_filtered_by_the_interests_and_carry_the_race_facts()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("proposals-filter");
        var token = RaceTestSupport.NewToken();
        var road = await factory.SeedAsync(RaceTestSupport.Facts(token, 20, "Road", RaceKind.Road, [10_000, 5_000]));
        var trail = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "Trail", RaceKind.Trail, [15_000]));
        var tooLong = await factory.SeedAsync(RaceTestSupport.Facts(token, 22, "Long", RaceKind.Road, [60_000]));
        var farAway = await factory.SeedAsync(RaceTestSupport.Facts(token, 23, "Far", RaceKind.Road, [10_000], "PL-22", new GeoPoint(54.35, 18.65)));
        await user.ConfigureAsync(token, b =>
        {
            b["kinds"] = new List<string> { "Road" };
            b["regionCodes"] = new List<string> { "PL-24" };
            b["minDistanceMeters"] = 5_000;
            b["maxDistanceMeters"] = 42_195;
        });

        var list = await user.ProposalsAsync();

        Assert.True(list.GetProperty("profileConfigured").GetBoolean());
        Assert.Equal([road.Id], list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("raceId").GetGuid()).ToArray());
        var item = list.ItemFor(road.Id);
        Assert.Equal($"{token} Road", item.GetProperty("name").GetString());
        Assert.Equal(Iso(RaceTestSupport.Today.AddDays(20)), item.GetProperty("date").GetString());
        Assert.Equal("09:00", item.GetProperty("startTime").GetString());
        Assert.Equal("Gliwice", item.GetProperty("locationName").GetString());
        Assert.Equal("PL-24", item.GetProperty("regionCode").GetString());
        Assert.Equal("PL", item.GetProperty("countryCode").GetString());
        Assert.Equal(50.2945, item.GetProperty("location").GetProperty("latitude").GetDouble());
        Assert.Equal("Road", item.GetProperty("kind").GetString());
        Assert.Equal([5_000d, 10_000d], item.GetProperty("distancesMeters").EnumerateArray().Select(d => d.GetDouble()).ToArray());
        Assert.Equal("None", item.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("linkedRunId").ValueKind);
        Assert.True(item.GetProperty("isNew").GetBoolean());
        Assert.False(item.GetProperty("dateChanged").GetBoolean());
        Assert.Equal("demo", item.GetProperty("source").GetString());
        Assert.DoesNotContain(trail.Id, list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("raceId").GetGuid()));
        Assert.DoesNotContain(tooLong.Id, list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("raceId").GetGuid()));
        Assert.DoesNotContain(farAway.Id, list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("raceId").GetGuid()));
    }

    [Fact]
    public async Task The_window_is_clamped_to_today_and_the_horizon_and_orders_by_date()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("proposals-clamp");
        var token = RaceTestSupport.NewToken();
        var past = await factory.SeedAsync(RaceTestSupport.Facts(token, -5, "Past"));
        var later = await factory.SeedAsync(RaceTestSupport.Facts(token, 25, "Later"));
        var sooner = await factory.SeedAsync(RaceTestSupport.Facts(token, 12, "Sooner"));
        var beyond = await factory.SeedAsync(RaceTestSupport.Facts(token, 60, "Beyond"));
        await user.ConfigureAsync(token, b => b["horizonDays"] = 30);
        var today = DateOnly.Parse((await TodayOfAsync(user)).GetString()!, CultureInfo.InvariantCulture);

        var list = await user.ProposalsAsync(fromDays: -10, toDays: 80);

        Assert.Equal(Iso(RaceTestSupport.Today.AddDays(-10)), list.GetProperty("from").GetString());
        Assert.Equal(Iso(today), list.GetProperty("effectiveFrom").GetString());
        Assert.Equal(Iso(today.AddDays(30)), list.GetProperty("effectiveTo").GetString());
        Assert.Equal([sooner.Id, later.Id], list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("raceId").GetGuid()).ToArray());
        Assert.False(list.Contains(past.Id));
        Assert.False(list.Contains(beyond.Id));
    }

    [Fact]
    public async Task A_window_wholly_beyond_the_horizon_is_empty_with_an_inverted_effective_range()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("proposals-beyond");
        var token = RaceTestSupport.NewToken();
        await factory.SeedAsync(RaceTestSupport.Facts(token, 60));
        await user.ConfigureAsync(token, b => b["horizonDays"] = 30);

        var list = await user.ProposalsAsync(fromDays: 50, toDays: 70);

        Assert.Equal(0, list.GetProperty("items").GetArrayLength());
        Assert.True(DateOnly.Parse(list.GetProperty("effectiveTo").GetString()!, CultureInfo.InvariantCulture)
            < DateOnly.Parse(list.GetProperty("effectiveFrom").GetString()!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Dismissed_races_are_hidden_unless_include_dismissed_and_removed_or_duplicate_races_never_show()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("proposals-dismissed");
        var token = RaceTestSupport.NewToken();
        var kept = await factory.SeedAsync(RaceTestSupport.Facts(token, 14, "Kept"));
        var dismissed = await factory.SeedAsync(RaceTestSupport.Facts(token, 15, "Dismissed"));
        var removed = await factory.SeedAsync(RaceTestSupport.Facts(token, 16, "Removed"), r => r.Remove(DateTimeOffset.UtcNow));
        var canonical = await factory.SeedAsync(RaceTestSupport.Facts(token, 17, "Canonical"));
        var duplicate = await factory.SeedAsync(RaceTestSupport.Facts(token, 17, "Duplicate"), r => r.SetDuplicateOf(canonical.Id));
        await user.ConfigureAsync(token);
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(dismissed.Id, "Dismissed")).StatusCode);

        var hidden = await user.ProposalsAsync();
        var shown = await user.ProposalsAsync(includeDismissed: true);

        Assert.Equal([kept.Id, canonical.Id], hidden.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("raceId").GetGuid()).ToArray());
        Assert.Equal([kept.Id, dismissed.Id, canonical.Id], shown.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("raceId").GetGuid()).ToArray());
        Assert.Equal("Dismissed", shown.ItemFor(dismissed.Id).GetProperty("state").GetString());
        Assert.False(shown.Contains(removed.Id));
        Assert.False(shown.Contains(duplicate.Id));
    }

    [Fact]
    public async Task A_window_with_more_catalogue_rows_than_the_cap_is_truncated()
    {
        factory.SkipIfDockerUnavailable();
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Races:Proposals:MaxCatalogueRows", "2"));
        var user = host.EntraUser("proposals-truncated");
        var token = RaceTestSupport.NewToken();
        for (var i = 0; i < 3; i++)
        {
            await host.SeedAsync(RaceTestSupport.Facts(token, 200 + i, $"R{i}"));
        }

        await user.ConfigureAsync(token);
        var list = await user.ProposalsAsync(fromDays: 199, toDays: 203);

        // The cap applies to the catalogue window read, not to the matches. Days 199-203 hold only these three rows (the demo catalogue ends around day 175).
        Assert.True(list.GetProperty("truncated").GetBoolean());
        Assert.Equal(2, list.GetProperty("items").GetArrayLength());

        var exact = await user.ProposalsAsync(fromDays: 200, toDays: 201);
        Assert.False(exact.GetProperty("truncated").GetBoolean());
        Assert.Equal(2, exact.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_fresh_guest_sees_silesian_demo_proposals_only_and_none_in_the_past()
    {
        factory.SkipIfDockerUnavailable();
        await factory.EnsureDemoCatalogueAsync();
        var guest = await factory.GuestSessionAsync("proposals-guest");
        var today = DateOnly.Parse((await TodayOfAsync(guest)).GetString()!, CultureInfo.InvariantCulture);

        var interests = await (await guest.Client.GetAsync(U("/api/races/interests"), Ct)).ReadAsync();
        Assert.True(interests.GetProperty("configured").GetBoolean());
        var list = await (await guest.Client.GetAsync(U($"/api/races/proposals?from={Iso(today)}&to={Iso(today.AddDays(60))}"), Ct)).ReadAsync();

        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.True(list.GetProperty("profileConfigured").GetBoolean());
        Assert.True(items.Count >= 3, $"Expected at least 3 proposals, got {items.Count}.");
        Assert.All(items, i =>
        {
            Assert.Equal("PL-24", i.GetProperty("regionCode").GetString());
            Assert.True(DateOnly.Parse(i.GetProperty("date").GetString()!, CultureInfo.InvariantCulture) >= today);
            Assert.Equal("None", i.GetProperty("state").GetString());
        });
        var dates = items.Select(i => i.GetProperty("date").GetString()).ToList();
        Assert.Equal(dates.Order(StringComparer.Ordinal), dates);
    }

    // ---------- details ----------

    [Fact]
    public async Task Race_details_carry_every_fact_and_are_never_cached()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("race-details");
        var race = await factory.SeedAsync(RaceTestSupport.Facts(RaceTestSupport.NewToken(), 20, "Detail", RaceKind.Trail, [21_097.5, 10_000]));

        var response = await user.GetRaceAsync(race.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.ReadAsync();
        Assert.Equal(race.Id, body.GetProperty("raceId").GetGuid());
        Assert.Equal("Europe/Warsaw", body.GetProperty("timeZoneId").GetString());
        Assert.Equal("Trail", body.GetProperty("surface").GetString());
        Assert.Equal(120, body.GetProperty("elevationGainMeters").GetDouble());
        Assert.Equal(49.99m, body.GetProperty("price").GetProperty("amount").GetDecimal());
        Assert.Equal("PLN", body.GetProperty("price").GetProperty("currency").GetString());
        Assert.Equal(Iso(RaceTestSupport.Today.AddDays(17)), body.GetProperty("signupDeadline").GetString());
        Assert.Equal("Test Club", body.GetProperty("organizerName").GetString());
        Assert.Equal("https://example.org/signup", body.GetProperty("signupUrl").GetString());
        Assert.Equal("https://example.org/event", body.GetProperty("eventUrl").GetString());
        Assert.Equal("https://example.org/source", body.GetProperty("sourceUrl").GetString());
        Assert.False(body.GetProperty("removed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("previousDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("canonicalId").ValueKind);
        Assert.Equal([10_000d, 21_097.5], body.GetProperty("distancesMeters").EnumerateArray().Select(d => d.GetDouble()).ToArray());
        Assert.Equal("None", body.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("lastSeenUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, body.GetProperty("updatedUtc").ValueKind);
    }

    [Fact]
    public async Task An_unknown_race_is_404_and_a_non_guid_id_does_not_match_the_route()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("race-404");

        var missing = await user.GetRaceAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync(U("/api/races/not-a-guid"), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_removed_race_is_404_unless_the_caller_is_interested_and_a_duplicate_exposes_its_canonical_id()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("race-removed");
        var other = factory.EntraUser("race-removed-other");
        var token = RaceTestSupport.NewToken();
        var race = await factory.SeedAsync(RaceTestSupport.Facts(token, 20, "Interesting"));
        var canonical = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "Canonical"));
        var duplicate = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "Duplicate"), r => r.SetDuplicateOf(canonical.Id));
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(race.Id, "Interested")).StatusCode);
        await factory.MutateRaceAsync(race.Id, r => r.Remove(DateTimeOffset.UtcNow));

        var interested = await user.GetRaceAsync(race.Id);
        var stranger = await other.GetRaceAsync(race.Id);
        var dup = await (await user.GetRaceAsync(duplicate.Id)).ReadAsync();

        Assert.Equal(HttpStatusCode.OK, interested.StatusCode);
        var body = await interested.ReadAsync();
        Assert.True(body.GetProperty("removed").GetBoolean());
        Assert.Equal("Interested", body.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(canonical.Id, dup.GetProperty("canonicalId").GetGuid());
    }

    // ---------- state and seen ----------

    [Fact]
    public async Task Marking_a_race_changes_the_proposal_and_none_deletes_the_row()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("state-roundtrip");
        var token = RaceTestSupport.NewToken();
        var race = await factory.SeedAsync(RaceTestSupport.Facts(token, 20));
        await user.ConfigureAsync(token);
        var userId = await user.GetUserIdAsync();

        var interested = await user.SetStateAsync(race.Id, "Interested");

        Assert.Equal(HttpStatusCode.OK, interested.StatusCode);
        var state = await interested.ReadAsync();
        Assert.Equal(race.Id, state.GetProperty("raceId").GetGuid());
        Assert.Equal("Interested", state.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("linkedRunId").ValueKind);
        Assert.False(state.GetProperty("dateChanged").GetBoolean());
        Assert.Equal("Interested", (await user.ProposalsAsync()).ItemFor(race.Id).GetProperty("state").GetString());
        Assert.Equal(1, await factory.StateRowsAsync(userId));

        Assert.Equal("Dismissed", (await (await user.SetStateAsync(race.Id, "Dismissed")).ReadAsync()).GetProperty("state").GetString());
        Assert.Equal(1, await factory.StateRowsAsync(userId));

        var none = await (await user.SetStateAsync(race.Id, "None")).ReadAsync();
        Assert.Equal("None", none.GetProperty("state").GetString());
        Assert.Equal(0, await factory.StateRowsAsync(userId));
        Assert.Equal("None", (await user.ProposalsAsync()).ItemFor(race.Id).GetProperty("state").GetString());

        // Clearing nothing is fine too.
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(race.Id, "None")).StatusCode);
    }

    [Theory]
    [InlineData("SignedUp")]
    [InlineData("Bogus")]
    [InlineData("")]
    public async Task Setting_an_invalid_state_is_400_on_state(string state)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("state-invalid");
        var race = await factory.SeedAsync(RaceTestSupport.Facts(RaceTestSupport.NewToken(), 20));

        var response = await user.SetStateAsync(race.Id, state);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("state", (await response.ReadAsync()).GetProperty("errors").EnumerateObject().Select(e => e.Name));
    }

    [Fact]
    public async Task Setting_a_state_on_an_unknown_race_is_404_and_on_a_removed_or_duplicate_race_only_none_works()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("state-404");
        var token = RaceTestSupport.NewToken();
        var removed = await factory.SeedAsync(RaceTestSupport.Facts(token, 20, "Removed"), r => r.Remove(DateTimeOffset.UtcNow));
        var canonical = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "Canonical"));
        var duplicate = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "Duplicate"), r => r.SetDuplicateOf(canonical.Id));

        Assert.Equal(HttpStatusCode.NotFound, (await user.SetStateAsync(Guid.NewGuid(), "Interested")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.SetStateAsync(Guid.NewGuid(), "None")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.SetStateAsync(removed.Id, "Interested")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.SetStateAsync(duplicate.Id, "Dismissed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(removed.Id, "None")).StatusCode);
    }

    [Fact]
    public async Task The_state_cap_refuses_a_new_mark_with_409_but_still_allows_changes_and_clearing()
    {
        factory.SkipIfDockerUnavailable();
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Races:Proposals:MaxStatesPerUser", "2"));
        var user = host.EntraUser("state-cap");
        var token = RaceTestSupport.NewToken();
        var races = new List<Race>();
        for (var i = 0; i < 3; i++)
        {
            races.Add(await host.SeedAsync(RaceTestSupport.Facts(token, 20 + i, $"R{i}")));
        }

        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(races[0].Id, "Interested")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(races[1].Id, "Dismissed")).StatusCode);
        var refused = await user.SetStateAsync(races[2].Id, "Interested");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await refused.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:proposal-state-limit", problem.GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(races[0].Id, "Dismissed")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(races[1].Id, "None")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(races[2].Id, "Interested")).StatusCode);
    }

    [Fact]
    public async Task Guests_have_a_smaller_state_cap_than_users()
    {
        factory.SkipIfDockerUnavailable();
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Races:Proposals:MaxStatesPerUser", "5");
            b.UseSetting("Races:Proposals:MaxStatesPerGuest", "1");
        });
        var guest = await host.GuestAsync("state-guest-cap");
        var user = host.EntraUser("state-user-cap");
        var token = RaceTestSupport.NewToken();
        var a = await host.SeedAsync(RaceTestSupport.Facts(token, 20, "A"));
        var b2 = await host.SeedAsync(RaceTestSupport.Facts(token, 21, "B"));

        Assert.Equal(HttpStatusCode.OK, (await guest.SetStateAsync(a.Id, "Interested")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await guest.SetStateAsync(b2.Id, "Interested")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(a.Id, "Interested")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.SetStateAsync(b2.Id, "Interested")).StatusCode);
    }

    [Fact]
    public async Task An_interested_race_flags_a_changed_date()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("state-datechanged");
        var token = RaceTestSupport.NewToken();
        var race = await factory.SeedAsync(RaceTestSupport.Facts(token, 20));
        await user.ConfigureAsync(token);
        await user.SetStateAsync(race.Id, "Interested");

        await factory.MutateRaceAsync(race.Id, r => r.ChangeDate(r.Date.AddDays(7), DateTimeOffset.UtcNow));

        Assert.True((await user.ProposalsAsync()).ItemFor(race.Id).GetProperty("dateChanged").GetBoolean());
        var details = await (await user.GetRaceAsync(race.Id)).ReadAsync();
        Assert.True(details.GetProperty("dateChanged").GetBoolean());
        Assert.Equal(Iso(RaceTestSupport.Today.AddDays(20)), details.GetProperty("previousDate").GetString());

        // Re-sending Interested acknowledges the new date.
        var again = await (await user.SetStateAsync(race.Id, "Interested")).ReadAsync();
        Assert.False(again.GetProperty("dateChanged").GetBoolean());
    }

    [Fact]
    public async Task Marking_the_list_as_seen_clears_the_new_badge_until_a_later_import()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("seen");
        var token = RaceTestSupport.NewToken();
        var race = await factory.SeedAsync(RaceTestSupport.Facts(token, 20));
        await user.ConfigureAsync(token);
        Assert.True((await user.ProposalsAsync()).ItemFor(race.Id).GetProperty("isNew").GetBoolean());

        var seen = await user.Client.PostAsync(U("/api/races/proposals/seen"), content: null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, seen.StatusCode);
        Assert.False((await user.ProposalsAsync()).ItemFor(race.Id).GetProperty("isNew").GetBoolean());
        Assert.False((await (await user.GetRaceAsync(race.Id)).ReadAsync()).GetProperty("isNew").GetBoolean());

        // A race imported after the marker is new again (imported "in the future" so the second resolution of the marker cannot hide it).
        var later = await factory.SeedAsync(RaceTestSupport.Facts(token, 21, "Later"), r => typeof(Race).GetProperty(nameof(Race.ImportedUtc))!.SetValue(r, DateTime.UtcNow.AddHours(1)));
        var list = await user.ProposalsAsync();
        Assert.True(list.ItemFor(later.Id).GetProperty("isNew").GetBoolean());
        Assert.False(list.ItemFor(race.Id).GetProperty("isNew").GetBoolean());
    }

    [Fact]
    public async Task Marking_seen_without_interests_is_a_204_no_op()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("seen-noprofile");

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PostAsync(U("/api/races/proposals/seen"), content: null, Ct)).StatusCode);
        Assert.False((await (await user.Client.GetAsync(U("/api/races/interests"), Ct)).ReadAsync()).GetProperty("configured").GetBoolean());
    }

    // ---------- feature switch and client config ----------

    [Fact]
    public async Task While_the_feature_is_off_every_race_endpoint_answers_503_feature_unavailable()
    {
        factory.SkipIfDockerUnavailable();
        using var host = factory.WithWebHostBuilder(b => b.UseSetting("Races:Enabled", "false"));
        var user = host.EntraUser("feature-off");
        var id = Guid.NewGuid();
        var from = Iso(RaceTestSupport.Today);

        var responses = new[]
        {
            await user.Client.GetAsync(U("/api/races/interests"), Ct),
            await user.PutInterestsAsync(RaceTestSupport.Interests(null)),
            await user.Client.GetAsync(U($"/api/races/proposals?from={from}&to={from}"), Ct),
            await user.Client.PostAsync(U("/api/races/proposals/seen"), content: null, Ct),
            await user.GetRaceAsync(id),
            await user.SetStateAsync(id, "None"),
            await user.SignUpAsync(id),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode));
        Assert.Equal("urn:runnersdiary:problem:feature-unavailable", (await responses[0].ReadAsync()).GetProperty("type").GetString());
        var config = await (await host.CreateClient().GetAsync(U("/api/client-config"), Ct)).ReadAsync();
        Assert.False(config.GetProperty("races").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Client_config_announces_the_feature_and_credits_the_real_sources_only()
    {
        factory.SkipIfDockerUnavailable();

        var races = (await (await factory.CreateClient().GetAsync(U("/api/client-config"), Ct)).ReadAsync()).GetProperty("races");

        Assert.True(races.GetProperty("enabled").GetBoolean());
        var source = Assert.Single(races.GetProperty("sources").EnumerateArray());
        Assert.Equal("running-life", source.GetProperty("id").GetString());
        Assert.Equal("running.life", source.GetProperty("name").GetString());
        Assert.Equal("https://running.life/", source.GetProperty("url").GetString());
    }
}
