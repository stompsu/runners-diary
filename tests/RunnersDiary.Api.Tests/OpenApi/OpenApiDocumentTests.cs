using System.Text.Json;

namespace RunnersDiary.Api.Tests.OpenApi;

/// <summary>Checks the committed, build-time OpenAPI document that web/ generates its client from (ADR 0002).</summary>
public sealed class OpenApiDocumentTests
{
    private static JsonDocument LoadDocument()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RunnersDiary.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir.FullName, "src", "RunnersDiary.Api", "openapi", "runnersdiary-api.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Document_IsOpenApi31_AndContainsApiHealth()
    {
        using var doc = LoadDocument();
        var root = doc.RootElement;

        Assert.StartsWith("3.1", root.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        var get = root.GetProperty("paths").GetProperty("/api/health").GetProperty("get");
        Assert.Equal("getApiHealth", get.GetProperty("operationId").GetString());
    }

    [Fact]
    public void Document_ExcludesInfrastructureHealthEndpoints()
    {
        using var doc = LoadDocument();
        var paths = doc.RootElement.GetProperty("paths");

        Assert.False(paths.TryGetProperty("/health", out _));
        Assert.False(paths.TryGetProperty("/alive", out _));
    }

    [Fact]
    public void Document_ContainsEveryM2OperationId_WithBearerSecurityOnProtectedOnes()
    {
        using var doc = LoadDocument();
        var operations = doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(m => (Path: p.Name, Operation: m.Value)))
            .ToDictionary(x => x.Operation.GetProperty("operationId").GetString()!, x => x);

        string[] protectedIds = ["getMe", "updateMe", "listRunTypes", "listRuns", "getRun", "createRun", "updateRun", "deleteRun", "getCalendarMonth", "getInsightsReport"];
        foreach (var id in protectedIds)
        {
            Assert.True(operations.ContainsKey(id), id);
            Assert.True(operations[id].Operation.TryGetProperty("security", out var security), id + " has no security requirement");
            Assert.Contains("Bearer", security[0].EnumerateObject().Select(p => p.Name));
        }

        Assert.False(operations["getApiHealth"].Operation.TryGetProperty("security", out _));
        Assert.False(operations["getClientConfig"].Operation.TryGetProperty("security", out _));
        Assert.Equal("/api/client-config", operations["getClientConfig"].Path);
        var scheme = doc.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    [Fact]
    public void Document_DescribesTimeOnlyAsHHmm_AndKeepsNumbersStrict()
    {
        using var doc = LoadDocument();
        var request = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("RunWriteRequest").GetProperty("properties");

        Assert.Equal(@"^\d{2}:\d{2}$", request.GetProperty("startTime").GetProperty("pattern").GetString());
        Assert.False(request.GetProperty("startTime").TryGetProperty("format", out _));
        Assert.DoesNotContain("string", request.GetProperty("runTypeId").GetProperty("type").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Document_ContainsTheM4TrackAndGeocodingOperations_WithTheMultipartUpload()
    {
        using var doc = LoadDocument();
        var paths = doc.RootElement.GetProperty("paths");
        var operations = paths.EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(m => (Path: p.Name, Operation: m.Value)))
            .ToDictionary(x => x.Operation.GetProperty("operationId").GetString()!, x => x);

        string[] ids = ["uploadTrack", "getTrack", "getTrackGeoJson", "downloadTrackGpx", "deleteTrack", "searchPlaces"];
        foreach (var id in ids)
        {
            Assert.True(operations.ContainsKey(id), id);
            Assert.True(operations[id].Operation.TryGetProperty("security", out _), id + " has no security requirement");
        }

        var upload = operations["uploadTrack"].Operation;
        Assert.Equal("/api/runs/{runId}/tracks", operations["uploadTrack"].Path);
        var schemaRef = upload.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema").GetProperty("$ref").GetString();
        Assert.EndsWith("/UploadTrackForm", schemaRef, StringComparison.Ordinal);
        var form = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("UploadTrackForm").GetProperty("properties");
        Assert.Equal(["kind", "file", "replace"], form.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.True(operations["getTrackGeoJson"].Operation.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("application/geo+json", out _));
    }
}
