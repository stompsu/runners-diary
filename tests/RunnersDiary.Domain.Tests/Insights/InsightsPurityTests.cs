namespace RunnersDiary.Domain.Tests.Insights;

/// <summary>AC2: the metrics have no I/O and no clock; "today" is always an input.</summary>
public sealed class InsightsPurityTests
{
    private static readonly string[] Forbidden =
    [
        "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "DateOnly.FromDateTime(DateTime", "TimeProvider",
        "TimeZoneInfo", "System.IO", "File.", "Console.", "HttpClient", "Random", "Environment.",
    ];

    [Fact]
    public void Insights_sources_use_no_clock_io_or_randomness()
    {
        var directory = FindInsightsDirectory();
        var files = Directory.GetFiles(directory, "*.cs");

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in Forbidden)
            {
                Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} must not use '{token}'.");
            }
        }
    }

    private static string FindInsightsDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "RunnersDiary.Domain", "Insights");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/RunnersDiary.Domain/Insights was not found above " + AppContext.BaseDirectory);
    }
}
