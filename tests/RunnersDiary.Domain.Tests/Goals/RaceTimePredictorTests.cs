using RunnersDiary.Domain.Goals;

namespace RunnersDiary.Domain.Tests.Goals;

public sealed class RaceTimePredictorTests
{
    private static readonly DateOnly Day = new(2026, 10, 1);

    private static EffortFact Race(double meters, int seconds, double? gain = null, DateOnly? date = null, int id = 1) =>
        new(new Guid(id, 0, 0, new byte[8]), date ?? Day, EffortKind.Race, meters, seconds, gain);

    private static EffortFact Hard(double meters, int seconds, DateOnly? date = null, int id = 100) =>
        new(new Guid(id, 0, 0, new byte[8]), date ?? Day, EffortKind.Training, meters, seconds, null);

    [Fact]
    public void Riegel_from_a_10K_race_to_the_half_marathon()
    {
        // 3000 s x (21097.5 / 10000)^1.06 = 6619.2 s; pace 6619 / 21.0975 = 313.75 -> 314 s/km.
        var p = RaceTimePredictor.Predict(21_097.5, null, [Race(10_000, 3_000)]);

        Assert.NotNull(p);
        Assert.Equal(6_619, p.DurationSeconds);
        Assert.Equal(314, p.PaceSecondsPerKm);
        Assert.Equal(PredictionConfidence.Low, p.Confidence); // ratio 2.11 is beyond double the distance
    }

    [Fact]
    public void Riegel_towards_shorter_distances()
    {
        // 3000 x 0.5^1.06 = 1438.9 s -> 1439 s over 5 km = 288 s/km (287.8).
        var five = RaceTimePredictor.Predict(5_000, null, [Race(10_000, 3_000)]);
        // 6000 x (10000 / 21097.5)^1.06 = 2719.36 s -> 2719; pace 271.9 -> 272.
        var ten = RaceTimePredictor.Predict(10_000, null, [Race(21_097.5, 6_000)]);

        Assert.Equal((1_439, 288), (five!.DurationSeconds, five.PaceSecondsPerKm));
        Assert.Equal((2_719, 272), (ten!.DurationSeconds, ten.PaceSecondsPerKm));
    }

    [Fact]
    public void Elevation_counts_ten_metres_per_metre_of_climb_on_both_sides()
    {
        // Goal 25 km + 900 m climb: E = 34,000. Flat 10K race basis E = 10,000, ratio 3.4: 3000 x 3.4^1.06 = 10,977 s. Ratio above 2 -> Low.
        var trail = RaceTimePredictor.Predict(25_000, 900, [Race(10_000, 3_000)]);
        // Basis 10 km + 100 m (E = 11,000, 3300 s) to a flat half marathon: 3300 x (21097.5 / 11000)^1.06 = 6581 s.
        var climbed = RaceTimePredictor.Predict(21_097.5, null, [Race(10_000, 3_300, gain: 100)]);

        Assert.Equal(34_000, RaceTimePredictor.EffortDistance(25_000, 900));
        Assert.Equal(10_977, trail!.DurationSeconds);
        Assert.Equal(439, trail.PaceSecondsPerKm); // 10977 / 25 = 439.08
        Assert.Equal(PredictionConfidence.Low, trail.Confidence);
        Assert.Equal(6_581, climbed!.DurationSeconds);
        Assert.Equal(PredictionConfidence.High, climbed.Confidence);
    }

    [Fact]
    public void A_race_beats_a_closer_hard_training_run()
    {
        var p = RaceTimePredictor.Predict(10_000, null, [Race(21_097.5, 6_000, id: 1), Hard(10_000, 2_900)]);

        Assert.Equal(EffortKind.Race, p!.Basis.Kind);
        Assert.Equal(2_719, p.DurationSeconds);
    }

