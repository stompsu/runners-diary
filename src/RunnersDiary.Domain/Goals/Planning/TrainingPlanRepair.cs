using RunnersDiary.Domain.Calendar;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// The deterministic repair step between parsing and the plan-level verdict (ADR 0040 amendment "repair"). Local, mechanical issues never fail a
/// plan; each repair only makes the plan easier or safer, and everything else stays a violation (retry, then <c>plan-failed</c>).
/// <para>Order: <c>phase</c> set from the envelope week; item-level drops of hard sessions in the quiet window (<c>taperIntensity</c>) and of
/// too-fast targets (<c>pace</c>), the <c>easyPace</c> clamp (M9.1), then the <c>frequency</c> drops of an over-full week; <c>longRun</c> clamp; <c>weeklyVolume</c> scale. (The pace-spread clamp happens while a session is converted, in
/// <see cref="TrainingPlanValidator"/>.) Long runs are clamped before the weekly sum is judged, so a week that overshoots only because of one long run
/// can be rescued. A volume or long-run overshoot of more than <see cref="MaxRepairedOvershoot"/> above the tolerant cap is not repaired. A repair that
/// would push a session out of its own range (distance below 1,000 m, duration below 10 min, implied pace out of bounds) is skipped, so the
/// original violation remains. Idempotent: running it over its own output changes nothing.</para>
/// </summary>
public static class TrainingPlanRepair
{
    /// <summary>The largest overshoot, as a share above the tolerant cap (cap x <see cref="TrainingPlanValidator.Tolerance"/>), that is scaled down instead of rejected.</summary>
    public const double MaxRepairedOvershoot = 0.25;

    private const double DistanceStepMeters = 100;

