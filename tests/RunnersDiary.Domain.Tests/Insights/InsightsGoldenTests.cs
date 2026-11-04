using System.Text.Json;
using System.Text.Json.Serialization;
using RunnersDiary.Domain.Insights;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

/// <summary>
/// Golden snapshot of the demo dataset's default report (today = 2026-11-04, Europe/Warsaw). It changes only with an intended
/// definition change (which bumps <see cref="MetricsVersion"/>) or an intended change of <c>seed/demo/runs.json</c>.
/// Regenerate with <c>UPDATE_GOLDEN=1 dotnet test --project tests/RunnersDiary.Domain.Tests --filter-class "*InsightsGoldenTests"</c>.
/// </summary>
public sealed class InsightsGoldenTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void The_demo_report_for_a_fixed_today_matches_the_committed_snapshot()
    {
        var report = DemoInsightsTests.TwelveWeeks(D("2026-11-04"));
        var actual = JsonSerializer.Serialize(report, Options).ReplaceLineEndings("\n") + "\n";
        var path = GoldenPath();

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        Assert.True(File.Exists(path), "Missing golden file; run with UPDATE_GOLDEN=1.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
        Assert.Equal(1, report.MetricsVersion);
    }

    private static string GoldenPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "RunnersDiary.Domain.Tests", "Insights", "golden");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, "demo-report-v1.json");
            }
        }

        throw new DirectoryNotFoundException("tests/RunnersDiary.Domain.Tests/Insights/golden was not found above " + AppContext.BaseDirectory);
    }
}
