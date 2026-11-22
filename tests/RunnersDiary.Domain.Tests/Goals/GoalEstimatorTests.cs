using System.Globalization;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tests.Insights;

namespace RunnersDiary.Domain.Tests.Goals;

public sealed class GoalEstimatorTests
{
    // A Monday. The last four complete ISO weeks are 2026-10-19 .. 2026-11-15.
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly string[] WeekStarts = ["2026-10-19", "2026-10-26", "2026-11-02", "2026-11-09"];

    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Four weeks with a 10 km (Wed) and a 20 km (Sat) easy run each: 30,000 m a week.</summary>
    private static List<RunFact> ThirtyKmWeeks()
    {
        var facts = new List<RunFact>();
        var id = 1;
        foreach (var monday in WeekStarts)
        {
            facts.Add(InsightsTestData.Run(D(monday).AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "easy", 10_000, 3_000, rpe: 3, id: id++));
            facts.Add(InsightsTestData.Run(D(monday).AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "long", 20_000, 6_600, rpe: 5, id: id++));
        }

        return facts;
    }

    /// <summary>A completed 10K race (3000 s) 50 days ago, as a race fact and as the matching run fact.</summary>
    private static (RaceFact Race, RunFact Run) TenKRace()
    {
        var race = InsightsTestData.Race("2026-09-27", RaceDistanceCategory.TenK, result: 3_000, distance: 10_000, id: 900);
        var run = InsightsTestData.Run("2026-09-27", "roadRace", 10_000, 3_000, rpe: 9, id: 900);
        return (race, run);
    }

    private static GoalSpec Spec(string target, double meters, int? seconds = null, double? gain = null) =>
        new(seconds is null ? GoalType.Distance : GoalType.Performance, meters, gain, D(target), seconds, Surface.Road);

    private static GoalEstimate Estimate(GoalSpec goal, bool withRace = true, List<RunFact>? facts = null)
    {
        facts ??= ThirtyKmWeeks();
        var races = new List<RaceFact>();
        if (withRace)
        {
            var (race, run) = TenKRace();
            facts.Add(run);
            races.Add(race);
        }

        return GoalEstimator.Estimate(goal, Today, facts, races);
    }

    [Fact]
    public void Half_marathon_in_90_days_from_a_10K_race_has_a_low_confidence_prediction_and_no_warnings()
    {
        var e = Estimate(Spec("2027-02-14", 21_097.5));

        Assert.Equal(90, e.DaysToGoal);
        Assert.Equal(12, e.WeeksToGoal); // floor(90 / 7)
        Assert.Equal(GoalTerm.LongTerm, e.Term);
        Assert.Equal(8, e.MinimumWeeks);
        Assert.Equal(30_000, e.RecentWeeklyDistanceMeters);
        Assert.Equal(6_619, e.Prediction!.DurationSeconds);
        Assert.Equal(PredictionConfidence.Low, e.Prediction.Confidence); // ratio 2.11 > 2
        Assert.Equal(new Guid(900, 0, 0, new byte[8]), e.Prediction.Basis.RunId);
        Assert.Empty(e.Warnings);
    }

    [Theory]
    [InlineData(10_000, 4)]
    [InlineData(14_999, 4)]
    [InlineData(15_000, 8)]
    [InlineData(29_999, 8)]
    [InlineData(30_000, 12)]
    [InlineData(42_200, 12)]
    [InlineData(42_201, 16)]
    public void Minimum_weeks_by_distance(double meters, int weeks) => Assert.Equal(weeks, GoalEstimator.MinimumWeeks(meters));

    [Fact]
    public void Without_any_usable_training_run_the_estimate_warns_noRecentEffort()
    {
        // Fewer than 3 training runs in the last 6 weeks (no race, no hard run): no fallback either.
        var facts = new List<RunFact>
        {
            InsightsTestData.Run("2026-11-11", "easy", 10_000, 3_000, rpe: 3, id: 1),
            InsightsTestData.Run("2026-11-13", "easy", 10_000, 3_000, rpe: 3, id: 2),
            InsightsTestData.Run("2026-09-01", "easy", 10_000, 3_000, rpe: 3, id: 3), // outside the 42 days
        };
        var e = Estimate(Spec("2027-02-14", 10_000), withRace: false, facts: facts);

        Assert.Null(e.Prediction);
        Assert.Equal([EstimateWarningCode.NoRecentEffort], e.Warnings.Select(w => w.Code));
        Assert.Null(e.Warnings[0].Value);
    }

