using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.RunTypes;
using static RunnersDiary.Domain.Tests.Insights.InsightsTestData;

namespace RunnersDiary.Domain.Tests.Insights;

public sealed class TypeDistributionTests
{
    [Fact]
    public void Groups_are_ordered_by_duration_then_catalog_order_and_rest_is_excluded()
    {
        var facts = new[]
        {
            Run("2026-11-02", "easy", meters: 5000, seconds: 1800, rpe: 3, id: 1),
            Run("2026-11-03", "easy", meters: 6000, seconds: 2100, rpe: 3, id: 2),
            Run("2026-11-04", "long", meters: 15_000, seconds: 5400, rpe: 5, id: 3),
            Run("2026-11-05", "tempo", meters: 8000, seconds: 3900, rpe: 7, id: 4),   // same total as easy: 3900
            Run("2026-11-06", "crossTraining", seconds: 2400, rpe: 4, id: 5),
            Run("2026-11-07", "rest", id: 6),
        };

        var shares = TypeDistribution.Compute(facts);

        Assert.Equal(["long", "easy", "tempo", "crossTraining"], shares.Select(s => s.Code).ToArray()); // easy (20) before tempo (23) on the 3900 tie
        Assert.Equal(2, shares[1].RunCount);
        Assert.Equal(11_000, shares[1].DistanceMeters);
        Assert.Equal(3900, shares[1].DurationSeconds);
        Assert.Equal(3 * 3900 / 60.0, shares[1].Load);
        Assert.Equal(RunTypeCategory.Training, shares[0].Category);
    }

    [Fact]
    public void Cross_training_is_included_with_its_load_but_without_distance()
    {
        var shares = TypeDistribution.Compute([Run("2026-11-02", "crossTraining", seconds: 3000, rpe: 3)]);

        var share = Assert.Single(shares);
        Assert.Equal(RunTypeCategory.Other, share.Category);
        Assert.Equal(0, share.DistanceMeters);
        Assert.Equal(150.0, share.Load);
    }
}
