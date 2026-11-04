using System.Net.Http.Json;
using System.Text.Json;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Tracks;

/// <summary>Fixtures from <c>tests/fixtures/gpx</c> and helpers to create runs and upload files through the real endpoints.</summary>
internal static class TrackTestSupport
{
    private static readonly string FixtureDirectory = FindUp("tests", "fixtures", "gpx");

    public static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(FixtureDirectory, name));

    public static Uri TracksUri(Guid runId) => new($"/api/runs/{runId}/tracks", UriKind.Relative);

    public static Uri TrackUri(Guid runId, Guid trackId, string? suffix = null) =>
        new($"/api/runs/{runId}/tracks/{trackId}{(suffix is null ? string.Empty : "/" + suffix)}", UriKind.Relative);

    /// <summary>Creates a completed easy run and returns its id.</summary>
    public static async Task<Guid> CreateRunAsync(this TestActor actor, string date = "2026-11-12", Action<Dictionary<string, object?>>? change = null)
    {
        var response = await actor.Client.PostAsJsonAsync(new Uri("/api/runs", UriKind.Relative), Json.Run(date, change), TestContext.Current.CancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync()).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> UploadAsync(
        this TestActor actor, Guid runId, byte[] file, string kind = "RecordedTrack", string fileName = "run.gpx", bool? replace = null) =>
        actor.Client.UploadAsync(runId, file, kind, fileName, replace);

    public static async Task<HttpResponseMessage> UploadAsync(
        this HttpClient client, Guid runId, byte[] file, string? kind = "RecordedTrack", string? fileName = "run.gpx", bool? replace = null)
    {
        using var form = new MultipartFormDataContent();
        if (kind is not null)
        {
            form.Add(new StringContent(kind), "kind");
        }

        if (replace is { } r)
        {
            form.Add(new StringContent(r ? "true" : "false"), "replace");
        }

        if (fileName is not null)
        {
            var part = new ByteArrayContent(file);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/gpx+xml");
            form.Add(part, "file", fileName);
        }

        return await client.PostAsync(TracksUri(runId), form, TestContext.Current.CancellationToken);
    }

    /// <summary>Uploads a fixture that must succeed and returns the track JSON.</summary>
    public static async Task<JsonElement> UploadOkAsync(this TestActor actor, Guid runId, string fixture = "road-5k.gpx", string kind = "RecordedTrack")
    {
        var response = await actor.UploadAsync(runId, Fixture(fixture), kind);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync();
    }

    private static string FindUp(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(string.Join('/', parts));
    }
}
