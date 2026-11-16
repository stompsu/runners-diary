using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Goals;

/// <summary>The realistic-target estimate of a goal (ADR 0039 §4): a Riegel prediction and non-blocking warnings. Pure.</summary>
public static class GoalEstimator
{
    /// <summary>Runs older than this (inclusive of today) are not read for hard training efforts.</summary>
    public const int TrainingLookbackDays = 112;

    /// <summary>Completed races older than this are not used as a basis.</summary>
    public const int RaceLookbackDays = 365;

    public const int MinHardRpe = 8;
    public const double TargetFasterThreshold = 0.95;
    public const double LowVolumeMinGoalMeters = 21_000;
    public const double LowVolumeShare = 0.5;
    public const int PlanHorizonDays = 112;

    /// <summary>Weeks a goal needs at least: under 15 km 4, under 30 km 8, up to 42.2 km 12, longer 16.</summary>
    public static int MinimumWeeks(double distanceMeters) =>
        distanceMeters < 15_000 ? 4 : distanceMeters < 30_000 ? 8 : distanceMeters <= 42_200 ? 12 : 16;

    /// <param name="facts">Runs of the last 112 days (any status; only completed ones are read).</param>
    /// <param name="races">The caller's race-category runs (any status); only completed ones in the last 365 days are read.</param>
    public static GoalEstimate Estimate(GoalSpec goal, DateOnly today, IReadOnlyList<RunFact> facts, IReadOnlyList<RaceFact> races)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(races);

        var days = goal.TargetDate.DayNumber - today.DayNumber;
        var weeks = (int)Math.Floor(days / 7.0);
        var minimumWeeks = MinimumWeeks(goal.DistanceMeters);
        var history = TrainingHistory.From(facts, today);
        var recentWeekly = history.MeanWeeklyDistanceMeters;

        var prediction = RaceTimePredictor.Predict(goal.DistanceMeters, goal.ElevationGainMeters, Efforts(facts, races, today));

        var warnings = new List<EstimateWarning>();
        if (prediction is null)
        {
            warnings.Add(new EstimateWarning(EstimateWarningCode.NoRecentEffort, null));
        }
        else if (goal.TargetDurationSeconds is { } target && target < TargetFasterThreshold * prediction.DurationSeconds)
        {
            warnings.Add(new EstimateWarning(
                EstimateWarningCode.TargetMuchFasterThanPredicted,
                Rounding.Round((1 - ((double)target / prediction.DurationSeconds)) * 100, 1)));
        }

        if (weeks < minimumWeeks)
        {
            warnings.Add(new EstimateWarning(EstimateWarningCode.TooLittleTime, minimumWeeks - weeks));
        }

        if (goal.DistanceMeters >= LowVolumeMinGoalMeters && recentWeekly < LowVolumeShare * goal.DistanceMeters)
        {
            warnings.Add(new EstimateWarning(EstimateWarningCode.LowWeeklyVolume, recentWeekly));
        }

        if (days > PlanHorizonDays)
        {
            warnings.Add(new EstimateWarning(EstimateWarningCode.BeyondPlanHorizon, null));
        }

        return new GoalEstimate(today, days, weeks, GoalTerms.Of(goal.TargetDate, today), minimumWeeks, recentWeekly, prediction, warnings);
    }

    /// <summary>Completed races (time = result time, else duration) and completed hard training runs (RPE 8 or more) as prediction candidates.</summary>
    internal static List<EffortFact> Efforts(IReadOnlyList<RunFact> facts, IReadOnlyList<RaceFact> races, DateOnly today)
    {
        var efforts = new List<EffortFact>();
        var elevationById = new Dictionary<Guid, double?>();
        foreach (var fact in facts)
        {
            elevationById.TryAdd(fact.Id, fact.ElevationGainMeters);
        }

        foreach (var race in races)
        {
            var time = race.ResultTimeSeconds ?? race.DurationSeconds;
            var distance = race.DistanceMeters ?? race.OfficialDistanceMeters;
            if (race.Status != RunStatus.Completed || race.Date > today || race.Date <= today.AddDays(-RaceLookbackDays)
                || time is not > 0 || distance is not > 0)
            {
                continue;
            }

            efforts.Add(new EffortFact(race.Id, race.Date, EffortKind.Race, distance.Value, time.Value, elevationById.GetValueOrDefault(race.Id)));
        }

        foreach (var fact in facts)
        {
            if (fact.Status != RunStatus.Completed || fact.Date > today || fact.Date <= today.AddDays(-TrainingLookbackDays)
                || fact.Rpe is not >= MinHardRpe || fact.DistanceMeters is not > 0 || fact.DurationSeconds is not > 0
                || RunTypeCatalog.Find(fact.RunTypeId) is not { Category: RunTypeCategory.Training })
            {
                continue;
            }

            efforts.Add(new EffortFact(fact.Id, fact.Date, EffortKind.Training, fact.DistanceMeters.Value, fact.DurationSeconds.Value, fact.ElevationGainMeters));
        }

        return efforts;
    }
}
