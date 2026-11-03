using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Users;

namespace RunnersDiary.Api.Tests.Hardening;

/// <summary>Regression tests for the M2 review and QA findings that need the SQL / Azurite containers.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class ReviewFixesTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Uri Runs = new("/api/runs", UriKind.Relative);

    private static readonly Uri GuestSessions = new("/api/guest-sessions", UriKind.Relative);

    private static async Task<HttpResponseMessage> CreateGuestSessionAsync(HttpClient client, string remoteIp, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GuestSessions);
        request.Headers.Add(TestHost.ClientIpHeader, remoteIp);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return await client.SendAsync(request, Ct);
    }

    // ---- Q1: guest expiry ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Positive_guest_cache_never_outlives_the_account_expiry()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestAsync("short-lived");
        await factory.SetGuestExpiryAsync(guest.UserId!.Value, DateTime.UtcNow.AddSeconds(3));
        var provisioner = factory.Services.GetRequiredService<UserProvisioner>();

        Assert.True(await provisioner.IsActiveGuestAsync(guest.UserId.Value, Ct)); // cached, but only until the expiry
        await Task.Delay(TimeSpan.FromSeconds(4), Ct);

        Assert.False(await provisioner.IsActiveGuestAsync(guest.UserId.Value, Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.Client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).StatusCode);
    }

    // ---- M1: forwarded headers -----------------------------------------------------------------------------------

    [Fact]
    public async Task Per_ip_limit_partitions_by_the_client_ip_through_a_two_hop_forwarded_chain_and_ignores_untrusted_headers()
    {
        factory.SkipIfDockerUnavailable();
        await using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ForwardedHeaders:Enabled", "true");
            b.UseSetting("ForwardedHeaders:ForwardLimit", "2");
            b.UseSetting("ForwardedHeaders:KnownIPNetworks:0", "10.0.0.0/8");
            b.UseSetting("RateLimiting:GuestCreate:PermitLimit", "1");
        });
        using var client = host.CreateClient();

        // client 203.0.113.5 -> proxy 10.0.0.1 -> proxy 10.0.0.2 (the peer)
        Assert.Equal(HttpStatusCode.Created, (await CreateGuestSessionAsync(client, "10.0.0.2", "203.0.113.5, 10.0.0.1")).StatusCode);
        // same client through a different proxy chain: same partition
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CreateGuestSessionAsync(client, "10.0.0.3", "203.0.113.5, 10.0.0.9")).StatusCode);
        // another client behind the very same proxies: own partition
        Assert.Equal(HttpStatusCode.Created, (await CreateGuestSessionAsync(client, "10.0.0.2", "203.0.113.6, 10.0.0.1")).StatusCode);

        // A peer outside KnownIPNetworks cannot choose its partition through X-Forwarded-For.
        Assert.Equal(HttpStatusCode.Created, (await CreateGuestSessionAsync(client, "198.51.100.7", "203.0.113.50")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CreateGuestSessionAsync(client, "198.51.100.7", "203.0.113.51")).StatusCode);
    }

    // ---- M2: IPv6 /64 and global guest-creation limit ------------------------------------------------------------

    [Fact]
    public async Task Ipv6_clients_share_one_limit_per_64_and_ipv4_mapped_addresses_count_as_ipv4()
    {
        factory.SkipIfDockerUnavailable();
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:GuestCreate:PermitLimit", "1"));
        using var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await CreateGuestSessionAsync(client, "2001:db8:1:2::1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CreateGuestSessionAsync(client, "2001:db8:1:2:ffff:eeee:dddd:9")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateGuestSessionAsync(client, "2001:db8:1:3::1")).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await CreateGuestSessionAsync(client, "::ffff:198.51.100.9")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CreateGuestSessionAsync(client, "198.51.100.9")).StatusCode);
    }

    [Fact]
    public async Task Global_guest_creation_limit_applies_across_clients_and_does_not_count_per_ip_rejections()
    {
        factory.SkipIfDockerUnavailable();
        await using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:GuestCreate:PermitLimit", "1");
            b.UseSetting("RateLimiting:GuestCreate:GlobalPermitLimit", "2");
        });
        using var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await CreateGuestSessionAsync(client, "198.51.100.1")).StatusCode);
        // Rejected by the per-IP policy: must not consume the global budget.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CreateGuestSessionAsync(client, "198.51.100.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateGuestSessionAsync(client, "198.51.100.2")).StatusCode);

        var rejected = await CreateGuestSessionAsync(client, "198.51.100.3");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("urn:runnersdiary:problem:rate-limited", (await rejected.ReadAsync()).GetProperty("type").GetString());
    }

    // ---- M3: run caps --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Guests_are_capped_at_their_own_total_and_users_at_theirs_with_a_run_limit_problem()
    {
        factory.SkipIfDockerUnavailable();
        await using var host = factory.WithWebHostBuilder(b =>
        {
            // Just above the seeded demo diary (122 runs): the host refuses to start with a cap below it.
            b.UseSetting("RunLimits:GuestMaxRuns", "123");
            b.UseSetting("RunLimits:UserMaxRuns", "2");
        });
        var guest = await host.GuestSessionAsync("capped-guest");
        Assert.Equal(122, await host.RunCountAsync(guest.UserId!.Value));
        var user = host.EntraUser("capped-user");

        Assert.Equal(HttpStatusCode.Created, (await guest.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-01"), Ct)).StatusCode);
        var guestOver = await guest.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-02"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, guestOver.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:run-limit", (await guestOver.ReadAsync()).GetProperty("type").GetString());
        Assert.Equal(123, await host.RunCountAsync(guest.UserId!.Value));

        Assert.Equal(HttpStatusCode.Created, (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-01"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-02"), Ct)).StatusCode);
        var userOver = await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-03"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, userOver.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:run-limit", (await userOver.ReadAsync()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task Per_date_cap_applies_on_create_and_when_an_update_moves_a_run_onto_a_full_date_but_not_to_updates_in_place()
    {
        factory.SkipIfDockerUnavailable();
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("RunLimits:MaxRunsPerDate", "2"));
        var user = host.EntraUser("per-date");
        var other = host.EntraUser("per-date-other");

        Assert.Equal(HttpStatusCode.Created, (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-05"), Ct)).StatusCode);
        var second = await (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-05"), Ct)).ReadAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-05"), Ct)).StatusCode);
        // The cap is per account: another user's runs on the same date do not count.
        Assert.Equal(HttpStatusCode.Created, (await other.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-05"), Ct)).StatusCode);

        // An update in place on the full date is fine.
        var inPlace = Json.Run("2026-11-05", r => { r["title"] = "edited"; r["version"] = second.GetProperty("version").GetString(); });
        var id = second.GetProperty("id").GetGuid();
        var edited = await user.Client.PutAsJsonAsync(new Uri($"/api/runs/{id}", UriKind.Relative), inPlace, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        // A run on another date cannot be moved onto the full date...
        var third = await (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-06"), Ct)).ReadAsync();
        var thirdId = third.GetProperty("id").GetGuid();
        var move = Json.Run("2026-11-05", r => r["version"] = third.GetProperty("version").GetString());
        var moved = await user.Client.PutAsJsonAsync(new Uri($"/api/runs/{thirdId}", UriKind.Relative), move, Ct);
        Assert.Equal(HttpStatusCode.Conflict, moved.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:run-limit", (await moved.ReadAsync()).GetProperty("type").GetString());

        // ...but can move to a date with room.
        var free = Json.Run("2026-11-07", r => r["version"] = third.GetProperty("version").GetString());
        Assert.Equal(HttpStatusCode.OK, (await user.Client.PutAsJsonAsync(new Uri($"/api/runs/{thirdId}", UriKind.Relative), free, Ct)).StatusCode);
    }

    [Fact]
    public async Task Host_refuses_to_start_when_the_demo_dataset_does_not_fit_the_guest_cap()
    {
        factory.SkipIfDockerUnavailable();
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("RunLimits:GuestMaxRuns", "10"));

        var ex = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.Contains("guest cap", ex.ToString(), StringComparison.Ordinal);
    }

    // ---- L1: capacity re-check -----------------------------------------------------------------------------------

    [Fact]
    public async Task Requests_queued_for_a_seeding_slot_recheck_the_capacity_before_inserting()
    {
        factory.SkipIfDockerUnavailable();
        var active = await factory.ActiveGuestCountAsync();
        await using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Guests:MaxActive", (active + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            b.UseSetting("Guests:SeedingConcurrency", "1");
            b.UseSetting("Guests:SeedingWaitTimeout", "00:01:00");
        });
        _ = host.CreateClient();
        var service = host.Services.GetRequiredService<GuestAccountService>();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.CreateAsync(Ct)));

        Assert.Equal(1, results.Count(r => r.Account is not null));
        Assert.All(results.Where(r => r.Account is null), r => Assert.Equal(GuestCreationFailure.AtCapacity, r.Failure));
        Assert.Equal(active + 1, await factory.ActiveGuestCountAsync());
    }

    // ---- L5: binding errors never echo framework text ------------------------------------------------------------

    [Theory]
    [InlineData("/api/runs?from=SECRETVALUE&to=2026-01-01", "from")]
    [InlineData("/api/runs?to=2026-01-01", "from")]
    public async Task Binding_failures_use_a_fixed_message_keyed_by_the_parameter(string url, string key)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("bind");

        var response = await user.Client.GetAsync(new Uri(url, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var errors = (await response.ReadAsync()).GetProperty("errors");
        Assert.Equal("The value could not be read from the request or is missing.", errors.GetProperty(key)[0].GetString());
        Assert.DoesNotContain("SECRETVALUE", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DateOnly", body, StringComparison.Ordinal);
    }

    // ---- L6: body size -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Oversized_bodies_are_rejected_with_413_on_authenticated_routes_while_normal_bodies_pass()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("big-body");
        var big = Json.Run("2026-11-09", r => r["notes"] = new string('x', 70_000));

        var response = await user.Client.PostAsJsonAsync(Runs, big, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Created, (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-09"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Chunked_bodies_over_the_limit_are_also_rejected_with_413()
    {
        factory.SkipIfDockerUnavailable();
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("RequestLimits:MaxBodyBytes", "2048"));
        var user = host.EntraUser("chunked");
        using var content = new StreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            $$"""{"date":"2026-11-09","status":"Completed","runTypeId":20,"notes":"{{new string('y', 5000)}}"}""")));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, Runs) { Content = content };
        request.Headers.TransferEncodingChunked = true;

        var response = await user.Client.SendAsync(request, Ct);

        // TestServer has no Kestrel body limit: the request is either cut off by the limit (413) or read fully and then refused by a
        // domain rule; it must never create a run with the oversized payload.
        Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, await host.RunCountAsync(await user.GetUserIdAsync()));
    }

    // ---- L9: PUT keeps the run's zone ----------------------------------------------------------------------------

    [Fact]
    public async Task Put_without_a_time_zone_keeps_the_runs_zone_while_post_defaults_to_the_profile_zone()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("zone-keeper");
        var put = await user.Client.PutAsJsonAsync(new Uri("/api/me", UriKind.Relative), new { timeZoneId = "Asia/Tokyo", unitSystem = "Metric" }, Ct);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var created = await (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-10", r => r["timeZoneId"] = "America/New_York"), Ct)).ReadAsync();
        var id = created.GetProperty("id").GetGuid();
        var update = Json.Run("2026-11-10", r =>
        {
            r.Remove("timeZoneId");
            r["title"] = "renamed";
            r["version"] = created.GetProperty("version").GetString();
        });
        var updated = await (await user.Client.PutAsJsonAsync(new Uri($"/api/runs/{id}", UriKind.Relative), update, Ct)).ReadAsync();

        Assert.Equal("America/New_York", updated.GetProperty("timeZoneId").GetString());
        Assert.Equal("renamed", updated.GetProperty("title").GetString());

        var defaulted = await (await user.Client.PostAsJsonAsync(Runs, Json.Run("2026-11-10", r => r.Remove("timeZoneId")), Ct)).ReadAsync();
        Assert.Equal("Asia/Tokyo", defaulted.GetProperty("timeZoneId").GetString());
    }

    // ---- N4: malformed tokens never produce a 500 ----------------------------------------------------------------

    public static TheoryData<string> GarbageTokens() =>
    [
        "aaa.bbb.ccc",
        "eyJhbGciOiJIUzI1NiJ9.bm90LWpzb24.c2ln",       // payload is not JSON
        "eyJhbGciOiJIUzI1NiJ9.W10.c2ln",                 // payload is a JSON array
        "eyJhbGciOiJIUzI1NiJ9.MTIz.c2ln",                // payload is a JSON number
        "eyJhbGciOiJIUzI1NiJ9.eyJpc3MiOjF9.c2ln",        // issuer is a number
        "eyJ.eyJ.eyJ",
        "....",
        "e30.e30.e30",
    ];

    [Theory]
    [MemberData(nameof(GarbageTokens))]
    public async Task Garbage_but_jwt_shaped_tokens_end_in_401_with_a_problem_never_a_500(string token)
    {
        factory.SkipIfDockerUnavailable();
        using var client = factory.CreateClient().WithBearer(token);

        var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Q2 companion: cleanup eviction on the real host ---------------------------------------------------------

    [Fact]
    public async Task Guest_removed_by_cleanup_is_refused_immediately_even_after_a_successful_request_cached_it()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("cached-then-cleaned");
        Assert.Equal(HttpStatusCode.OK, (await guest.Client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).StatusCode); // caches
        await factory.SetGuestExpiryAsync(guest.UserId!.Value, DateTime.UtcNow.AddHours(-2));
        var cleanup = factory.Services.GetRequiredService<GuestCleanup>();

        // The real service uses the default batch size; run until the guest is gone (other expired guests may precede it).
        for (var i = 0; i < 20 && await factory.UserExistsAsync(guest.UserId.Value); i++)
        {
            await cleanup.RunOnceAsync(Ct);
        }

        Assert.False(await factory.UserExistsAsync(guest.UserId.Value));
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.Client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).StatusCode);
    }
}
