using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Plans;

/// <summary>M10 review L5: the live plan eval reports the largest single-attempt output per language against the output cap.</summary>
public sealed class PlanOutputTokenStatsTests
{
    private static PlanAttemptDetails Attempt(int number, int outputTokens, string finish = "stop") =>
        new(number, 9_000, outputTokens, finish, [], 40, 0, []);

    [Fact]
    public void Reports_the_max_per_language_with_its_location_share_of_the_cap_and_truncations()
    {
        var stats = new PlanOutputTokenStats();
        stats.Record("en", "half-marathon #1", [Attempt(1, 4_100)]);
        stats.Record("pl", "half-marathon #1", [Attempt(1, 8_000, "length"), Attempt(2, 6_000)]);
        stats.Record("pl", "first-10k #2", [Attempt(1, 5_000)]);

        Assert.Equal(4_100, stats.Max("en"));
        Assert.Equal(8_000, stats.Max("pl"));
        Assert.Null(stats.Max("de"));

        var lines = stats.Report(8_000).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("max output tokens per attempt, en: 4100 / cap 8000 (51%) at half-marathon #1 attempt 1; 1 answered attempts, 0 truncated (finish length)", lines[0]);
        Assert.Equal("max output tokens per attempt, pl: 8000 / cap 8000 (100%) at half-marathon #1 attempt 1; 3 answered attempts, 1 truncated (finish length)", lines[1]);
    }

    [Fact]
    public void A_run_without_answered_attempts_records_nothing()
    {
        var stats = new PlanOutputTokenStats();
        stats.Record("pl", "x #1", []);

        Assert.Null(stats.Max("pl"));
    }
}
