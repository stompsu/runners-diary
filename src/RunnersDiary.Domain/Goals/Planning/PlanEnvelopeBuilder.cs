using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>Builds the <see cref="PlanEnvelope"/> (ADR 0040 §2). Pure: "today", the history and the commitments are inputs.</summary>
public static class PlanEnvelopeBuilder
{
    /// <summary>The phase of each of <paramref name="totalWeeks"/> weeks to the goal, first week first (ADR 0040 §2).</summary>
    public static IReadOnlyList<TrainingPhase> Phases(int totalWeeks, int taperWeeks)
    {
        if (totalWeeks <= 0)
        {
            return [];
        }

        var phases = new List<TrainingPhase>(totalWeeks);
        if (totalWeeks <= taperWeeks + 1)
        {
            // "Goal three weeks away = taper only."
            phases.AddRange(Enumerable.Repeat(TrainingPhase.Taper, totalWeeks));
            return phases;
        }

        var peak = totalWeeks >= 8 ? 2 : 1;
        var remaining = totalWeeks - taperWeeks - peak;
        var baseWeeks = remaining >= 6 ? remaining / 2 : (remaining >= 3 ? 1 : 0);
        phases.AddRange(Enumerable.Repeat(TrainingPhase.Base, baseWeeks));
        phases.AddRange(Enumerable.Repeat(TrainingPhase.Build, remaining - baseWeeks));
        phases.AddRange(Enumerable.Repeat(TrainingPhase.Peak, peak));
        phases.AddRange(Enumerable.Repeat(TrainingPhase.Taper, taperWeeks));
        return phases;
    }

