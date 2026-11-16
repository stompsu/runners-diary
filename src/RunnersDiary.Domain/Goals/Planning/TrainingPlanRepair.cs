using RunnersDiary.Domain.Calendar;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// The deterministic repair step between parsing and the plan-level verdict (ADR 0040 amendment "repair"). Local, mechanical issues never fail a
/// plan; each repair only makes the plan easier or safer, and everything else stays a violation (retry, then <c>plan-failed</c>).
/// <para>Order: <c>phase</c> set from the envelope week; item-level drops of hard sessions in the quiet window (<c>taperIntensity</c>) and of
/// too-fast targets (<c>pace</c>); <c>longRun</c> clamp; <c>weeklyVolume</c> scale. (The pace-spread clamp happens while a session is converted, in
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