    [Fact]
    public void A_hard_training_run_serves_as_a_low_confidence_basis_when_there_is_no_race()
    {
        var facts = ThirtyKmWeeks();
        facts.Add(InsightsTestData.Run("2026-11-05", "tempo", 8_000, 2_400, rpe: 8, id: 50));

        var e = Estimate(Spec("2027-02-14", 10_000), withRace: false, facts);

        Assert.Equal(3_040, e.Prediction!.DurationSeconds);
        Assert.Equal(PredictionConfidence.Low, e.Prediction.Confidence);
    }

    [Fact]
    public void Training_runs_with_rpe_below_eight_or_beyond_112_days_or_planned_are_not_efforts()
    {
        var facts = ThirtyKmWeeks();
        facts.Add(InsightsTestData.Run("2026-11-05", "tempo", 8_000, 2_400, rpe: 7, id: 50));
        facts.Add(InsightsTestData.Run("2026-07-26", "tempo", 8_000, 2_400, rpe: 9, id: 51)); // 113 days ago
        facts.Add(InsightsTestData.Run("2026-07-27", "tempo", 8_000, 2_400, rpe: 9, id: 54)); // 112 days ago: the window is the last 112 days, today included
        facts.Add(InsightsTestData.Run("2026-11-20", "tempo", 8_000, 2_400, rpe: 9, status: RunStatus.Planned, id: 52));

        // Not hard efforts: only the M9.1 fallback from ordinary training runs applies.
        Assert.Equal(EffortKind.RecentTraining, Estimate(Spec("2027-02-14", 10_000), withRace: false, facts).Prediction!.Basis.Kind);

        facts.Add(InsightsTestData.Run("2026-07-28", "tempo", 8_000, 2_400, rpe: 9, id: 53)); // 111 days ago: inside
        Assert.Equal(EffortKind.Training, Estimate(Spec("2027-02-14", 10_000), withRace: false, facts).Prediction!.Basis.Kind);
    }

    [Fact]
    public void A_race_older_than_a_year_or_still_planned_is_not_a_basis()
    {
        var old = InsightsTestData.Race("2025-11-16", RaceDistanceCategory.TenK, result: 3_000, distance: 10_000, id: 901);
        var planned = InsightsTestData.Race("2026-12-01", RaceDistanceCategory.TenK, result: null, status: RunStatus.Planned, id: 902);

        var e = GoalEstimator.Estimate(Spec("2027-02-14", 10_000), Today, ThirtyKmWeeks(), [old, planned]);
        Assert.Equal(EffortKind.RecentTraining, e.Prediction!.Basis.Kind); // no race basis: the fallback

        var yearAgo = InsightsTestData.Race("2025-11-17", RaceDistanceCategory.TenK, result: 3_000, distance: 10_000, id: 903);
        Assert.Equal(EffortKind.Race, GoalEstimator.Estimate(Spec("2027-02-14", 10_000), Today, ThirtyKmWeeks(), [yearAgo]).Prediction!.Basis.Kind);
    }

    [Fact]
    public void The_race_elevation_comes_from_its_run_fact()
    {
        var race = InsightsTestData.Race("2026-09-27", RaceDistanceCategory.TenK, result: 3_300, distance: 10_000, id: 910);
        var facts = ThirtyKmWeeks();
        facts.Add(InsightsTestData.Run("2026-09-27", "trailRace", 10_000, 3_300, rpe: 9, gain: 100, id: 910));

        var e = GoalEstimator.Estimate(Spec("2027-02-14", 21_097.5), Today, facts, [race]);

        Assert.Equal(6_581, e.Prediction!.DurationSeconds); // basis effort 11,000 m
        Assert.Equal(100, e.Prediction.Basis.ElevationGainMeters);
    }

    [Theory]
    [InlineData(6_288, true, 5.0)] // < 0.95 x 6619 = 6288.05: faster by (1 - 6288 / 6619) = 5.0007 %
    [InlineData(6_289, false, 0)]
    [InlineData(6_000, true, 9.4)] // 1 - 6000 / 6619 = 9.35 %
    public void TargetMuchFasterThanPredicted_threshold_and_value(int target, bool warns, double percent)
    {
        var e = Estimate(Spec("2027-02-14", 21_097.5, target));

        var warning = e.Warnings.SingleOrDefault(w => w.Code == EstimateWarningCode.TargetMuchFasterThanPredicted);
        Assert.Equal(warns, warning is not null);
        if (warns)
        {
            Assert.Equal(percent, warning!.Value);
        }
    }