    /// <summary>
    /// The reference pace (ADR 0040 amendment 2026-11-21, climbing basis 2026-11-22): the slower (larger s/km) of the target pace and the fitness pace,
    /// both on a FLAT basis so a hilly goal does not inflate the pace of every session. Fitness: the prediction's flat pace (the same Riegel for the goal
    /// distance with no climbing, any basis), else 0.90 × the mean pace of the last 4 complete weeks (a real, flat pace). Target: its on-course pace
    /// × <c>distance / effort distance</c> (the climbing share removed; unchanged on a flat goal). Whichever exists when only one does; null when none.
    /// </summary>
    public static int? ReferencePace(GoalSpec goal, TimePrediction? prediction, TrainingHistory history)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(history);
        int? fitness = prediction?.FitnessPaceSecondsPerKm
            ?? (history.MeanPaceSecondsPerKm is { } mean ? (int)Math.Round(PlanEnvelopeRules.ReferencePaceFromMeanPace * mean, MidpointRounding.AwayFromZero) : null);
        int? target = goal.TargetPaceSecondsPerKm is { } onCourse
            ? (int)Math.Round(onCourse * goal.DistanceMeters / RaceTimePredictor.EffortDistance(goal.DistanceMeters, goal.ElevationGainMeters), MidpointRounding.AwayFromZero)
            : null;
        return target is { } t && fitness is { } f ? Math.Max(t, f) : target ?? fitness;
    }

    /// <summary>The faster bound of easy and recovery sessions: <c>max(reference, ceil(0.95 × recent easy pace))</c>; either part alone; null when neither.</summary>
    public static int? EasyPaceMin(int? referencePaceSecondsPerKm, TrainingHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        int? fromHistory = history.RecentEasyPaceSecondsPerKm is { } easy
            ? (int)Math.Ceiling((PlanEnvelopeRules.EasyPaceShareOfRecent * easy) - 1e-9)
            : null;
        return referencePaceSecondsPerKm is { } r && fromHistory is { } h ? Math.Max(r, h) : referencePaceSecondsPerKm ?? fromHistory;
    }

    public static PlanEnvelope Build(
        GoalSpec goal, DateOnly today, TrainingHistory history, IReadOnlyList<CommitmentFact> commitments, int? referencePaceSecondsPerKm)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(commitments);

        var planStart = today.AddDays(1);
        var firstMonday = IsoWeek.MondayOf(planStart);
        var goalMonday = IsoWeek.MondayOf(goal.TargetDate);
        var totalWeeks = ((goalMonday.DayNumber - firstMonday.DayNumber) / 7) + 1;
        var taperWeeks = PlanEnvelopeRules.TaperWeeks(goal.DistanceMeters);
        var phases = Phases(totalWeeks, taperWeeks);

        var lastDayBeforeGoal = goal.TargetDate.AddDays(-1);
        var horizonEnd = firstMonday.AddDays((7 * (PlanEnvelopeRules.MaxHorizonWeeks - 1)) + 6);
        var planEnd = lastDayBeforeGoal < horizonEnd ? lastDayBeforeGoal : horizonEnd;
        var capped = planEnd < lastDayBeforeGoal;

        var maxSessions = Math.Clamp(
            (int)Math.Ceiling(history.SessionsPerWeek) + 1, PlanEnvelopeRules.MinSessionsPerWeek, PlanEnvelopeRules.MaxSessionsPerWeek);
        var v0 = Math.Max(history.MeanWeeklyDistanceMeters, PlanEnvelopeRules.MinBaseVolumeMeters);
        var l0 = Math.Max(history.LongestRunMeters, PlanEnvelopeRules.LongRunFloorMeters);
        var longCeiling = PlanEnvelopeRules.LongRunCeilingMeters(goal.DistanceMeters);
        var factors = PlanEnvelopeRules.TaperFactors(taperWeeks);
        var taperOnly = totalWeeks <= taperWeeks + 1;
        var preTaperWeeks = taperOnly ? 0 : totalWeeks - taperWeeks;

        var weeks = new List<PlanWeek>();
        for (var k = 1; k <= totalWeeks && planEnd >= planStart; k++)
        {
            var monday = firstMonday.AddDays(7 * (k - 1));
            if (monday > planEnd)
            {
                break;
            }

            var phase = phases[k - 1];
            double cap;
            double longRun;
            if (phase == TrainingPhase.Taper)
            {
                var factor = factors[Math.Max(0, taperWeeks - 1 - (totalWeeks - k))];
                cap = Math.Min(v0 * Math.Pow(PlanEnvelopeRules.WeeklyGrowth, preTaperWeeks), PlanEnvelopeRules.WeeklyCeilingMeters) * factor;
                longRun = Math.Min(l0 * Math.Pow(PlanEnvelopeRules.WeeklyGrowth, preTaperWeeks), longCeiling) * factor;
            }
            else
            {
                cap = Math.Min(v0 * Math.Pow(PlanEnvelopeRules.WeeklyGrowth, k), PlanEnvelopeRules.WeeklyCeilingMeters);
                longRun = Math.Min(l0 * Math.Pow(PlanEnvelopeRules.WeeklyGrowth, k), longCeiling);
            }

            if (k == 1)
            {
                // A partial first week gets a proportional cap.
                cap *= (7 - (planStart.DayNumber - monday.DayNumber)) / 7.0;
            }

            var fixedDistance = 0.0;
            var fixedSessions = 0;
            var sunday = monday.AddDays(6);
            foreach (var c in commitments)
            {
                if (c.Kind == CommitmentKind.OtherGoal || c.Date < planStart || c.Date > planEnd || c.Date < monday || c.Date > sunday
                    || c.RunTypeId is not { } typeId || RunTypeCatalog.Find(typeId) is not { CountsTowardRunningDistance: true })
                {
                    continue;
                }

                fixedDistance += c.DistanceMeters ?? 0;
                fixedSessions++;
            }

            // Caps the model can hit: rounded down to the 500 m step (v2); the validator checks these same values.
            var maxDistance = PlanEnvelopeRules.FloorToStep(cap);
            var fixedRounded = Round(fixedDistance);
            weeks.Add(new PlanWeek(
                monday, phase, maxDistance, fixedRounded, PlanEnvelopeRules.FloorToStep(maxDistance - fixedRounded), PlanEnvelopeRules.FloorToStep(longRun),
                fixedSessions));
        }

        return new PlanEnvelope(
            PlanEnvelopeRules.Version,
            planStart,
            planEnd,
            capped,
            Math.Max(0, totalWeeks),
            taperWeeks,
            maxSessions,
            referencePaceSecondsPerKm,
            EasyPaceMin(referencePaceSecondsPerKm, history),
            Math.Round(v0, 1, MidpointRounding.AwayFromZero),
            weeks,
            PlanEnvelopeRules.AllowedRunTypeCodes);
    }

    private static double Round(double value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