    public static RepairResult Apply(IReadOnlyList<PlannedSession> sessions, PlanFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(sheet);
        var envelope = sheet.Envelope;
        var weeks = envelope.Weeks.ToDictionary(w => w.Start);
        var repairs = new List<PlanRepair>();
        var dropped = 0;
        var result = new List<PlannedSession>(sessions.Count);

        // 1. phase, 3-4. item-level drops (taperIntensity, then pace).
        foreach (var original in sessions.OrderBy(s => s.Date))
        {
            var s = original;
            var where = TrainingPlanValidator.Format(s.Date);
            if (weeks.GetValueOrDefault(IsoWeek.MondayOf(s.Date)) is { } week && s.Phase != week.Phase)
            {
                repairs.Add(new PlanRepair(PlanRuleCode.Phase, where, PlanRepairAction.Set, (int)s.Phase, (int)week.Phase));
                s = s with { Phase = week.Phase };
            }

            if (TrainingPlanValidator.IsHardInQuietWindow(s, sheet))
            {
                repairs.Add(new PlanRepair(PlanRuleCode.TaperIntensity, where, PlanRepairAction.Drop));
                dropped++;
                continue;
            }

            if (TrainingPlanValidator.IsTooFast(s, envelope.ReferencePaceSecondsPerKm))
            {
                repairs.Add(new PlanRepair(PlanRuleCode.Pace, where, PlanRepairAction.Drop));
                dropped++;
                continue;
            }

            result.Add(s);
        }

        // 4a. easyPace: clamp easy/recovery sessions to the envelope's easy-pace bound (slower is the safe direction); drop when that leaves the session's range.
        var trailGoal = TrainingPlanValidator.IsTrailGoal(sheet);
        for (var i = result.Count - 1; i >= 0; i--)
        {
            var s = result[i];
            if (!TrainingPlanValidator.IsEasyTooFast(s, envelope.EasyPaceMinSecondsPerKm))
            {
                continue;
            }

            var where = TrainingPlanValidator.Format(s.Date);
            if (ClampEasyPace(s, envelope.EasyPaceMinSecondsPerKm!.Value, trailGoal) is { } slower)
            {
                repairs.Add(new PlanRepair(PlanRuleCode.EasyPace, where, PlanRepairAction.Clamp, s.TargetPaceMinSecondsPerKm, slower.TargetPaceMinSecondsPerKm));
                result[i] = slower;
            }
            else
            {
                repairs.Add(new PlanRepair(PlanRuleCode.EasyPace, where, PlanRepairAction.Drop, s.TargetPaceMinSecondsPerKm, envelope.EasyPaceMinSecondsPerKm));
                result.RemoveAt(i);
                dropped++;
            }
        }

        // The clamp loop ran back to front; keep the repair log in date order.
        var easyLog = repairs.Where(r => r.Rule == PlanRuleCode.EasyPace).OrderBy(r => r.Location, StringComparer.Ordinal).ToList();
        repairs.RemoveAll(r => r.Rule == PlanRuleCode.EasyPace);
        repairs.AddRange(easyLog);

        // 4b. frequency: drop the excess running sessions of an over-full week (fixed commitments + suggestions above MaxSessionsPerWeek).
        foreach (var week in envelope.Weeks)
        {
            var sunday = week.Start.AddDays(6);
            var running = result.Where(s => s.Intensity != TrainingIntensity.Rest && s.Date >= week.Start && s.Date <= sunday).ToList();
            var excess = week.FixedRunningSessions + running.Count - envelope.MaxSessionsPerWeek;
            if (running.Count == 0 || excess <= 0)
            {
                continue;
            }

            // The longest run (earliest on equal distance) goes last; the rest by priority: recovery, easy, other non-hard, hard; later date first.
            var longest = running.OrderByDescending(s => s.DistanceMeters ?? 0).ThenBy(s => s.Date).First();
            var order = running.Where(s => !ReferenceEquals(s, longest))
                .OrderBy(s => s.Intensity switch
                {
                    TrainingIntensity.Recovery => 0,
                    TrainingIntensity.Easy => 1,
                    _ when !s.Intensity.IsHard() => 2,
                    _ => 3,
                })
                .ThenByDescending(s => s.Date)
                .Append(longest)
                .Take(excess)
                .ToList();
            var gone = new HashSet<PlannedSession>(order, ReferenceEqualityComparer.Instance);
            result.RemoveAll(gone.Contains);
            dropped += order.Count;
            repairs.Add(new PlanRepair(PlanRuleCode.Frequency, TrainingPlanValidator.Format(week.Start), PlanRepairAction.Drop, running.Count, running.Count - order.Count));
        }

        // 5a. longRun: clamp one session to the week's cap.
        for (var i = 0; i < result.Count; i++)
        {
            var s = result[i];
            if (s.DistanceMeters is not { } distance || weeks.GetValueOrDefault(IsoWeek.MondayOf(s.Date)) is not { } week)
            {
                continue;
            }

            var tolerant = week.MaxLongRunMeters * TrainingPlanValidator.Tolerance;
            if (distance > tolerant && distance <= tolerant * (1 + MaxRepairedOvershoot)
                && Resize(s, Math.Floor(week.MaxLongRunMeters / DistanceStepMeters) * DistanceStepMeters) is { } smaller)
            {
                repairs.Add(new PlanRepair(
                    PlanRuleCode.LongRun, TrainingPlanValidator.Format(s.Date), PlanRepairAction.Clamp,
                    TrainingPlanValidator.Meters(distance), TrainingPlanValidator.Meters(smaller.DistanceMeters!.Value)));
                result[i] = smaller;
            }
        }

        // 5b. weeklyVolume: scale the week's suggested sessions down to availableDistanceMeters.
        foreach (var week in envelope.Weeks)
        {
            var sunday = week.Start.AddDays(6);
            var indexes = Enumerable.Range(0, result.Count)
                .Where(i => result[i].Intensity != TrainingIntensity.Rest && result[i].DistanceMeters is > 0 && result[i].Date >= week.Start && result[i].Date <= sunday)
                .ToList();
            var suggested = indexes.Sum(i => result[i].DistanceMeters!.Value);
            var tolerant = week.MaxDistanceMeters * TrainingPlanValidator.Tolerance;
            var total = week.FixedDistanceMeters + suggested;
            if (suggested <= 0 || total <= tolerant || total > tolerant * (1 + MaxRepairedOvershoot))
            {
                continue;
            }

            var factor = week.AvailableDistanceMeters / suggested;
            var scaled = indexes.Select(i => Resize(result[i], Math.Floor(result[i].DistanceMeters!.Value * factor / DistanceStepMeters) * DistanceStepMeters)).ToList();
            if (scaled.Any(s => s is null))
            {
                continue;
            }

            for (var k = 0; k < indexes.Count; k++)
            {
                result[indexes[k]] = scaled[k]!;
            }

            repairs.Add(new PlanRepair(
                PlanRuleCode.WeeklyVolume, TrainingPlanValidator.Format(week.Start), PlanRepairAction.Scale,
                TrainingPlanValidator.Meters(suggested), TrainingPlanValidator.Meters(scaled.Sum(s => s!.DistanceMeters!.Value))));
        }

        return new RepairResult([.. result.OrderBy(s => s.Date)], dropped, repairs);
    }