    [Theory]
    [InlineData("2027-01-04", 7, true, 1)] // 49 days -> 7 weeks, an HM needs 8
    [InlineData("2027-01-10", 7, true, 1)] // 55 days -> floor 7
    [InlineData("2027-01-11", 8, false, 0)] // 56 days -> 8
    public void TooLittleTime_uses_the_floored_weeks(string target, int weeks, bool warns, double short_)
    {
        var e = Estimate(Spec(target, 21_097.5));

        Assert.Equal(weeks, e.WeeksToGoal);
        var warning = e.Warnings.SingleOrDefault(w => w.Code == EstimateWarningCode.TooLittleTime);
        Assert.Equal(warns, warning is not null);
        if (warns)
        {
            Assert.Equal(short_, warning!.Value);
        }
    }

    [Fact]
    public void LowWeeklyVolume_needs_a_goal_of_21_km_and_a_mean_below_half_the_goal()
    {
        // One 10 km run a week: mean 10,000 m; half of 21,097.5 m is 10,548.75.
        var facts = WeekStarts.Select((m, i) => InsightsTestData.Run(D(m).AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "easy", 10_000, 3_000, id: i + 1)).ToList();

        var half = Estimate(Spec("2027-02-14", 21_097.5), withRace: false, [.. facts]);
        var shorter = Estimate(Spec("2027-02-14", 20_999), withRace: false, [.. facts]);
        var ten = Estimate(Spec("2027-02-14", 21_000), withRace: false, [.. facts]); // 10,000 < 10,500: warns
        var enough = Estimate(Spec("2027-02-14", 20_000), withRace: false, [.. facts]);

        Assert.Equal(10_000, half.Warnings.Single(w => w.Code == EstimateWarningCode.LowWeeklyVolume).Value);
        Assert.DoesNotContain(shorter.Warnings, w => w.Code == EstimateWarningCode.LowWeeklyVolume);
        Assert.Contains(ten.Warnings, w => w.Code == EstimateWarningCode.LowWeeklyVolume);
        Assert.DoesNotContain(enough.Warnings, w => w.Code == EstimateWarningCode.LowWeeklyVolume);
    }

    [Fact]
    public void LowWeeklyVolume_is_off_at_exactly_half_the_goal()
    {
        // 30 km a week against a 60 km goal: 30,000 is not below 30,000.
        Assert.DoesNotContain(Estimate(Spec("2027-06-01", 60_000)).Warnings, w => w.Code == EstimateWarningCode.LowWeeklyVolume);
        Assert.Contains(Estimate(Spec("2027-06-01", 60_001)).Warnings, w => w.Code == EstimateWarningCode.LowWeeklyVolume);
    }

    [Theory]
    [InlineData("2027-03-08", false)] // 112 days
    [InlineData("2027-03-09", true)] // 113 days
    public void BeyondPlanHorizon_is_informational_after_112_days(string target, bool warns) =>
        Assert.Equal(warns, Estimate(Spec(target, 10_000)).Warnings.Any(w => w.Code == EstimateWarningCode.BeyondPlanHorizon));

    [Fact]
    public void Warning_wire_codes_are_lowerCamel()
    {
        Assert.Equal(
            ["noRecentEffort", "targetMuchFasterThanPredicted", "tooLittleTime", "lowWeeklyVolume", "beyondPlanHorizon"],
            Enum.GetValues<EstimateWarningCode>().Select(c => c.ToWire()));
    }

    [Fact]
    public void Weekly_mean_ignores_planned_runs_cross_training_and_the_current_week()
    {
        var facts = ThirtyKmWeeks();
        facts.Add(InsightsTestData.Run("2026-11-12", "crossTraining", 99_000, 3_600, id: 60));
        facts.Add(InsightsTestData.Run("2026-11-13", "easy", 50_000, 3_600, status: RunStatus.Planned, id: 61));
        facts.Add(InsightsTestData.Run("2026-11-16", "easy", 50_000, 3_600, id: 62)); // this week (Monday today)

        Assert.Equal(30_000, Estimate(Spec("2027-02-14", 10_000), withRace: false, facts).RecentWeeklyDistanceMeters);
    }

