using System.Net;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Api.Tests.Tracks;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Isolation;

/// <summary>
/// AC4 (plan section 1): every track route, for every ordered pair of callers (two guests, two Entra users, guest vs user both ways),
/// answers 404 for another caller's runs and tracks, indistinguishable from missing ones, and never touches the owner's data or blobs.
/// </summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class TrackIsolationTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<TestActor> ActorAsync(string kind) => kind.StartsWith("guest", StringComparison.Ordinal)
        ? await factory.GuestAsync(kind + "-" + Guid.NewGuid().ToString("N")[..6])
        : factory.EntraUser(kind + "-" + Guid.NewGuid().ToString("N")[..6]);

    private static async Task<string> Shape(HttpResponseMessage response)
    {
        var json = await response.ReadAsync();
        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType?.MediaType}|{json.GetProperty("title").GetString()?.Replace("Run", "X", StringComparison.Ordinal).Replace("Track", "X", StringComparison.Ordinal)}|{json.GetProperty("type").GetString()}";
    }

    [Theory]
    [MemberData(nameof(IsolationMatrixTests.Pairs), MemberType = typeof(IsolationMatrixTests))]
    public async Task Intruder_cannot_read_download_upload_to_or_delete_the_owners_tracks(string ownerKind, string intruderKind)
    {
        factory.SkipIfDockerUnavailable();
        var owner = await ActorAsync(ownerKind);
        var intruder = await ActorAsync(intruderKind);
        var ownerId = await owner.GetUserIdAsync();
        var intruderId = await intruder.GetUserIdAsync();

        var ownerRun = await owner.CreateRunAsync();
        var ownerTrack = (await owner.UploadOkAsync(ownerRun)).GetProperty("id").GetGuid();
        var intruderRun = await intruder.CreateRunAsync();
        var intruderTrack = (await intruder.UploadOkAsync(intruderRun)).GetProperty("id").GetGuid();
        var ownerBlobs = (await factory.BlobNamesAsync(BlobPaths.UserPrefix(ownerId))).Order(StringComparer.Ordinal).ToArray();
        var intruderBlobs = (await factory.BlobNamesAsync(BlobPaths.UserPrefix(intruderId))).Order(StringComparer.Ordinal).ToArray();

        // (foreign run, foreign track), (foreign run, missing track), (own run, foreign track), (foreign run, own track), (missing, missing)
        var combos = new[]
        {
            (ownerRun, ownerTrack), (ownerRun, Guid.NewGuid()), (intruderRun, ownerTrack), (ownerRun, intruderTrack), (Guid.NewGuid(), Guid.NewGuid()),
        };
        var missing = (Guid.NewGuid(), Guid.NewGuid());

        foreach (var (runId, trackId) in combos)
        {
            foreach (var suffix in new string?[] { null, "geojson", "gpx" })
            {
                var response = await intruder.Client.GetAsync(TrackTestSupport.TrackUri(runId, trackId, suffix), Ct);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                var baseline = await intruder.Client.GetAsync(TrackTestSupport.TrackUri(missing.Item1, missing.Item2, suffix), Ct);
                Assert.Equal(await Shape(baseline), await Shape(response));
            }

            Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.DeleteAsync(TrackTestSupport.TrackUri(runId, trackId), Ct)).StatusCode);
        }

        // Uploading to (or replacing on) a run you do not own is 404 and writes nothing for anyone.
        var upload = await intruder.UploadAsync(ownerRun, TrackTestSupport.Fixture("road-5k.gpx"), replace: true);
        var uploadPlanned = await intruder.UploadAsync(ownerRun, TrackTestSupport.Fixture("route-only.gpx"), "PlannedCourse");
        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, uploadPlanned.StatusCode);
        // Deleting a run you do not own is 404 and leaves its blobs.
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.DeleteAsync(new Uri($"/api/runs/{ownerRun}", UriKind.Relative), Ct)).StatusCode);

        Assert.Equal(ownerBlobs, (await factory.BlobNamesAsync(BlobPaths.UserPrefix(ownerId))).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(intruderBlobs, (await factory.BlobNamesAsync(BlobPaths.UserPrefix(intruderId))).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(ownerTrack, Assert.Single(await factory.TracksAsync(ownerId)).Id);
        Assert.Equal(intruderTrack, Assert.Single(await factory.TracksAsync(intruderId)).Id);

        // The owner still sees everything, and the intruder's own track still works.
        foreach (var suffix in new string?[] { null, "geojson", "gpx" })
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync(TrackTestSupport.TrackUri(ownerRun, ownerTrack, suffix), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await intruder.Client.GetAsync(TrackTestSupport.TrackUri(intruderRun, intruderTrack, suffix), Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task A_real_guests_demo_tracks_are_invisible_to_an_entra_user_and_to_another_guest()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("demoowner");
        var otherGuest = await factory.GuestSessionAsync("demointruder");
        var user = factory.EntraUser("demouser");
        var track = (await factory.TracksAsync(guest.UserId!.Value)).First();
        var blobsBefore = await factory.BlobNamesAsync(BlobPaths.UserPrefix(guest.UserId.Value));

        foreach (var intruder in new[] { user, otherGuest })
        {
            foreach (var suffix in new string?[] { null, "geojson", "gpx" })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.GetAsync(TrackTestSupport.TrackUri(track.RunId, track.Id, suffix), Ct)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.DeleteAsync(TrackTestSupport.TrackUri(track.RunId, track.Id), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await intruder.Client.DeleteAsync(new Uri($"/api/runs/{track.RunId}", UriKind.Relative), Ct)).StatusCode);
        }

        Assert.Equal(blobsBefore.Count, (await factory.BlobNamesAsync(BlobPaths.UserPrefix(guest.UserId.Value))).Count);
        Assert.Equal(HttpStatusCode.OK, (await guest.Client.GetAsync(TrackTestSupport.TrackUri(track.RunId, track.Id, "geojson"), Ct)).StatusCode);
    }
}
