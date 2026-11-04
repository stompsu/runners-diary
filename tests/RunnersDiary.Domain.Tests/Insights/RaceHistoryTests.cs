using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

public sealed class RaceHistoryTests
{
    private static readonly InsightsPeriod Year = new(D("2026-01-01"), D("2026-12-31"));
    private static readonly DateOnly Today = D("2026-11-04");

    [Fact]
    public void A_pb_is_the_fastest_result_so_far_in_its_distance_a_later_slower_race_is_not()
    {
        var races = new[]
        {
            Race("2026-03-01", RaceDistanceCategory.TenK, 3000, 1),
            Race("2026-06-01", RaceDistanceCategory.TenK, 2900, 2),
            Race("2026-09-01", RaceDistanceCategory.TenK, 2950, 3),
        };

        var list = RaceHistory.Compute(races, Year, Today);

        Assert.Equal([true, true, false], list.Select(r => r.IsPersonalBest).ToArray());
    }

    [Fact]
    public void An_earlier_slower_race_is_a_pb_at_its_time_only()
    {
        var races = new[]
        {
            Race("2026-03-01", RaceDistanceCategory.HalfMarathon, 7000, 1),
            Race("2026-06-01", RaceDistanceCategory.TenK, 2900, 2), // other distance: unaffected
            Race("2026-09-01", RaceDistanceCategory.HalfMarathon, 6800, 3),
        };

        var list = RaceHistory.Compute(races, Year, Today);

        Assert.All(list, r => Assert.True(r.IsPersonalBest));
    }

    [Fact]
    public void On_a_tie_the_earlier_race_holds_the_pb()
    {
        var races = new[]
        {
            Race("2026-03-01", RaceDistanceCategory.FiveK, 1500, 1),
            Race("2026-09-01", RaceDistanceCategory.FiveK, 1500, 2),
        };

        var list = RaceHistory.Compute(races, Year, Today);

        Assert.Equal([true, false], list.Select(r => r.IsPersonalBest).ToArray());
    }

    [Fact]
    public void The_pb_check_looks_at_races_before_the_period_too()
    {
        var races = new[]
        {
            Race("2025-05-01", RaceDistanceCategory.TenK, 2800, 1), // before the listed period
            Race("2026-03-01", RaceDistanceCategory.TenK, 2900, 2),
        };

        var list = RaceHistory.Compute(races, Year, Today);

        var only = Assert.Single(list);
        Assert.Equal(2900, only.ResultTimeSeconds);
        Assert.False(only.IsPersonalBest);
    }

    [Fact]
    public void Other_distances_never_have_a_pb()
    {
        var list = RaceHistory.Compute([Race("2026-03-01", RaceDistanceCategory.Other, 3000, 1)], Year, Today);

        Assert.False(Assert.Single(list).IsPersonalBest);
    }

    [Fact]
    public void Planned_races_are_listed_with_their_target_and_never_as_pb()
    {
        var races = new[]
        {
            Race("2026-03-01", RaceDistanceCategory.TenK, 3000, 1),
            Race("2026-12-01", RaceDistanceCategory.TenK, null, 2, RunStatus.Planned, target: 2800),
        };

        var list = RaceHistory.Compute(races, Year, Today);

        Assert.Equal(2, list.Count);
        Assert.Equal(RunStatus.Planned, list[1].Status);
        Assert.Equal(2800, list[1].TargetTimeSeconds);
        Assert.Null(list[1].ResultTimeSeconds);
        Assert.False(list[1].IsPersonalBest);
        Assert.True(list[0].IsPersonalBest);
    }

    [Fact]
    public void The_result_falls_back_to_the_duration_and_the_pace_uses_the_distance()
    {
        var race = new RaceFact(new Guid(1, 0, 0, new byte[8]), D("2026-03-01"), RunStatus.Completed, 1, "Gliwice 10K", RaceDistanceCategory.TenK, 10_000, 10_005, 2970, null, null, 12, 3, "M40", 5);

        var result = Assert.Single(RaceHistory.Compute([race], Year, Today));

        Assert.Equal(2970, result.ResultTimeSeconds);
        Assert.Equal(296.9, result.PaceSecondsPerKm); // 2970 / 10.005 km
        Assert.Equal("roadRace", result.RunTypeCode);
        Assert.Equal(5, result.RatingStars);
    }

    [Fact]
    public void Completed_races_dated_after_today_are_ignored()
    {
        var list = RaceHistory.Compute([Race("2026-12-01", RaceDistanceCategory.TenK, 2000, 1)], Year, Today);

        Assert.Empty(list);
    }
}
