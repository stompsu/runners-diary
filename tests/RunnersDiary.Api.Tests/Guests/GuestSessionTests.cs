using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Guests;

/// <summary>AC5 and the guest-creation contract: <c>POST /api/guest-sessions</c> gives a usable token and a populated, private diary.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class GuestSessionTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Uri Endpoint = new("/api/guest-sessions", UriKind.Relative);

    [Fact]
    public async Task Creating_a_session_returns_a_24h_token_that_works_and_is_not_cacheable()
    {
        factory.SkipIfDockerUnavailable();
        using var anonymous = factory.CreateClient();

        var before = DateTimeOffset.UtcNow;
        var response = await anonymous.PostAsync(Endpoint, content: null, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.ReadAsync();
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        var expiresAt = body.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expiresAt, before.AddHours(24).AddSeconds(-2), DateTimeOffset.UtcNow.AddHours(24).AddSeconds(2));

        // The token's exp equals the account expiry.
        var token = body.GetProperty("accessToken").GetString()!;
        var payload = JsonDocument.Parse(Base64UrlDecode(token.Split('.')[1])).RootElement;
        Assert.Equal(expiresAt.ToUnixTimeSeconds(), payload.GetProperty("exp").GetInt64());
        Assert.Equal(body.GetProperty("userId").GetGuid().ToString("D"), payload.GetProperty("sub").GetString());

        using var client = factory.CreateClient().WithBearer(token);
        var me = await (await client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).ReadAsync();
        Assert.Equal("Guest", me.GetProperty("kind").GetString());
        Assert.Equal("Europe/Warsaw", me.GetProperty("timeZoneId").GetString());
        Assert.Equal(expiresAt.UtcDateTime, me.GetProperty("guestExpiresAt").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task A_fresh_guest_sees_a_populated_calendar_for_the_current_month_and_a_race_history()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("fresh");

        var me = await (await guest.Client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).ReadAsync();
        var today = DateOnly.Parse(me.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);

        var month = await (await guest.Client.GetAsync(new Uri($"/api/calendar/months/{today.Year}/{today.Month}", UriKind.Relative), Ct)).ReadAsync();
        var monthRuns = month.GetProperty("runs").EnumerateArray().ToList();
        Assert.True(monthRuns.Count >= 4, $"Only {monthRuns.Count} runs in the current month.");
        Assert.DoesNotContain(monthRuns, r => r.GetProperty("status").GetString() == "Completed"
            && DateOnly.Parse(r.GetProperty("date").GetString()!, CultureInfo.InvariantCulture) > today);

        // History: ~26 weeks back and the planned weeks ahead, in windows of at most 92 days.
        var all = new List<JsonElement>();
        for (var from = today.AddDays(-200); from <= today.AddDays(45); from = from.AddDays(90))
        {
            var to = from.AddDays(89);
            var list = await (await guest.Client.GetAsync(new Uri($"/api/runs?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", UriKind.Relative), Ct)).ReadAsync();
            all.AddRange(list.GetProperty("items").EnumerateArray());
        }

        Assert.Equal(all.Count, all.Select(r => r.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.InRange(all.Count, 100, 140);

        var raceTypeIds = new[] { 1, 2, 3, 4, 5, 6 };
        var races = all.Where(r => raceTypeIds.Contains(r.GetProperty("runTypeId").GetInt32())).ToList();
        var completed = races.Where(r => r.GetProperty("status").GetString() == "Completed").ToList();
        var planned = races.Where(r => r.GetProperty("status").GetString() == "Planned").ToList();

        Assert.True(completed.Count >= 3);
        Assert.All(completed, r =>
        {
            Assert.Equal(JsonValueKind.Object, r.GetProperty("rating").ValueKind);
            Assert.Equal(JsonValueKind.Number, r.GetProperty("race").GetProperty("resultTimeSeconds").ValueKind);
            Assert.Contains(r.GetProperty("tracks").EnumerateArray(), t => t.GetProperty("kind").GetString() == "RecordedTrack");
        });
        Assert.InRange(planned.Count, 1, 2);
        Assert.All(planned, r => Assert.Contains(r.GetProperty("tracks").EnumerateArray(), t => t.GetProperty("kind").GetString() == "PlannedCourse"));
    }

    [Fact]
    public async Task Each_guest_gets_private_blob_copies_under_the_users_prefix_matching_the_track_records()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("blobs");
        var userId = guest.UserId!.Value;

        var tracks = await factory.TracksAsync(userId);
        var blobs = await factory.BlobNamesAsync(BlobPaths.UserPrefix(userId));

        Assert.True(tracks.Count >= 4);
        Assert.Equal(tracks.Select(t => t.RawBlobName).Order(StringComparer.Ordinal), blobs.Order(StringComparer.Ordinal));
        Assert.All(tracks, t =>
        {
            Assert.StartsWith($"users/{userId:N}/tracks/{t.Id:N}/", t.RawBlobName, StringComparison.Ordinal);
            Assert.True(t.SizeBytes > 0);
        });

        var container = factory.Services.GetRequiredService<Azure.Storage.Blobs.BlobServiceClient>().GetBlobContainerClient(BlobPaths.ContainerName);
        var content = await container.GetBlobClient(tracks[0].RawBlobName).DownloadContentAsync(Ct);
        Assert.Equal(tracks[0].SizeBytes, content.Value.Content.ToMemory().Length);
        Assert.StartsWith("<?xml", Encoding.UTF8.GetString(content.Value.Content.ToMemory().Span), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_guests_have_disjoint_data()
    {
        factory.SkipIfDockerUnavailable();
        var a = await factory.GuestSessionAsync("a");
        var b = await factory.GuestSessionAsync("b");

        Assert.NotEqual(a.UserId, b.UserId);
        Assert.Equal(await factory.RunCountAsync(a.UserId!.Value), await factory.RunCountAsync(b.UserId!.Value));

        var me = await (await a.Client.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).ReadAsync();
        var today = DateOnly.Parse(me.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);
        var uri = new Uri($"/api/runs?from={today.AddDays(-60):yyyy-MM-dd}&to={today.AddDays(30):yyyy-MM-dd}", UriKind.Relative);
        var idsA = (await (await a.Client.GetAsync(uri, Ct)).ReadAsync()).GetProperty("items").Ids().ToHashSet();
        var idsB = (await (await b.Client.GetAsync(uri, Ct)).ReadAsync()).GetProperty("items").Ids().ToHashSet();

        Assert.NotEmpty(idsA);
        Assert.Equal(idsA.Count, idsB.Count);
        Assert.Empty(idsA.Intersect(idsB));

        var blobsA = await factory.TracksAsync(a.UserId!.Value);
        var blobsB = await factory.TracksAsync(b.UserId!.Value);
        Assert.Empty(blobsA.Select(t => t.RawBlobName).Intersect(blobsB.Select(t => t.RawBlobName)));
    }

    [Fact]
    public async Task At_capacity_the_endpoint_answers_503_with_retry_after_and_creates_nothing()
    {
        factory.SkipIfDockerUnavailable();
        var active = await factory.ActiveGuestCountAsync();
        await using var full = factory.WithWebHostBuilder(b => b.UseSetting("Guests:MaxActive", active.ToString(CultureInfo.InvariantCulture)));
        using var client = full.CreateClient();

        var response = await client.PostAsync(Endpoint, content: null, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        var problem = await response.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:guest-capacity", problem.GetProperty("type").GetString());
        Assert.Equal(active, await factory.ActiveGuestCountAsync());
    }

    [Fact]
    public async Task A_failure_while_seeding_answers_503_and_leaves_no_rows_or_blobs()
    {
        factory.SkipIfDockerUnavailable();
        FaultyBlobStore? faulty = null;
        await using var broken = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.AddSingleton<IBlobStore>(sp =>
                faulty = new FaultyBlobStore(new AzureBlobStore(sp.GetRequiredService<Azure.Storage.Blobs.BlobServiceClient>())) { FailUploadsAfter = 2 });
        }));
        var guestsBefore = await factory.ActiveGuestCountAsync();
        using var client = broken.CreateClient();

        var response = await client.PostAsync(Endpoint, content: null, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(guestsBefore, await factory.ActiveGuestCountAsync());
        Assert.NotNull(faulty);
        Assert.NotEmpty(faulty.Uploaded); // some blobs did go up before the failure ...
        foreach (var name in faulty.Uploaded)
        {
            // ... and were removed again.
            Assert.Empty(await factory.BlobNamesAsync(name));
        }
    }

    [Fact]
    public async Task Creating_a_session_needs_no_token_but_other_endpoints_still_do()
    {
        factory.SkipIfDockerUnavailable();
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await anonymous.PostAsync(Endpoint, content: null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(new Uri("/api/me", UriKind.Relative), Ct)).StatusCode);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