    [Fact]
    public void The_estimate_of_an_easy_only_diary_uses_the_recent_training_fallback_and_warns_when_the_target_is_much_faster()
    {
        // The M9.1 scenario: 10 x 8 km at 6:00/km, half marathon in 1:40.
        var facts = Enumerable.Range(0, 10)
            .Select(i => InsightsTestData.Run(Today.AddDays(-2 - (3 * i)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "easy", 8_000, 2_880, rpe: 3, id: 100 + i))
            .ToList();

        var e = GoalEstimator.Estimate(Spec("2026-12-20", 21_097.5, seconds: 6_000), Today, facts, []);

        Assert.Equal(7_245, e.Prediction!.DurationSeconds);
        Assert.Equal(343, e.Prediction.PaceSecondsPerKm);
        Assert.Equal(PredictionConfidence.Low, e.Prediction.Confidence);
        Assert.Equal(EffortKind.RecentTraining, e.Prediction.Basis.Kind);
        Assert.Null(e.Prediction.Basis.RunId);
        Assert.Equal(10, e.Prediction.Basis.RunCount);
        var warning = Assert.Single(e.Warnings, w => w.Code == EstimateWarningCode.TargetMuchFasterThanPredicted);
        Assert.Equal(17.2, warning.Value);
        Assert.DoesNotContain(e.Warnings, w => w.Code == EstimateWarningCode.NoRecentEffort);
    }

    [Fact]
    public void The_fallback_excludes_treadmill_runs_but_keeps_trail_runs()
    {
        var facts = new List<RunFact>
        {
            InsightsTestData.Run("2026-11-10", "easy", 8_000, 2_880, surface: Surface.Treadmill, id: 1),
            InsightsTestData.Run("2026-11-11", "easy", 8_000, 2_880, surface: Surface.Trail, id: 2),
            InsightsTestData.Run("2026-11-12", "easy", 8_000, 2_880, surface: Surface.Road, id: 3),
        };

        var runs = GoalEstimator.RecentTrainingRuns(facts, Today);

        Assert.Equal([2, 3], runs.Select(r => (int)r.RunId!.Value.ToByteArray()[0]).Order());

        // Three treadmill runs alone are no basis (fewer than 3 usable runs).
        var belt = Enumerable.Range(0, 3).Select(i => InsightsTestData.Run($"2026-11-1{i}", "easy", 8_000, 2_880, surface: Surface.Treadmill, id: 10 + i)).ToList();
        Assert.Null(Estimate(Spec("2027-02-14", 10_000), withRace: false, belt).Prediction);
    }

    [Fact]
    public void The_flat_fitness_pace_of_a_hilly_goal_is_the_flat_prediction_while_the_duration_keeps_the_climbing()
    {
        var facts = Enumerable.Range(0, 10)
            .Select(i => InsightsTestData.Run(Today.AddDays(-2 - (3 * i)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "easy", 8_000, 2_880, rpe: 3, id: 100 + i))
            .ToList();

        var hilly = GoalEstimator.Estimate(Spec("2026-12-20", 21_097.5, seconds: 6_000, gain: 400), Today, facts, []).Prediction!;
        var flat = GoalEstimator.Estimate(Spec("2026-12-20", 21_097.5, seconds: 6_000), Today, facts, []).Prediction!;

        Assert.Equal(7_245, flat.DurationSeconds);
        Assert.Equal(343, flat.FlatPaceSecondsPerKm);
        Assert.Equal(343, hilly.FlatPaceSecondsPerKm);
        Assert.Equal(343, hilly.FitnessPaceSecondsPerKm);
        Assert.True(hilly.DurationSeconds > 8_500); // the estimate still includes the climbing
        Assert.True(hilly.PaceSecondsPerKm > 400);
    }

    [Fact]
    public void The_fallback_uses_only_completed_training_runs_of_the_last_42_days()
    {
        var facts = new List<RunFact>
        {
            InsightsTestData.Run("2026-11-10", "easy", 8_000, 2_880, id: 1),
            InsightsTestData.Run("2026-11-12", "easy", 8_000, 2_880, id: 2),
            InsightsTestData.Run("2026-10-06", "easy", 8_000, 2_880, id: 3), // today - 41: inside
            InsightsTestData.Run("2026-10-05", "easy", 8_000, 2_880, id: 4), // today - 42: outside
            InsightsTestData.Run("2026-11-14", "easy", 8_000, 2_880, status: RunStatus.Planned, id: 5),
            InsightsTestData.Run("2026-11-14", "crossTraining", 8_000, 2_880, id: 6),
        };

        var runs = GoalEstimator.RecentTrainingRuns(facts, Today);

        Assert.Equal([1, 2, 3], runs.Select(r => (int)r.RunId!.Value.ToByteArray()[0]).Order());
    }
}
