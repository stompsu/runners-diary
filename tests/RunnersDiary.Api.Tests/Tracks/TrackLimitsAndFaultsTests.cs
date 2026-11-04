using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Tracks;

/// <summary>AC5 and AC6 (plan section 1): configured limits, rate limits, processing slots, and blob failures at each step.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class TrackLimitsAndFaultsTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] Road = TrackTestSupport.Fixture("road-5k.gpx");

    [Fact]
    public async Task The_track_cap_gives_409_track_limit_for_users_but_replacing_at_the_cap_still_works()
    {
        factory.SkipIfDockerUnavailable();
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("GpxLimits:UserMaxTracks", "1"));
        var user = limited.EntraUser("capped");
        var runId = await user.CreateRunAsync();
        var otherRun = await user.CreateRunAsync("2026-11-13");
        await user.UploadOkAsync(runId);

        var over = await user.UploadAsync(otherRun, Road);
        var replace = await user.UploadAsync(runId, Road, replace: true);

        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        var problem = await over.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:track-limit", problem.GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.Created, replace.StatusCode);
        Assert.Single(await limited.TracksAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task Guests_have_their_own_lower_track_cap()
    {
        factory.SkipIfDockerUnavailable();
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("GpxLimits:GuestMaxTracks", "1"));
        var guest = await limited.GuestAsync("capguest");
        var user = limited.EntraUser("uncapped");
        var guestRuns = new[] { await guest.CreateRunAsync(), await guest.CreateRunAsync("2026-11-13") };
        var userRuns = new[] { await user.CreateRunAsync(), await user.CreateRunAsync("2026-11-13") };

        await guest.UploadOkAsync(guestRuns[0]);
        await user.UploadOkAsync(userRuns[0]);

        Assert.Equal(HttpStatusCode.Conflict, (await guest.UploadAsync(guestRuns[1], Road)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await user.UploadAsync(userRuns[1], Road)).StatusCode);
    }

    [Fact]
    public async Task Uploads_are_rate_limited_per_caller_with_a_lower_limit_for_guests()
    {
        factory.SkipIfDockerUnavailable();
        await using var limited = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:GpxUpload:UserPermitLimit", "2");
            b.UseSetting("RateLimiting:GpxUpload:GuestPermitLimit", "1");
        });
        var user = limited.EntraUser("ratelimited");
        var other = limited.EntraUser("quiet");
        var guest = await limited.GuestAsync("ratelimitedguest");
        var userRun = await user.CreateRunAsync();
        var guestRun = await guest.CreateRunAsync();
        var otherRun = await other.CreateRunAsync();

        // Rejected uploads count too (the bad file is the cheap abuse case).
        Assert.Equal(HttpStatusCode.BadRequest, (await user.UploadAsync(userRun, TrackTestSupport.Fixture("malformed.gpx"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await user.UploadAsync(userRun, Road)).StatusCode);
        var third = await user.UploadAsync(userRun, Road, replace: true);
        Assert.Equal(HttpStatusCode.Created, (await guest.UploadAsync(guestRun, Road)).StatusCode);
        var guestSecond = await guest.UploadAsync(guestRun, Road, replace: true);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("urn:runnersdiary:problem:rate-limited", (await third.ReadAsync()).GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.TooManyRequests, guestSecond.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await other.UploadAsync(otherRun, Road)).StatusCode);
        // Only the upload route is affected.
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(new Uri("/api/run-types", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task When_all_processing_slots_are_taken_the_answer_is_503_processing_busy_and_nothing_is_stored()
    {
        factory.SkipIfDockerUnavailable();
        await using var busy = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("GpxLimits:MaxConcurrentProcessing", "0");
            b.UseSetting("GpxLimits:ProcessingWait", "00:00:00");
        });
        var user = busy.EntraUser("busy");
        var runId = await user.CreateRunAsync();

        var response = await user.UploadAsync(runId, Road);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.RetryAfter is not null);
        Assert.Equal("urn:runnersdiary:problem:processing-busy", (await response.ReadAsync()).GetProperty("type").GetString());
        Assert.Empty(await busy.BlobNamesAsync(BlobPaths.UserPrefix(await user.GetUserIdAsync())));
        Assert.Empty(await busy.TracksAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_slot_is_released_after_each_upload()
    {
        factory.SkipIfDockerUnavailable();
        await using var oneSlot = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("GpxLimits:MaxConcurrentProcessing", "1");
            b.UseSetting("GpxLimits:ProcessingWait", "00:00:00");
        });
        var user = oneSlot.EntraUser("sequential");
        var runId = await user.CreateRunAsync();

        // Rejected, accepted, replaced: each one must hand the single slot back.
        Assert.Equal(HttpStatusCode.BadRequest, (await user.UploadAsync(runId, TrackTestSupport.Fixture("malformed.gpx"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await user.UploadAsync(runId, Road)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await user.UploadAsync(runId, Road, replace: true)).StatusCode);
    }

    private sealed class Capture : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Sink(this);

        public void Dispose()
        {
        }

        private sealed class Sink(Capture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Messages)
                {
                    owner.Messages.Add(formatter(state, exception));
                }
            }
        }
    }

    private (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory, FaultyBlobStore Blobs, Capture Log) Faulty(int? failUploadsAfter = null)
    {
        var capture = new Capture();
        var broken = factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services => services.AddLogging(l => l.AddProvider(capture)).AddSingleton<IBlobStore>(sp =>
                new FaultyBlobStore(new AzureBlobStore(sp.GetRequiredService<Azure.Storage.Blobs.BlobServiceClient>())) { FailUploadsAfter = failUploadsAfter }));
        });
        return (broken, (FaultyBlobStore)broken.Services.GetRequiredService<IBlobStore>(), capture);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_failing_blob_upload_gives_503_and_rolls_back_the_blobs_already_written(int failAfter)
    {
        factory.SkipIfDockerUnavailable();
        var (broken, _, _) = Faulty(failAfter);
        await using var _1 = broken;
        var user = broken.EntraUser("storagefail" + failAfter);
        var runId = await user.CreateRunAsync();

        var response = await user.UploadAsync(runId, Road);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:storage-unavailable", (await response.ReadAsync()).GetProperty("type").GetString());
        Assert.Empty(await broken.BlobNamesAsync(BlobPaths.UserPrefix(await user.GetUserIdAsync())));
        Assert.Empty(await broken.TracksAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_failing_blob_delete_keeps_the_track_and_its_blobs_with_503_then_a_retry_succeeds()
    {
        factory.SkipIfDockerUnavailable();
        var (broken, blobs, _) = Faulty();
        await using var _1 = broken;
        var user = broken.EntraUser("deletefail");
        var userId = await user.GetUserIdAsync();
        var runId = await user.CreateRunAsync();
        var trackId = (await user.UploadOkAsync(runId)).GetProperty("id").GetGuid();

        blobs.FailDeletes = true;
        var failed = await user.Client.DeleteAsync(TrackTestSupport.TrackUri(runId, trackId), Ct);
        var runFailed = await user.Client.DeleteAsync(new Uri($"/api/runs/{runId}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.True(failed.Headers.RetryAfter is not null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, runFailed.StatusCode);
        Assert.Single(await broken.TracksAsync(userId));
        Assert.Equal(1, await broken.RunCountAsync(userId));
        Assert.Equal(2, (await broken.BlobNamesAsync(BlobPaths.UserPrefix(userId))).Count);
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(TrackTestSupport.TrackUri(runId, trackId), Ct)).StatusCode);

        blobs.FailDeletes = false;
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync(new Uri($"/api/runs/{runId}", UriKind.Relative), Ct)).StatusCode);
        Assert.Empty(await broken.BlobNamesAsync(BlobPaths.UserPrefix(userId)));
        Assert.Empty(await broken.TracksAsync(userId));
    }

    [Fact]
    public async Task A_failing_cleanup_after_a_committed_replace_still_answers_201_and_logs_TrackBlobOrphaned()
    {
        factory.SkipIfDockerUnavailable();
        var (broken, blobs, log) = Faulty();
        await using var _1 = broken;
        var user = broken.EntraUser("orphan");
        var userId = await user.GetUserIdAsync();
        var runId = await user.CreateRunAsync();
        var firstId = (await user.UploadOkAsync(runId)).GetProperty("id").GetGuid();

        blobs.FailDeletes = true;
        var replaced = await user.UploadAsync(runId, Road, replace: true);

        Assert.Equal(HttpStatusCode.Created, replaced.StatusCode);
        var newId = (await replaced.ReadAsync()).GetProperty("id").GetGuid();
        Assert.Equal([newId], (await broken.TracksAsync(userId)).Select(t => t.Id).ToArray());
        // The old blobs are orphaned (and logged), the new ones exist.
        var names = await broken.BlobNamesAsync(BlobPaths.UserPrefix(userId));
        Assert.Equal(4, names.Count);
        Assert.Contains(names, n => n.Contains(firstId.ToString("N"), StringComparison.Ordinal));
        Assert.Contains(log.Messages, m => m.Contains("TrackBlobOrphaned", StringComparison.Ordinal));
    }
}