    /// <summary>
    /// The easy/recovery session with its faster pace bound raised to <paramref name="bound"/>, the slower bound kept inside the run type's spread and
    /// a duration whose implied pace is faster than the new bound raised to <c>ceil(distance in km × bound)</c>. Null (the session is dropped) when
    /// the bound or the duration leaves the session's own range.
    /// </summary>
    private static PlannedSession? ClampEasyPace(PlannedSession s, int bound, bool trailGoal)
    {
        if (s.TargetPaceMinSecondsPerKm is null || s.TargetPaceMaxSecondsPerKm is not { } oldMax || bound > GoalRules.MaxPaceSecondsPerKm)
        {
            return null;
        }

        var spread = TrainingSuggestion.MaxPaceSpreadSeconds(s.RunTypeId, trailGoal);
        var max = Math.Min(Math.Clamp(oldMax, bound, bound + spread), GoalRules.MaxPaceSecondsPerKm);
        var duration = s.DurationSeconds;
        if (s.DistanceMeters is { } distance && duration is { } seconds && seconds * 1000.0 < bound * distance)
        {
            duration = (int)Math.Ceiling((distance / 1000 * bound) - 1e-9);
            if (duration > TrainingSuggestion.MaxDurationSeconds || !TrainingSuggestion.HasPlausibleImpliedPace(distance, duration))
            {
                return null;
            }
        }

        return s with { TargetPaceMinSecondsPerKm = bound, TargetPaceMaxSecondsPerKm = max, DurationSeconds = duration };
    }

    /// <summary>
    /// The session with a new, smaller distance and its duration scaled in proportion (whole seconds, so the implied pace is kept), or null when the
    /// result would leave the session's own bounds (then the repair is skipped and the violation stands).
    /// </summary>
    private static PlannedSession? Resize(PlannedSession s, double distanceMeters)
    {
        if (!(distanceMeters >= TrainingSuggestion.MinDistanceMeters) || s.DistanceMeters is not { } old || distanceMeters > old)
        {
            return null;
        }

        int? duration = s.DurationSeconds is { } seconds ? (int)Math.Round(seconds * distanceMeters / old, MidpointRounding.AwayFromZero) : null;
        if (duration is < TrainingSuggestion.MinDurationSeconds || !TrainingSuggestion.HasPlausibleImpliedPace(distanceMeters, duration))
        {
            return null;
        }

        return s with { DistanceMeters = distanceMeters, DurationSeconds = duration };
    }
}

/// <param name="Sessions">The repaired sessions by date.</param>
/// <param name="Dropped">Sessions left out by the item-level repairs (<c>taperIntensity</c>, <c>pace</c>).</param>
/// <param name="Repairs">Every repair made, in order.</param>
public sealed record RepairResult(IReadOnlyList<PlannedSession> Sessions, int Dropped, IReadOnlyList<PlanRepair> Repairs);
