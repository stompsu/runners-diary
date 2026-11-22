using RunnersDiary.Domain.Insights;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>Projects the deterministic inputs of a plan into the <see cref="PlanFactSheet"/> (ADR 0040 §3). Pure.</summary>
public static class PlanFactSheetBuilder
{
    /// <summary>
    /// Load and ACWR per week and the races come from <paramref name="historyReport"/> (the M5 metrics, reused): build it over
    /// <c>[today - 364, today]</c> so it covers the ten complete weeks and the completed races of the last 365 days.
    /// Commitments (nearest first) and dismissed hints are capped at 200 and 60.
    /// </summary>
    public static PlanFactSheet Build(
        GoalSpec goal,
        GoalEstimate estimate,
        DateOnly today,
        InsightsReport historyReport,
        TrainingHistory history,
        IReadOnlyList<CommitmentFact> commitments,
        IReadOnlyList<DismissedHint> dismissed,
        PlanEnvelope envelope,
        string language)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(estimate);
        ArgumentNullException.ThrowIfNull(historyReport);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(commitments);
        ArgumentNullException.ThrowIfNull(dismissed);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(language);

        var reportWeeks = historyReport.Weeks.ToDictionary(w => w.Start);
        var weeks = history.Weeks.Select(w =>
        {
            reportWeeks.TryGetValue(w.Start, out var bucket);
            var acwr = bucket?.AcwrAtEnd;
            return new PlanHistoryWeek(
                w.Start,
                w.DistanceMeters,
                w.DurationSeconds,
                w.Sessions,
                w.LongestRunMeters,
                w.HardSessions,
                bucket?.Load ?? 0,
                acwr,
                acwr is { } value ? Acwr.BandOf((decimal)value) : null);
        }).ToList();

        var latest = historyReport.Load.AtAsOf;
        var races = historyReport.Races
            .Where(r => r.Status == Runs.RunStatus.Completed && r.Date <= today && r.Date > today.AddDays(-GoalEstimator.RaceLookbackDays)
                && r.ResultTimeSeconds is > 0 && (r.DistanceMeters ?? r.OfficialDistanceMeters) is > 0)
            .OrderByDescending(r => r.Date).ThenBy(r => r.RunId)
            .Take(PlanFactSheet.MaxRaces)
            .OrderBy(r => r.Date).ThenBy(r => r.RunId)
            .Select(r => new PlanRaceFact(r.Date, (r.DistanceMeters ?? r.OfficialDistanceMeters)!.Value, r.ResultTimeSeconds!.Value, r.RunTypeCode))
            .ToList();

        var prediction = estimate.Prediction;
        return new PlanFactSheet(
            PlanFactSheet.CurrentVersion,
            today,
            language,
            new PlanGoalFacts(
                goal.Type,
                goal.DistanceMeters,
                goal.ElevationGainMeters,
                goal.Surface,
                goal.TargetDate,
                goal.TargetDurationSeconds,
                goal.TargetPaceSecondsPerKm,
                prediction?.DurationSeconds,
                estimate.WeeksToGoal,
                prediction?.Basis.Kind,
                prediction?.Confidence,
                prediction?.FitnessPaceSecondsPerKm,
                GoalEstimator.IsAmbitiousTarget(goal.TargetDurationSeconds, prediction?.DurationSeconds),
                PlanGoalFacts.IsFarBeyondPrediction(goal.TargetDurationSeconds, prediction?.DurationSeconds)),
            new PlanHistoryFacts(
                weeks, history.UsualWeekdays, history.SessionsPerWeek, new PlanAcwrFact(latest.Value, latest.Band), races, history.RecentEasyPaceSecondsPerKm),
            [.. commitments.OrderBy(c => c.Date).ThenBy(c => c.Kind).Take(PlanFactSheet.MaxCommitments)],
            [.. dismissed.OrderBy(d => d.Date).ThenBy(d => d.RunTypeId).Take(PlanFactSheet.MaxDismissed)],
            envelope);
    }
}
