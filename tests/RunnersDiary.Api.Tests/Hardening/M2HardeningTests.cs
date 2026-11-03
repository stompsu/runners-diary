using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Hardening;

/// <summary>
/// QA additions for M2 (AC3, AC4, AC6, AC8): gaps found when mapping the acceptance criteria to the existing tests.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class M2HardeningTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri RunUri(Guid id) => new($"/api/runs/{id}", UriKind.Relative);

    private static readonly Uri Me = new("/api/me", UriKind.Relative);

    private static readonly Uri Runs = new("/api/runs", UriKind.Relative);

    // ---- update / ownership --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("userId")]
    [InlineData("id")]
    [InlineData("UserId")]
    public async Task Update_rejects_ownership_members_and_the_run_stays_with_its_owner(string member)
    {
        factory.SkipIfDockerUnavailable();
        var owner = factory.EntraUser("upd-owner");
        var other = factory.EntraUser("upd-other");
        var otherId = await other.GetUserIdAsync();
        var created = await (await owner.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-12"), Ct)).ReadAsync();
        var id = created.GetProperty("id").GetGuid();

        var body = Json.Run("2026-11-12", r => { r["version"] = created.GetProperty("version").GetString(); r[member] = otherId; });
        var response = await owner.Client.PutAsJsonAsync(RunUri(id), body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, await factory.RunCountAsync(await owner.GetUserIdAsync()));
        Assert.Equal(0, await factory.RunCountAsync(otherId));
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(RunUri(id), Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_with_userId_of_another_account_is_rejected_for_guests_too()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("create-guest");
        var victim = await factory.GuestAsync("create-victim");
        using var content = new StringContent(
            $$"""{"date":"2026-11-02","status":"Completed","runTypeId":20,"userId":"{{victim.UserId}}"}""", Encoding.UTF8, "application/json");

        var response = await guest.Client.PostAsync(Runs, content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.RunCountAsync(victim.UserId!.Value));
        Assert.Equal(0, await factory.RunCountAsync(guest.UserId!.Value));
    }

    [Fact]
    public async Task Update_with_userId_unknown_member_via_raw_json_is_400_and_leaves_the_row_unchanged()
    {
        factory.SkipIfDockerUnavailable();
        var owner = factory.EntraUser("raw-upd");
        var created = await (await owner.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-12", r => r["title"] = "keep"), Ct)).ReadAsync();
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetString();
        using var content = new StringContent(
            $$"""{"date":"2026-11-12","status":"Completed","runTypeId":20,"title":"changed","version":"{{version}}","userId":"{{Guid.NewGuid()}}"}""",
            Encoding.UTF8, "application/json");

        var response = await owner.Client.PutAsync(RunUri(id), content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await (await owner.Client.GetAsync(RunUri(id), Ct)).ReadAsync();
        Assert.Equal("keep", after.GetProperty("title").GetString());
        Assert.Equal(version, after.GetProperty("version").GetString());
    }

    // ---- DST / calendar ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Fall_back_ambiguous_start_time_resolves_to_the_earlier_occurrence()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("dstamb");

        var run = await (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-10-25", r => r["startTime"] = "02:30"), Ct)).ReadAsync();

        Assert.Equal("2026-10-25T00:30:00Z", run.GetProperty("startUtc").GetString());
    }

    [Fact]
    public async Task Late_evening_run_stays_on_its_local_date_in_the_calendar_and_utc_is_only_derived()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("tzedge");
        // 23:30 Warsaw (CET, UTC+1) on Sun 2026-11-01 is 22:30Z: same date. In America/Los_Angeles 23:30 on 2026-11-01 is already 2026-11-02 in UTC.
        var run = await (await user.Client.PostAsJsonAsync(Runs,
            Json.Run("2026-11-01", r => { r["startTime"] = "23:30"; r["timeZoneId"] = "America/Los_Angeles"; }), Ct)).ReadAsync();
        Assert.Equal("2026-11-01", run.GetProperty("date").GetString());
        Assert.Equal("2026-11-02T07:30:00Z", run.GetProperty("startUtc").GetString());

        var month = await (await user.Client.GetAsync(new Uri("/api/calendar/months/2026/11", UriKind.Relative), Ct)).ReadAsync();
        var weeks = month.GetProperty("weeks").EnumerateArray().ToList();
        // Sunday 1 Nov belongs to the week starting Mon 26 Oct (grid week 0), not to the week of its UTC date.
        Assert.Equal(1, weeks[0].GetProperty("completed").GetProperty("runCount").GetInt32());
        Assert.Equal(0, weeks[1].GetProperty("completed").GetProperty("runCount").GetInt32());
    }

    [Fact]
    public async Task Calendar_across_the_iso_year_boundary_uses_week_53_of_2026()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("isoyear");
        foreach (var date in new[] { "2026-12-28", "2027-01-01", "2027-01-03", "2027-01-04" })
        {
            await user.Client.PostAsJsonAsync(Runs, Json.Run(date, r => { r["distanceMeters"] = 1000; r["durationSeconds"] = 300; }), Ct);
        }

        var jan = await (await user.Client.GetAsync(new Uri("/api/calendar/months/2027/1", UriKind.Relative), Ct)).ReadAsync();
        var weeks = jan.GetProperty("weeks").EnumerateArray().ToList();
        Assert.Equal("2026-12-28", jan.GetProperty("gridStart").GetString());
        Assert.Equal("2027-01-31", jan.GetProperty("gridEnd").GetString());
        Assert.Equal(5, weeks.Count);
        Assert.Equal((2026, 53), (weeks[0].GetProperty("isoYear").GetInt32(), weeks[0].GetProperty("isoWeek").GetInt32()));
        Assert.Equal((2027, 1), (weeks[1].GetProperty("isoYear").GetInt32(), weeks[1].GetProperty("isoWeek").GetInt32()));
        var w53 = weeks[0].GetProperty("completed");
        Assert.Equal(3, w53.GetProperty("runCount").GetInt32());   // 28 Dec, 1 Jan, 3 Jan
        Assert.Equal(3000, w53.GetProperty("distanceMeters").GetDouble());
        Assert.Equal(1, weeks[1].GetProperty("completed").GetProperty("runCount").GetInt32()); // 4 Jan

        var dec = await (await user.Client.GetAsync(new Uri("/api/calendar/months/2026/12", UriKind.Relative), Ct)).ReadAsync();
        var decWeeks = dec.GetProperty("weeks").EnumerateArray().ToList();
        Assert.Equal("2027-01-03", dec.GetProperty("gridEnd").GetString());
        Assert.Equal(53, decWeeks[^1].GetProperty("isoWeek").GetInt32());
        Assert.Equal(2026, decWeeks[^1].GetProperty("isoYear").GetInt32());
    }

    [Theory]
    [InlineData("2026-01-01", "2026-04-02", HttpStatusCode.OK)]       // 92 days inclusive
    [InlineData("2026-01-01", "2026-04-03", HttpStatusCode.BadRequest)] // 93 days
    public async Task Range_boundary_is_92_days_inclusive(string from, string to, HttpStatusCode expected)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("range92");
        var response = await user.Client.GetAsync(new Uri($"/api/runs?from={from}&to={to}", UriKind.Relative), Ct);
        Assert.Equal(expected, response.StatusCode);
    }

    // ---- /api/me isolation ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Me_returns_only_the_callers_own_profile_for_guest_and_user_in_both_directions()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("me-guest");
        var user = factory.EntraUser("me-user");
        var guestMe = await (await guest.Client.GetAsync(Me, Ct)).ReadAsync();
        var userMe = await (await user.Client.GetAsync(Me, Ct)).ReadAsync();

        Assert.Equal(guest.UserId, guestMe.GetProperty("id").GetGuid());
        Assert.Equal("Guest", guestMe.GetProperty("kind").GetString());
        Assert.Equal(await user.GetUserIdAsync(), userMe.GetProperty("id").GetGuid());
        Assert.Equal("User", userMe.GetProperty("kind").GetString());
        Assert.NotEqual(guestMe.GetProperty("id").GetGuid(), userMe.GetProperty("id").GetGuid());

        // Updating the profile of one never touches the other.
        var put = await guest.Client.PutAsJsonAsync(Me, new { timeZoneId = "Asia/Tokyo", unitSystem = "Metric" }, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var userAfter = await (await user.Client.GetAsync(Me, Ct)).ReadAsync();
        Assert.Equal(userMe.GetProperty("timeZoneId").GetString(), userAfter.GetProperty("timeZoneId").GetString());
    }

    [Fact]
    public async Task Me_update_rejects_identity_members()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("me-put");
        var other = factory.EntraUser("me-put-other");
        using var content = new StringContent($$"""{"timeZoneId":"Europe/Warsaw","unitSystem":"Metric","id":"{{await other.GetUserIdAsync()}}","kind":"Guest"}""", Encoding.UTF8, "application/json");

        var response = await user.Client.PutAsync(Me, content, Ct);

        Assert.True((int)response.StatusCode is >= 400 and < 500, response.StatusCode.ToString());
        var me = await (await user.Client.GetAsync(Me, Ct)).ReadAsync();
        Assert.Equal("User", me.GetProperty("kind").GetString());
        Assert.NotEqual(await other.GetUserIdAsync(), me.GetProperty("id").GetGuid());
    }

    // ---- guest expiry with real issuance -------------------------------------------------------------------------

    private WebApplicationFactoryHandle WithClock(FakeTimeProvider clock) => new(factory.WithWebHostBuilder(b =>
        b.ConfigureTestServices(s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(clock);
        })));

    private sealed class WebApplicationFactoryHandle(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> inner) : IAsyncDisposable
    {
        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory => inner;

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    [Fact]
    public async Task A_token_issued_by_the_real_endpoint_more_than_24h_ago_is_rejected()
    {
        factory.SkipIfDockerUnavailable();
        // The server clock believes it is 25 h in the past while issuing; the validator uses the real clock, so the token is expired.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-25));
        await using var past = WithClock(clock);
        using var anonymous = past.Factory.CreateClient();
        var created = await anonymous.PostAsync(new Uri("/api/guest-sessions", UriKind.Relative), content: null, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.ReadAsync();

        using var client = factory.CreateClient().WithBearer(body.GetProperty("accessToken").GetString()!);
        var response = await client.GetAsync(Me, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_token_with_time_left_is_rejected_once_the_guest_account_itself_has_expired()
    {
        // UserProvisioner.IsActiveGuestAsync is documented as "existing, unexpired guest" (defence in depth when cleanup lags).
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("expired-row");
        await factory.SetGuestExpiryAsync(guest.UserId!.Value, DateTime.UtcNow.AddMinutes(-10));

        // Fresh factory instance => fresh in-memory auth cache, so only the database state decides.
        await using var fresh = factory.WithWebHostBuilder(_ => { });
        using var client = fresh.CreateClient().WithBearer(guest.Client.DefaultRequestHeaders.Authorization!.Parameter!);
        var response = await client.GetAsync(Me, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task After_guest_B_is_cleaned_up_his_token_is_dead_and_guest_A_is_unaffected_and_cannot_reach_Bs_ids()
    {
        factory.SkipIfDockerUnavailable();
        var a = await factory.GuestSessionAsync("A");
        var b = await factory.GuestSessionAsync("B");
        var bId = b.UserId!.Value;
        var bRuns = await (await b.Client.GetAsync(new Uri("/api/runs?from=2026-01-01&to=2026-03-01", UriKind.Relative), Ct)).ReadAsync();
        var bToken = b.Client.DefaultRequestHeaders.Authorization!.Parameter!;
        var aRunCount = await factory.RunCountAsync(a.UserId!.Value);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await factory.SetGuestExpiryAsync(bId, clock.GetUtcNow().UtcDateTime.AddHours(-2));
        var cleanup = new GuestCleanup(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<IBlobStore>(),
            clock,
            Options.Create(new GuestOptions { Cleanup = { BatchSize = 1000 } }),
            NullLogger<GuestCleanup>.Instance);
        await cleanup.RunOnceAsync(Ct);
        Assert.False(await factory.UserExistsAsync(bId));

        // B's still-unexpired-by-exp token must not authenticate any more (same host instance: includes any auth caching).
        var deadResponse = await b.Client.GetAsync(Me, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, deadResponse.StatusCode);
        using var anyOther = factory.CreateClient().WithBearer(bToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anyOther.GetAsync(new Uri("/api/runs?from=2026-01-01&to=2026-03-01", UriKind.Relative), Ct)).StatusCode);

        // A is untouched and sees none of B's ids.
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync(Me, Ct)).StatusCode);
        Assert.Equal(aRunCount, await factory.RunCountAsync(a.UserId!.Value));
        foreach (var run in bRuns.GetProperty("items").EnumerateArray().Take(3))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync(RunUri(run.GetProperty("id").GetGuid()), Ct)).StatusCode);
        }
    }

    // ---- OpenAPI -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Live_openapi_document_marks_only_anonymous_operations_without_security()
    {
        factory.SkipIfDockerUnavailable();
        var path = LocateCommittedDocument();
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, Ct));
        var operations = doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(m => m.Value))
            .ToDictionary(o => o.GetProperty("operationId").GetString()!);

        var anonymous = operations.Where(o => !o.Value.TryGetProperty("security", out _)).Select(o => o.Key).Order().ToArray();

        Assert.Equal(["createGuestSession", "getApiHealth", "getClientConfig"], anonymous);
    }

    private static string LocateCommittedDocument()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RunnersDiary.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "src", "RunnersDiary.Api", "openapi", "runnersdiary-api.json");
    }
}
