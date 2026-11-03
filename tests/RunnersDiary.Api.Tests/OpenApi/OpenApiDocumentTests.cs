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
}
