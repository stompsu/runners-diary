using RunnersDiary.Ai.Tests.Support;

namespace RunnersDiary.Ai.Tests.Validation;

/// <summary>The narrative Domain code is pure like the metrics (M5 AC2): no clock, I/O or randomness.</summary>
public sealed class NarrativePurityTests
{
    private static readonly string[] Forbidden =
    [
        "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider", "TimeZoneInfo", "System.IO",
        "File.", "Console.", "HttpClient", "Random", "Environment.",
    ];

    [Fact]
    public void Narrative_sources_use_no_clock_io_or_randomness()
    {
        var files = Directory.GetFiles(Path.Combine(RepoPaths.Root, "src", "RunnersDiary.Domain", "Insights", "Narrative"), "*.cs");

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
}
