using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Features.Tracks;
using RunnersDiary.Api.Tests.Guests;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Tracks;

/// <summary>AC1, AC2, AC4, AC5 (plan section 1): the upload pipeline and the read/download/delete routes against SQL Server and Azurite.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class UploadTrackTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<IReadOnlyList<string>> BlobsOfAsync(TestActor actor) =>
        await factory.BlobNamesAsync(BlobPaths.UserPrefix(await actor.GetUserIdAsync()));

    /// <summary>The Cache-Control directives, order-independent (HttpClient re-serializes the header in its own order).</summary>
    private static string[] CacheDirectives(HttpResponseMessage response) =>
        [.. response.Headers.GetValues("Cache-Control").SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries)).Order(StringComparer.Ordinal)];

    private async Task<byte[]> ReadBlobAsync(string name)
    {
        await using var stream = await factory.Services.GetRequiredService<IBlobStore>().OpenReadAsync(name, Ct);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, Ct);
        return buffer.ToArray();
    }

    [Fact]
    public async Task A_recorded_track_is_stored_with_metrics_two_blobs_and_a_location_header()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("uploader");
        var runId = await user.CreateRunAsync();
        var bytes = TrackTestSupport.Fixture("road-5k.gpx");

        var response = await user.UploadAsync(runId, bytes, fileName: @"C:\Users\me\morning run.gpx");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var track = await response.ReadAsync();
        var trackId = track.GetProperty("id").GetGuid();
        Assert.Equal($"/api/runs/{runId}/tracks/{trackId}", response.Headers.Location?.ToString());
        Assert.Equal(runId, track.GetProperty("runId").GetGuid());
        Assert.Equal("RecordedTrack", track.GetProperty("kind").GetString());
        Assert.Equal("Processed", track.GetProperty("status").GetString());
        Assert.Equal("morning run.gpx", track.GetProperty("originalFileName").GetString());
        Assert.Equal(bytes.Length, track.GetProperty("sizeBytes").GetInt64());
        var summary = track.GetProperty("summary");
        Assert.InRange(summary.GetProperty("distanceMeters").GetDouble(), 4_975, 5_025);
        Assert.Equal(1_500, summary.GetProperty("elapsedSeconds").GetInt32());
        Assert.Equal(JsonValueKind.Number, summary.GetProperty("movingSeconds").ValueKind);
        Assert.Equal(50.0, summary.GetProperty("start").GetProperty("latitude").GetDouble(), 3);
        Assert.Equal("Europe/Warsaw", summary.GetProperty("startLocal").GetProperty("timeZoneId").GetString());
        Assert.Equal("2025-06-01", summary.GetProperty("startLocal").GetProperty("date").GetString());
        Assert.Equal("09:00", summary.GetProperty("startLocal").GetProperty("time").GetString());
        Assert.Equal(0, track.GetProperty("warnings").GetArrayLength());

        var userId = await user.GetUserIdAsync();
        Assert.Equal(
            [BlobPaths.TrackOriginal(userId, trackId), BlobPaths.TrackProcessed(userId, trackId)],
            (await BlobsOfAsync(user)).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(bytes, await ReadBlobAsync(BlobPaths.TrackOriginal(userId, trackId)));
        using var geoJson = JsonDocument.Parse(await ReadBlobAsync(BlobPaths.TrackProcessed(userId, trackId)));
        Assert.Equal("FeatureCollection", geoJson.RootElement.GetProperty("type").GetString());

        var run = await (await user.Client.GetAsync(new Uri($"/api/runs/{runId}", UriKind.Relative), Ct)).ReadAsync();
        var listed = Assert.Single(run.GetProperty("tracks").EnumerateArray());
        Assert.Equal(trackId, listed.GetProperty("id").GetGuid());
        Assert.Equal("morning run.gpx", listed.GetProperty("originalFileName").GetString());
        Assert.Equal(JsonValueKind.String, listed.GetProperty("createdAt").ValueKind);
        // Tracks never change the run itself.
        Assert.Equal(10_000, run.GetProperty("distanceMeters").GetDouble());
    }

    [Theory]
    [InlineData("route-only.gpx", "PlannedCourse", "routeConverted")]
    [InlineData("no-elevation.gpx", "RecordedTrack", "noElevation")]
    [InlineData("multi-segment.gpx", "RecordedTrack", "multipleSegments")]
    [InlineData("partial-time.gpx", "RecordedTrack", "timestampsIgnored")]
    public async Task Unusual_but_valid_files_are_accepted_with_warnings(string fixture, string kind, string warning)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("warned");
        var runId = await user.CreateRunAsync();

        var track = await user.UploadOkAsync(runId, fixture, kind);

        Assert.Equal(kind, track.GetProperty("kind").GetString());
        Assert.Contains(warning, track.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public async Task An_untimed_file_has_no_times_in_its_summary()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("untimed");
        var runId = await user.CreateRunAsync();

        var summary = (await user.UploadOkAsync(runId, "no-time.gpx")).GetProperty("summary");

        Assert.Equal(JsonValueKind.Null, summary.GetProperty("elapsedSeconds").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("startLocal").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("startUtc").ValueKind);
    }

    [Theory]
    [InlineData("xxe-file.gpx", "dtdProhibited")]
    [InlineData("xxe-http.gpx", "dtdProhibited")]
    [InlineData("billion-laughs.gpx", "dtdProhibited")]
    [InlineData("dtd-only.gpx", "dtdProhibited")]
    [InlineData("not-gpx-root.xml", "notGpx")]
    [InlineData("wrong-namespace.gpx", "notGpx")]
    [InlineData("malformed.gpx", "malformedXml")]
    [InlineData("empty.gpx", "unsupportedFormat")]
    [InlineData("gzip.gpx", "unsupportedFormat")]
    [InlineData("zip-as.gpx", "unsupportedFormat")]
    [InlineData("binary.gpx", "unsupportedFormat")]
    [InlineData("html-as.gpx", "dtdProhibited")]
    [InlineData("deep-nesting.gpx", "tooComplex")]
    [InlineData("invalid-lat.gpx", "invalidCoordinate")]
    [InlineData("nan-lon.gpx", "invalidCoordinate")]
    [InlineData("one-point.gpx", "tooFewPoints")]
    [InlineData("no-points.gpx", "tooFewPoints")]
    public async Task Rejected_files_give_400_invalid_gpx_and_leave_no_row_and_no_blob(string fixture, string code)
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("attacker");
        var runId = await user.CreateRunAsync();

        var response = await user.UploadAsync(runId, TrackTestSupport.Fixture(fixture));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync();
        Assert.Equal("urn:runnersdiary:problem:invalid-gpx", problem.GetProperty("type").GetString());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        var message = Assert.Single(problem.GetProperty("errors").GetProperty("file").EnumerateArray()).GetString();
        Assert.Equal(GpxRejection.For(Enum.Parse<GpxRejectionCode>(code, ignoreCase: true)).Message, message);
        var body = problem.GetRawText();
        Assert.DoesNotContain("passwd", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("root:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("lol", body, StringComparison.Ordinal);
        Assert.Empty(await BlobsOfAsync(user));
        Assert.Empty(await factory.TracksAsync(await user.GetUserIdAsync()));
    }

    [Fact]
    public async Task A_file_name_without_the_gpx_extension_is_rejected_with_wrongExtension()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("extension");
        var runId = await user.CreateRunAsync();

        var response = await user.UploadAsync(runId, TrackTestSupport.Fixture("road-5k.gpx"), fileName: "route.txt");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("wrongExtension", (await response.ReadAsync()).GetProperty("code").GetString());
        Assert.Empty(await BlobsOfAsync(user));
    }

    [Fact]
    public async Task Missing_or_invalid_form_fields_give_a_validation_problem()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("form");
        var runId = await user.CreateRunAsync();
        var bytes = TrackTestSupport.Fixture("road-5k.gpx");

        var noKind = await user.Client.UploadAsync(runId, bytes, kind: null);
        var badKind = await user.Client.UploadAsync(runId, bytes, kind: "1");
        var lowerKind = await user.Client.UploadAsync(runId, bytes, kind: "recordedtrack");
        var noFile = await user.Client.UploadAsync(runId, bytes, fileName: null);
        using var badReplaceForm = new MultipartFormDataContent { { new StringContent("RecordedTrack"), "kind" }, { new StringContent("maybe"), "replace" } };
        badReplaceForm.Add(new ByteArrayContent(bytes), "file", "x.gpx");
        var maybe = await user.Client.PostAsync(TrackTestSupport.TracksUri(runId), badReplaceForm, Ct);

        Assert.Contains("kind", (await Errors(noKind)));
        Assert.Contains("kind", (await Errors(badKind)));
        Assert.Contains("kind", (await Errors(lowerKind)));
        Assert.Contains("file", (await Errors(noFile)));
        Assert.Contains("replace", (await Errors(maybe)));

        static async Task<string[]> Errors(HttpResponseMessage r)
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            return [.. (await r.ReadAsync()).GetProperty("errors").EnumerateObject().Select(p => p.Name)];
        }
    }

    [Fact]
    public async Task A_non_multipart_body_is_415_and_two_files_are_a_validation_error()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("shape");
        var runId = await user.CreateRunAsync();

        var json = await user.Client.PostAsJsonAsync(TrackTestSupport.TracksUri(runId), new { kind = "RecordedTrack" }, Ct);
        using var two = new MultipartFormDataContent { { new StringContent("RecordedTrack"), "kind" } };
        two.Add(new ByteArrayContent(TrackTestSupport.Fixture("road-5k.gpx")), "file", "a.gpx");
        two.Add(new ByteArrayContent(TrackTestSupport.Fixture("road-5k.gpx")), "other", "b.gpx");
        var twoFiles = await user.Client.PostAsync(TrackTestSupport.TracksUri(runId), two, Ct);

        var urlEncoded = await user.Client.PostAsync(
            TrackTestSupport.TracksUri(runId), new FormUrlEncodedContent(new Dictionary<string, string> { ["kind"] = "RecordedTrack", ["file"] = "x" }), Ct);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, json.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, urlEncoded.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, twoFiles.StatusCode);
        Assert.Empty(await BlobsOfAsync(user));
    }

    [Fact]
    public async Task Files_over_the_limit_are_413_for_users_and_guests_and_other_routes_keep_the_64_KB_limit()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("big");
        var guest = await factory.GuestAsync("bigguest");
        var userRun = await user.CreateRunAsync();
        var guestRun = await guest.CreateRunAsync();

        var userTooBig = await user.UploadAsync(userRun, new byte[21 * 1024 * 1024]);
        var guestTooBig = await guest.UploadAsync(guestRun, new byte[6 * 1024 * 1024]);
        // Between the guest limit and limit + multipart allowance: refused from the file length once the form is read.
        var guestJustOver = await guest.UploadAsync(guestRun, new byte[(5 * 1024 * 1024) + 1024]);
        // A guest-sized file that is not GPX gets as far as the parser (the size limit is not what stops it).
        var guestOk = await guest.UploadAsync(guestRun, new byte[4 * 1024 * 1024]);
        var otherRoute = await user.Client.PostAsync(
            new Uri("/api/runs", UriKind.Relative), new ByteArrayContent(new byte[70 * 1024]) { Headers = { ContentType = new("application/json") } }, Ct);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, userTooBig.StatusCode);
        Assert.Equal("application/problem+json", userTooBig.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, guestTooBig.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, guestJustOver.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, guestOk.StatusCode);
        Assert.Equal("unsupportedFormat", (await guestOk.ReadAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, otherRoute.StatusCode);
        Assert.Empty(await BlobsOfAsync(user));
        Assert.Empty(await BlobsOfAsync(guest));
    }

    [Fact]
    public async Task A_second_track_of_the_same_kind_is_409_unless_replace_is_set_and_replace_swaps_the_blobs()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("replacer");
        var userId = await user.GetUserIdAsync();
        var runId = await user.CreateRunAsync();
        var first = await user.UploadOkAsync(runId);
        var firstId = first.GetProperty("id").GetGuid();

        var conflict = await user.UploadAsync(runId, TrackTestSupport.Fixture("hill-hysteresis.gpx"));
        var explicitFalse = await user.UploadAsync(runId, TrackTestSupport.Fixture("hill-hysteresis.gpx"), replace: false);
        var other = await user.UploadAsync(runId, TrackTestSupport.Fixture("route-only.gpx"), kind: "PlannedCourse");
        var replaced = await user.UploadAsync(runId, TrackTestSupport.Fixture("hill-hysteresis.gpx"), fileName: "hills.gpx", replace: true);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("urn:runnersdiary:problem:track-exists", (await conflict.ReadAsync()).GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.Conflict, explicitFalse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replaced.StatusCode);
        var newId = (await replaced.ReadAsync()).GetProperty("id").GetGuid();
        Assert.NotEqual(firstId, newId);
        var tracks = await factory.TracksAsync(userId);
        Assert.Equal(2, tracks.Count);
        Assert.DoesNotContain(tracks, t => t.Id == firstId);
        var blobs = await BlobsOfAsync(user);
        Assert.DoesNotContain(blobs, b => b.Contains(firstId.ToString("N"), StringComparison.Ordinal));
        Assert.Equal(4, blobs.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync(TrackTestSupport.TrackUri(runId, firstId), Ct)).StatusCode);
    }

    [Fact]
    public async Task Replace_without_an_existing_track_just_creates_one()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("replacefirst");
        var runId = await user.CreateRunAsync();

        var response = await user.UploadAsync(runId, TrackTestSupport.Fixture("road-5k.gpx"), replace: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Rest_days_and_unknown_runs_are_refused_without_storing_anything()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("rest");
        var restRun = await user.CreateRunAsync(change: r =>
        {
            r["runTypeId"] = 41;
            r["distanceMeters"] = null;
            r["durationSeconds"] = null;
            r["rpe"] = null;
        });

        var rest = await user.UploadAsync(restRun, TrackTestSupport.Fixture("road-5k.gpx"));
        var missing = await user.UploadAsync(Guid.NewGuid(), TrackTestSupport.Fixture("road-5k.gpx"));

        Assert.Equal(HttpStatusCode.BadRequest, rest.StatusCode);
        Assert.Contains("runId", (await rest.ReadAsync()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(await BlobsOfAsync(user));
    }

    [Fact]
    public async Task A_recorded_track_is_allowed_on_a_planned_run()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("planned");
        var runId = await user.CreateRunAsync(change: r => { r["status"] = "Planned"; r["rpe"] = null; });

        var track = await user.UploadOkAsync(runId);

        Assert.Equal("RecordedTrack", track.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Get_track_returns_the_dto_and_geojson_is_streamed_with_cache_headers_and_an_etag()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("reader");
        var runId = await user.CreateRunAsync();
        var created = await user.UploadOkAsync(runId, "hill-hysteresis.gpx");
        var trackId = created.GetProperty("id").GetGuid();

        var get = await user.Client.GetAsync(TrackTestSupport.TrackUri(runId, trackId), Ct);
        var geo = await user.Client.GetAsync(TrackTestSupport.TrackUri(runId, trackId, "geojson"), Ct);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, TrackTestSupport.TrackUri(runId, trackId, "geojson"));
        conditional.Headers.TryAddWithoutValidation("If-None-Match", geo.Headers.ETag!.ToString());
        var notModified = await user.Client.SendAsync(conditional, Ct);
        using var stale = new HttpRequestMessage(HttpMethod.Get, TrackTestSupport.TrackUri(runId, trackId, "geojson"));
        stale.Headers.TryAddWithoutValidation("If-None-Match", "\"something-else\"");
        var staleResponse = await user.Client.SendAsync(stale, Ct);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(created.GetRawText(), (await get.ReadAsync()).GetRawText());
        Assert.Equal(HttpStatusCode.OK, geo.StatusCode);
        Assert.Equal("application/geo+json", geo.Content.Headers.ContentType?.MediaType);
        // M2: revalidate on every use (no disk-cached geometry after sign-out); the ETag still gives cheap 304s.
        Assert.Equal(["no-cache", "private"], CacheDirectives(geo));
        Assert.Equal(["no-cache", "private"], CacheDirectives(notModified));
        Assert.NotNull(geo.Headers.ETag);
        var doc = await geo.ReadAsync();
        Assert.Equal("FeatureCollection", doc.GetProperty("type").GetString());
        Assert.Equal(1, doc.GetProperty("formatVersion").GetInt32());
        Assert.Equal(3, doc.GetProperty("features").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal(geo.Headers.ETag, notModified.Headers.ETag);
        Assert.Equal(HttpStatusCode.OK, staleResponse.StatusCode);
    }

    [Fact]
    public async Task The_original_file_downloads_as_a_hardened_attachment()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("downloader");
        var runId = await user.CreateRunAsync();
        var bytes = TrackTestSupport.Fixture("road-5k.gpx");
        var trackId = (await (await user.UploadAsync(runId, bytes, fileName: "../../etc/my run.gpx")).ReadAsync()).GetProperty("id").GetGuid();

        var response = await user.Client.GetAsync(TrackTestSupport.TrackUri(runId, trackId, "gpx"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("application/gpx+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment; filename=\"my run.gpx\"; filename*=UTF-8''my%20run.gpx", string.Join(",", response.Content.Headers.GetValues("Content-Disposition")));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(["no-store", "private"], CacheDirectives(response));
    }

    [Theory]
    [InlineData("morning.gpx", "attachment; filename=\"morning.gpx\"; filename*=UTF-8''morning.gpx")]
    [InlineData("m\u00f3j bieg.gpx", "attachment; filename=\"m_j bieg.gpx\"; filename*=UTF-8''m%C3%B3j%20bieg.gpx")]
    [InlineData("50%;x\\y.gpx", "attachment; filename=\"50__x_y.gpx\"; filename*=UTF-8''50%25%3Bx%5Cy.gpx")]
    [InlineData("a'(b)*.gpx", "attachment; filename=\"a'(b)*.gpx\"; filename*=UTF-8''a%27%28b%29%2A.gpx")]
    public void The_content_disposition_is_ascii_safe_with_an_rfc_5987_name(string name, string expected) =>
        Assert.Equal(expected, DownloadTrackGpx.AttachmentHeader(name));

    [Fact]
    public async Task Deleting_a_track_removes_its_blobs_and_row_and_repeating_is_404()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("deleter");
        var userId = await user.GetUserIdAsync();
        var runId = await user.CreateRunAsync();
        var trackId = (await user.UploadOkAsync(runId)).GetProperty("id").GetGuid();
        var keep = (await user.UploadOkAsync(runId, "route-only.gpx", "PlannedCourse")).GetProperty("id").GetGuid();

        var deleted = await user.Client.DeleteAsync(TrackTestSupport.TrackUri(runId, trackId), Ct);
        var again = await user.Client.DeleteAsync(TrackTestSupport.TrackUri(runId, trackId), Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal([keep], (await factory.TracksAsync(userId)).Select(t => t.Id).ToArray());
        Assert.All(await BlobsOfAsync(user), b => Assert.Contains(keep.ToString("N"), b, StringComparison.Ordinal));
        Assert.Equal(2, (await BlobsOfAsync(user)).Count);
    }

    [Fact]
    public async Task Deleting_a_run_removes_the_blobs_of_all_its_tracks()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("rundeleter");
        var userId = await user.GetUserIdAsync();
        var runId = await user.CreateRunAsync();
        var otherRun = await user.CreateRunAsync("2026-11-13");
        await user.UploadOkAsync(runId);
        await user.UploadOkAsync(runId, "route-only.gpx", "PlannedCourse");
        await user.UploadOkAsync(otherRun);

        var deleted = await user.Client.DeleteAsync(new Uri($"/api/runs/{runId}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await factory.TracksAsync(userId);
        Assert.Equal(otherRun, Assert.Single(remaining).RunId);
        Assert.Equal(2, (await BlobsOfAsync(user)).Count);
    }

    [Fact]
    public async Task Deleting_a_run_without_tracks_still_works_and_a_foreign_or_missing_run_is_404()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("plain");
        var runId = await user.CreateRunAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync(new Uri($"/api/runs/{runId}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.DeleteAsync(new Uri($"/api/runs/{Guid.NewGuid()}", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task A_pending_track_from_before_M4_is_listed_without_a_summary_and_has_no_geojson()
    {
        factory.SkipIfDockerUnavailable();
        var user = factory.EntraUser("legacy");
        var userId = await user.GetUserIdAsync();
        var runId = await user.CreateRunAsync();
        var trackId = Guid.NewGuid();
        await factory.WithDbAsync(async db =>
        {
            db.ChangeTracker.Clear();
            var pending = GpxTrack.CreatePending(
                trackId, runId, userId, GpxTrackKind.RecordedTrack, "old.gpx", 10, new byte[32], BlobPaths.TrackOriginal(userId, trackId), DateTimeOffset.UtcNow);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO GpxTracks (Id, RunId, UserId, Kind, OriginalFileName, SizeBytes, Sha256, RawBlobName, Status, CreatedAt, WarningFlags) VALUES ({pending.Id}, {runId}, {userId}, {(byte)pending.Kind}, {pending.OriginalFileName}, {pending.SizeBytes}, {pending.Sha256}, {pending.RawBlobName}, 0, {pending.CreatedAt}, 0)",
                Ct);
            return 0;
        });

        var get = await user.Client.GetAsync(TrackTestSupport.TrackUri(runId, trackId), Ct);
        var geo = await user.Client.GetAsync(TrackTestSupport.TrackUri(runId, trackId, "geojson"), Ct);
        var download = await user.Client.GetAsync(TrackTestSupport.TrackUri(runId, trackId, "gpx"), Ct);
        var run = await (await user.Client.GetAsync(new Uri($"/api/runs/{runId}", UriKind.Relative), Ct)).ReadAsync();

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var dto = await get.ReadAsync();
        Assert.Equal("Pending", dto.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("summary").ValueKind);
        Assert.Equal(0, dto.GetProperty("warnings").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, geo.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal("Pending", Assert.Single(run.GetProperty("tracks").EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_fresh_guests_demo_tracks_are_readable_through_the_endpoints()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("demo");
        var tracks = await factory.TracksAsync(guest.UserId!.Value);
        var track = tracks.First(t => t.Kind == GpxTrackKind.RecordedTrack);

        var get = await (await guest.Client.GetAsync(TrackTestSupport.TrackUri(track.RunId, track.Id), Ct)).ReadAsync();
        var geo = await guest.Client.GetAsync(TrackTestSupport.TrackUri(track.RunId, track.Id, "geojson"), Ct);
        var gpx = await guest.Client.GetAsync(TrackTestSupport.TrackUri(track.RunId, track.Id, "gpx"), Ct);

        Assert.Equal("Processed", get.GetProperty("status").GetString());
        Assert.True(get.GetProperty("summary").GetProperty("distanceMeters").GetDouble() > 5_000);
        Assert.Equal(HttpStatusCode.OK, geo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, gpx.StatusCode);
        Assert.Equal(track.OriginalFileName, new ContentDispositionName(gpx).Name);
    }

    private sealed class ContentDispositionName(HttpResponseMessage response)
    {
        public string? Name { get; } = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
    }
}