    [Fact]
    public void Without_a_race_the_hard_training_run_is_the_basis_and_confidence_is_low()
    {
        // 2400 x (10000 / 8000)^1.06 = 3040.4 s.
        var p = RaceTimePredictor.Predict(10_000, null, [Hard(8_000, 2_400)]);

        Assert.Equal(3_040, p!.DurationSeconds);
        Assert.Equal(PredictionConfidence.Low, p.Confidence);
        Assert.Equal(EffortKind.Training, p.Basis.Kind);
    }

    [Fact]
    public void The_basis_is_the_race_with_the_closest_effort_distance()
    {
        var five = Race(5_000, 1_400, id: 1);
        var half = Race(21_097.5, 6_000, id: 2);
        var ten = Race(10_000, 2_950, id: 3);

        Assert.Equal(ten.RunId, RaceTimePredictor.Predict(11_000, null, [five, half, ten])!.Basis.RunId);
        Assert.Equal(half.RunId, RaceTimePredictor.Predict(30_000, null, [five, half, ten])!.Basis.RunId);
        Assert.Equal(five.RunId, RaceTimePredictor.Predict(4_000, null, [five, half, ten])!.Basis.RunId);
    }

    [Fact]
    public void Equal_distances_tie_to_the_most_recent_and_then_to_the_smaller_id()
    {
        var older = Race(10_000, 3_100, date: Day.AddDays(-30), id: 1);
        var newer = Race(10_000, 3_000, date: Day, id: 2);
        var sameDay = Race(10_000, 2_900, date: Day, id: 3);

        Assert.Equal(newer.RunId, RaceTimePredictor.Predict(10_000, null, [older, newer])!.Basis.RunId);
        Assert.Equal(newer.RunId, RaceTimePredictor.Predict(10_000, null, [sameDay, newer, older])!.Basis.RunId);
    }

    [Fact]
    public void Efforts_under_three_kilometres_or_without_a_time_are_ignored()
    {
        Assert.Null(RaceTimePredictor.Predict(10_000, null, [Race(2_999, 600), Race(10_000, 0)]));
        Assert.NotNull(RaceTimePredictor.Predict(10_000, null, [Race(3_000, 900)]));
        Assert.Null(RaceTimePredictor.Predict(10_000, null, []));
        Assert.Null(RaceTimePredictor.Predict(0, null, [Race(10_000, 3_000)]));
    }

    [Theory]
    [InlineData(4_999, PredictionConfidence.Low)] // ratio 0.4998
    [InlineData(5_000, PredictionConfidence.High)] // exactly 0.5
    [InlineData(20_000, PredictionConfidence.High)] // exactly 2.0
    [InlineData(20_001, PredictionConfidence.Low)]
    public void Confidence_is_high_only_for_a_race_within_half_to_double_the_distance(double goalMeters, PredictionConfidence expected) =>
        Assert.Equal(expected, RaceTimePredictor.Predict(goalMeters, null, [Race(10_000, 3_000)])!.Confidence);

    private static EffortFact Easy(double meters, int seconds, double? gain = null, int id = 200, int day = 0) =>
        new(new Guid(id, 0, 0, new byte[8]), Day.AddDays(day), EffortKind.Training, meters, seconds, gain);

    private static List<EffortFact> Runs(int count, double meters, int seconds) =>
        [.. Enumerable.Range(0, count).Select(i => Easy(meters, seconds, id: 200 + i, day: i))];

