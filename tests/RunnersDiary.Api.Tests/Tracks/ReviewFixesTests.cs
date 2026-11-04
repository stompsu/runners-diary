using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Features.Tracks;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Tracks;

/// <summary>Regression tests for the M4 review findings (M1, M3, L1, L3, L4 and the 404-on-vanished-track nit).</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class ReviewFixesTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly byte[] Road = TrackTestSupport.Fixture("road-5k.gpx");

    private (Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory, FaultyBlobStore Blobs) Faulty(Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configure = null)
    {
        var broken = factory.WithWebHostBuilder(b =>
        {
            configure?.Invoke(b);
            b.ConfigureServices(services => services.AddSingleton<IBlobStore>(sp =>
                new FaultyBlobStore(new AzureBlobStore(sp.GetRequiredService<Azure.Storage.Blobs.BlobServiceClient>()))));
        });
        return (broken, (FaultyBlobStore)broken.Services.GetRequiredService<IBlobStore>());
    }

    [Fact]
    public async Task M1_a_cancellation_after_the_first_blob_was_written_rolls_the_blobs_back()
    {
        factory.SkipIfDockerUnavailable();
        var (broken, blobs) = Faulty();
        await using var _ = broken;
        blobs.CancelUploadsAfter = 1;
        var user = broken.EntraUser("m1cancel");
        var userId = await user.GetUserIdAsync();
        var runId = await user.CreateRunAsync();

        // The request fails (cancelled or 500, depending on the host); what matters is that nothing is left behind.
        await Record.ExceptionAsync(() => user.UploadAsync(runId, Road));

        Assert.Single(blobs.Uploaded);
        Assert.Empty(await broken.BlobNamesAsync(BlobPaths.UserPrefix(userId)));
        Assert.Empty(await broken.TracksAsync(userId));
    }

    [Fact]
    public async Task M3_the_upload_policy_replaces_the_general_limiter_for_its_route_only()
    {
        factory.SkipIfDockerUnavailable();
        await using var limited = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:General:TokenLimit", "2");
            b.UseSetting("RateLimiting:General:TokensPerPeriod", "1");
            b.UseSetting("RateLimiting:General:ReplenishmentPeriod", "01:00:00");
            b.UseSetting("RateLimiting:GpxUpload:UserPermitLimit", "50");
        });
        var user = limited.EntraUser("m3policy");
        var runId = await user.CreateRunAsync();
        var runTypes = new Uri("/api/run-types", UriKind.Relative);

        // The run creation above took one general token; this takes the second.
        Assert.Equal(HttpStatusCode.OK, (await user.Client.GetAsync(runTypes, Ct)).StatusCode);
        // Uploads are governed by "gpx-upload" alone: they neither need nor consume general tokens.
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await user.UploadAsync(runId, Road, replace: true)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await user.Client.GetAsync(runTypes, Ct)).StatusCode);
    }

    [Fact]
    public async Task L1_a_track_uploaded_while_its_run_is_being_deleted_does_not_leave_blobs()
    {
        factory.SkipIfDockerUnavailable();
        var (broken, blobs) = Faulty();
        await using var _ = broken;
        var user = broken.EntraUser("l1race");
        var userId = await user.GetUserIdAsync();
        var runId = await user.CreateRunAsync();
        await user.UploadOkAsync(runId);
        blobs.BeforeFirstDelete = async () =>
            Assert.Equal(HttpStatusCode.Created, (await user.UploadAsync(runId, Road, kind: "PlannedCourse")).StatusCode);

        var deleted = await user.Client.DeleteAsync(new Uri($"/api/runs/{runId}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(4, blobs.Uploaded.Count);
        Assert.Empty(await broken.BlobNamesAsync(BlobPaths.UserPrefix(userId)));
        Assert.Empty(await broken.TracksAsync(userId));
        Assert.Equal(0, await broken.RunCountAsync(userId));
    }

    [Fact]
    public async Task L3_ownership_is_answered_before_the_form_is_read()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("l3owner");
        var other = factory.EntraUser("l3other");
        var foreignRun = await other.CreateRunAsync();

        // No kind and no file: reading the form first would give 400; the owner check comes first.
        var foreign = await user.Client.UploadAsync(foreignRun, [], kind: null, fileName: null);
        var missing = await user.Client.UploadAsync(Guid.NewGuid(), [], kind: null, fileName: null);

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task L3_a_reached_track_cap_is_answered_before_the_form_is_read_for_a_run_without_a_track()
    {
        factory.SkipIfDockerUnavailable();
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("GpxLimits:UserMaxTracks", "1"));
        var user = limited.EntraUser("l3cap");
        var first = await user.CreateRunAsync();
        var second = await user.CreateRunAsync("2026-11-13");
        await user.UploadOkAsync(first);

        var response = await user.Client.UploadAsync(second, [], kind: null, fileName: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:track-limit", (await response.ReadAsync()).GetProperty("type").GetString());
    }

    [Fact]
    public async Task L4_concurrent_uploads_cannot_exceed_the_track_cap()
    {
        factory.SkipIfDockerUnavailable();
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("GpxLimits:UserMaxTracks", "1"));
        var user = limited.EntraUser("l4race");
        var userId = await user.GetUserIdAsync();
        var runs = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            runs.Add(await user.CreateRunAsync($"2026-11-{10 + i}"));
        }

        var responses = await Task.WhenAll(runs.Select(r => user.UploadAsync(r, Road)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.True(r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable));
        Assert.Single(await limited.TracksAsync(userId));
        Assert.Equal(2, (await limited.BlobNamesAsync(BlobPaths.UserPrefix(userId))).Count);
    }

    [Fact]
    public async Task A_replace_whose_track_was_deleted_meanwhile_is_404_and_leaves_no_blobs()
    {
        factory.SkipIfDockerUnavailable();
        var (broken, blobs) = Faulty();
        await using var _ = broken;
        var user = broken.EntraUser("replacegone");
        var userId = await user.GetUserIdAsync();
        var runId = await user.CreateRunAsync();
        var trackId = (await user.UploadOkAsync(runId)).GetProperty("id").GetGuid();
        blobs.BeforeFirstUpload = async () =>
            Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync(TrackTestSupport.TrackUri(runId, trackId), Ct)).StatusCode);

        var response = await user.UploadAsync(runId, Road, replace: true);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await broken.TracksAsync(userId));
        Assert.Empty(await broken.BlobNamesAsync(BlobPaths.UserPrefix(userId)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void L3_the_request_body_limit_is_the_callers_own_plus_framing(bool guest)
    {
        var limits = new RunnersDiary.Infrastructure.Gpx.GpxLimitsOptions();
        var context = new DefaultHttpContext();
        var feature = new FakeBodySizeFeature { MaxRequestBodySize = limits.MaxFileBytes + TracksEndpoints.MultipartOverheadBytes };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        UploadTrack.ApplyBodyLimit(context, limits.MaxFileBytesFor(guest));

        Assert.Equal(limits.MaxFileBytesFor(guest) + TracksEndpoints.MultipartOverheadBytes, feature.MaxRequestBodySize);
        Assert.Equal(guest, feature.MaxRequestBodySize < limits.MaxFileBytes);
    }

    [Fact]
    public void L3_a_read_only_body_size_feature_is_left_alone()
    {
        var context = new DefaultHttpContext();
        var feature = new FakeBodySizeFeature { MaxRequestBodySize = 123, IsReadOnly = true };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        UploadTrack.ApplyBodyLimit(context, 5);

        Assert.Equal(123, feature.MaxRequestBodySize);
    }

    private sealed class FakeBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; set; }

        public long? MaxRequestBodySize { get; set; }
    }
}