    [Fact]
    public void Fallback_from_ten_easy_runs_matches_the_documented_worked_example()
    {
        // 10 x 8 km at 6:00/km: p = 360, E_b = 8,000, T_b = 0.90 x 2880 = 2592 s; Riegel to 21,097.5 m = 7,245 s, 343 s/km.
        var p = RaceTimePredictor.Predict(21_097.5, null, [], Runs(10, 8_000, 2_880));

        Assert.NotNull(p);
        Assert.Equal(7_245, p.DurationSeconds);
        Assert.Equal(343, p.PaceSecondsPerKm);
        Assert.Equal(PredictionConfidence.Low, p.Confidence);
        Assert.Equal(EffortKind.RecentTraining, p.Basis.Kind);
        Assert.Null(p.Basis.RunId);
        Assert.Equal(10, p.Basis.RunCount);
        Assert.Equal(8_000, p.Basis.DistanceMeters);
        Assert.Equal(2_592, p.Basis.DurationSeconds);
        Assert.Equal(0, p.Basis.ElevationGainMeters);
        Assert.Equal(Day.AddDays(9), p.Basis.Date); // the latest run used
    }

    [Fact]
    public void Fallback_needs_three_usable_runs()
    {
        Assert.Null(RaceTimePredictor.Predict(10_000, null, [], Runs(2, 8_000, 2_880)));
        Assert.NotNull(RaceTimePredictor.Predict(10_000, null, [], Runs(3, 8_000, 2_880)));
        // Runs under 3 km, without a time or with an effort pace outside 150-900 s/km do not count.
        List<EffortFact> unusable = [.. Runs(2, 8_000, 2_880), Easy(2_999, 900, id: 1), Easy(8_000, 0, id: 2), Easy(8_000, 8_000 * 901 / 1000 + 100, id: 3), Easy(8_000, 1_000, id: 4)];
        Assert.Null(RaceTimePredictor.Predict(10_000, null, [], unusable));
    }

    [Fact]
    public void A_race_or_a_hard_effort_replaces_the_fallback()
    {
        var runs = Runs(10, 8_000, 2_880);

        var race = RaceTimePredictor.Predict(10_000, null, [Race(10_000, 3_000)], runs);
        var hard = RaceTimePredictor.Predict(10_000, null, [Hard(10_000, 3_100)], runs);

        Assert.Equal(EffortKind.Race, race!.Basis.Kind);
        Assert.Equal(EffortKind.Training, hard!.Basis.Kind);
    }

    [Fact]
    public void Fallback_uses_medians_so_an_outlier_does_not_move_it()
    {
        // Odd count: the middle value. 5 runs: paces 300, 360, 360, 360, 600 -> median 360.
        var odd = RaceTimePredictor.FromRecentTraining(8_000, null, [Easy(8_000, 2_400, id: 1), Easy(8_000, 2_880, id: 2), Easy(8_000, 2_880, id: 3), Easy(8_000, 2_880, id: 4), Easy(8_000, 4_800, id: 5)]);
        Assert.Equal(2_592, odd!.Basis.DurationSeconds);

        // Even count: the mean of the two middle values. Distances 6 km and 10 km at 6:00/km -> E_b = 8,000, p = 360.
        var even = RaceTimePredictor.FromRecentTraining(8_000, null, [Easy(6_000, 2_160, id: 1), Easy(6_000, 2_160, id: 2), Easy(10_000, 3_600, id: 3), Easy(10_000, 3_600, id: 4)]);
        Assert.Equal(8_000, even!.Basis.DistanceMeters);
        Assert.Equal(2_592, even.Basis.DurationSeconds);
        Assert.Equal(2_592, even.DurationSeconds); // goal = E_b: no extrapolation
    }

    [Fact]
    public void Fallback_counts_elevation_as_km_effort()
    {
        // 8 km + 200 m climb = E 10,000 in 3,000 s: effort pace 300 s/km; T_b = 0.90 x 300 x 10 = 2,700 s; goal 10 km flat = E_b: 2,700 s.
        var runs = Enumerable.Range(0, 3).Select(i => Easy(8_000, 3_000, gain: 200, id: 300 + i, day: i)).ToList();

        var p = RaceTimePredictor.FromRecentTraining(10_000, null, runs);

        Assert.Equal(2_700, p!.DurationSeconds);
        Assert.Equal(270, p.PaceSecondsPerKm);
        Assert.Equal(200, p.Basis.ElevationGainMeters);
    }
}
