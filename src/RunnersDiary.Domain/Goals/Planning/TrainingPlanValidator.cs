using System.Globalization;
using RunnersDiary.Domain.Calendar;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Insights.Narrative.Languages;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.RunTypes;

namespace RunnersDiary.Domain.Goals.Planning;

/// <summary>
/// Pure validation of a model plan against its fact sheet and envelope (ADR 0040 §5). It runs on every model answer before anything is stored,
/// and on the demo plan fixture. The model chooses sessions and words; every number it writes must stay inside bounds computed by code.
/// <para>Order: <c>schema</c> (as a whole, then per session; a bad session is left out of the later rules), <c>dateRange</c>, the item-level
/// <c>collision</c> drop (counted, never a violation), then the plan-level rules on the kept sessions, and <c>empty</c> when nothing is left.</para>
/// <para>Interpretations of ADR 0040 §5 where it leaves room: rest suggestions count for neither the weekly volume nor the frequency nor the
/// <c>emptyWeek</c> rule (they are not running sessions); <c>hardSessions</c> counts a week only when it has a hard suggestion (hard commitments
/// alone are not the model's doing); hard commitments are races and the hard training types of <see cref="HardTypeCodes"/>.</para>
/// </summary>
public static class TrainingPlanValidator
{
    public const int NotesMaxLength = 600;
    public const int DescriptionMaxLength = TrainingSuggestion.DescriptionMaxLength;
    public const double Tolerance = 1.05;
    public const double MinPaceShareOfReference = 0.85;
    public const int TaperQuietDays = 3;

    /// <summary>Training types that count as hard when they are commitments (the history's hard types, ADR 0040 §3).</summary>
    public static readonly IReadOnlySet<string> HardTypeCodes = new HashSet<string>(StringComparer.Ordinal) { "tempo", "intervals", "racePace", "hillRepeats", "fartlek" };

    public static PlanValidation Validate(string? modelJson, PlanFactSheet sheet, PlanValidationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(policy);
        return TrainingPlanDraftJson.TryParse(modelJson) is { } draft
            ? Validate(draft, sheet, policy)
            : new PlanValidation([new PlanViolation(PlanRuleCode.Schema, "draft")], [], 0, null);
    }

    public static PlanValidation Validate(TrainingPlanDraft draft, PlanFactSheet sheet, PlanValidationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(policy);

        var envelope = sheet.Envelope;
        var violations = new List<PlanViolation>();
        var deny = DenyMatcher.Create(policy.Language, policy.DenyList, policy.LanguageDenyLists);

        var notes = draft.Notes.Trim();
        if (draft.Notes.Length > NotesMaxLength)
        {
            violations.Add(new PlanViolation(PlanRuleCode.Schema, "notes"));
        }

        CheckText(draft.Notes, "notes", deny, violations);

        var maxSessions = 7 * envelope.Weeks.Count;
        if (draft.Sessions.Count < 1 || draft.Sessions.Count > maxSessions)
        {
            violations.Add(new PlanViolation(PlanRuleCode.Schema, "sessions"));
        }

        // Per session: shape, then the plan range.
        var candidates = new List<PlannedSession>();
        var repairs = new List<PlanRepair>();
        for (var i = 0; i < draft.Sessions.Count; i++)
        {
            var location = $"sessions[{i.ToString(CultureInfo.InvariantCulture)}]";
            var session = Convert(draft.Sessions[i], location, IsTrailGoal(sheet), deny, violations, repairs);
            if (session is null)
            {
                continue;
            }

            if (session.Date < envelope.PlanStart || session.Date > envelope.PlanEnd)
            {
                violations.Add(new PlanViolation(PlanRuleCode.DateRange, location));
                continue;
            }

            candidates.Add(session);
        }

        // Item level: a session on a blocked day is dropped and counted (never a retry).
        var (collisionKept, dropped) = DropCollisions(candidates, sheet.Commitments);

        // Repair (local, mechanical issues never fail a plan): phase from the week, hard-in-taper and too-fast sessions dropped, caps scaled.
        var repaired = TrainingPlanRepair.Apply(collisionKept, sheet);
        var kept = repaired.Sessions.ToList();
        dropped += repaired.Dropped;
        repairs.AddRange(repaired.Repairs);

        CheckSessionsPerDay(kept, violations);
        CheckSessions(kept, sheet, violations);
        CheckWeeks(kept, sheet, violations);
        CheckHardAdjacency(kept, sheet, violations);

        if (kept.Count == 0)
        {
            violations.Add(new PlanViolation(PlanRuleCode.Empty, "sessions"));
        }

        return new PlanValidation(
            Distinct(violations),
            [.. kept.OrderBy(s => s.Date)],
            dropped,
            notes.Length == 0 ? null : notes)
        {
            Repairs = repairs,
        };
    }

    /// <summary>
    /// The item-level <c>collision</c> rule: drops sessions on a date that already has a running or rest commitment (Race, Training or rest type,
    /// planned or completed) or another goal's target date. Cross-training and Body Bar do not block a day. Also re-applied at commit time
    /// against fresh commitments (ADR 0040 §1 step 10).
    /// </summary>
    public static (List<PlannedSession> Kept, int Dropped) DropCollisions(IReadOnlyList<PlannedSession> sessions, IReadOnlyList<CommitmentFact> commitments)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(commitments);
        var blocked = BlockedDates(commitments);
        var kept = sessions.Where(s => !blocked.Contains(s.Date)).ToList();
        return (kept, sessions.Count - kept.Count);
    }

    /// <summary>Dates no suggestion may use (see <see cref="DropCollisions"/>).</summary>
    public static HashSet<DateOnly> BlockedDates(IReadOnlyList<CommitmentFact> commitments)
    {
        ArgumentNullException.ThrowIfNull(commitments);
        return
        [
            .. commitments
                .Where(c => c.Kind == CommitmentKind.OtherGoal
                    || (c.RunTypeId is { } id && RunTypeCatalog.Find(id) is { } type && (type.CountsTowardRunningDistance || type.IsRestDay)))
                .Select(c => c.Date),
        ];
    }

    /// <summary>A race or a hard training type (<see cref="HardTypeCodes"/>).</summary>
    public static bool IsHardCommitment(CommitmentFact commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        return commitment.RunTypeId is { } id && RunTypeCatalog.Find(id) is { } type
            && (type.Category == RunTypeCategory.Race || HardTypeCodes.Contains(type.Code));
    }

    /// <summary>The goal's surface is Trail: easy/recovery runs get the wider terrain pace range (<see cref="TrainingSuggestion.MaxPaceSpreadSeconds"/>).</summary>
    public static bool IsTrailGoal(PlanFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return sheet.Goal.Surface == Surface.Trail;
    }

    /// <summary>A hard session in the last <see cref="TaperQuietDays"/> days before the goal (only when the goal is inside the horizon).</summary>
    internal static bool IsHardInQuietWindow(PlannedSession s, PlanFactSheet sheet) =>
        !sheet.Envelope.HorizonCapped && s.Intensity.IsHard()
        && s.Date >= sheet.Goal.TargetDate.AddDays(-TaperQuietDays) && s.Date < sheet.Goal.TargetDate;

    /// <summary>The target pace's faster bound is faster than the reference pace allows (85 %; easy and recovery: not faster than the reference).</summary>
    internal static bool IsTooFast(PlannedSession s, int? reference) =>
        reference is { } r && s.TargetPaceMinSecondsPerKm is { } min
        && (min < MinPaceShareOfReference * r || (s.Intensity is TrainingIntensity.Easy or TrainingIntensity.Recovery && min < r));

    private static PlannedSession? Convert(
        DraftSession s, string location, bool trailGoal, DenyMatcher deny, List<PlanViolation> violations, List<PlanRepair> repairs)
    {
        var before = violations.Count;
        var paceMax = s.PaceMaxSecondsPerKm;
        PlanRepair? paceClamp = null;
        void Fail(string member, int? value = null, int? limit = null) =>
            violations.Add(new PlanViolation(PlanRuleCode.Schema, location + "." + member, value, limit));

        var dateOk = DateOnly.TryParseExact(s.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
        if (!dateOk)
        {
            Fail("date");
        }

        var type = PlanEnvelopeRules.AllowedRunTypeCodes.Contains(s.RunType, StringComparer.Ordinal) ? RunTypeCatalog.FindByCode(s.RunType) : null;
        if (type is null)
        {
            Fail("runType");
        }

        if (!TrainingIntensities.TryFromWire(s.Intensity, out var intensity))
        {
            Fail("intensity");
        }

        if (!TrainingPhases.TryFromWire(s.Phase, out var phase))
        {
            // The value must still be a known code (strict schema); whether it is the right one no longer matters, the repair step sets it from the week.
            Fail("phase");
        }

        if (string.IsNullOrWhiteSpace(s.Description) || s.Description.Length > DescriptionMaxLength)
        {
            Fail("description");
        }

        CheckText(s.Description, location + ".description", deny, violations);

        var isRest = type?.IsRestDay == true;
        if (type is not null && TrainingIntensities.TryFromWire(s.Intensity, out _) && isRest != (intensity == TrainingIntensity.Rest))
        {
            Fail("intensity");
        }

        if (isRest)
        {
            if (s.DistanceMeters is not null || s.DurationSeconds is not null || s.PaceMinSecondsPerKm is not null || s.PaceMaxSecondsPerKm is not null)
            {
                Fail("distanceMeters");
            }
        }
        else
        {
            if (s.DistanceMeters is not { } distance || distance < TrainingSuggestion.MinDistanceMeters || distance > TrainingSuggestion.MaxDistanceMeters)
            {
                Fail("distanceMeters");
            }

            if (s.DurationSeconds is { } seconds && seconds is < TrainingSuggestion.MinDurationSeconds or > TrainingSuggestion.MaxDurationSeconds)
            {
                Fail("durationSeconds");
            }
            else if (!TrainingSuggestion.HasPlausibleImpliedPace(s.DistanceMeters, s.DurationSeconds))
            {
                // Each value in range is not enough: 30 km in 10 min would become an impossible planned run on Accept.
                Fail("durationSeconds");
            }

            if ((s.PaceMinSecondsPerKm is null) != (s.PaceMaxSecondsPerKm is null))
            {
                Fail(s.PaceMinSecondsPerKm is null ? "paceMinSecondsPerKm" : "paceMaxSecondsPerKm");
            }
            else if (s.PaceMinSecondsPerKm is { } min && s.PaceMaxSecondsPerKm is { } max)
            {
                if (min < GoalRules.MinPaceSecondsPerKm || min > GoalRules.MaxPaceSecondsPerKm)
                {
                    Fail("paceMinSecondsPerKm");
                }
                else if (max > GoalRules.MaxPaceSecondsPerKm || min > max)
                {
                    Fail("paceMaxSecondsPerKm");
                }
                else if (TrainingSuggestion.MaxPaceSpreadSeconds(type?.Id ?? 0, trailGoal) is var spreadLimit && max > min + spreadLimit)
                {
                    // Repair (ADR 0040 amendment "repair"): a range too wide for the run type gets its slower bound clamped to min + the allowed spread.
                    // Only makes the target tighter, never looser; recorded only when the session survives the other checks.
                    paceMax = min + spreadLimit;
                    paceClamp = new PlanRepair(PlanRuleCode.Schema, location + ".paceMaxSecondsPerKm", PlanRepairAction.Clamp, max, paceMax);
                }
            }
        }

        // Markup and deny-list findings do not stop a session from being checked further; shape errors do.
        if (violations.Skip(before).Any(v => v.Rule == PlanRuleCode.Schema) || !dateOk || type is null)
        {
            return null;
        }

        if (paceClamp is not null)
        {
            repairs.Add(paceClamp);
        }

        return new PlannedSession(
            date, type.Id, s.DistanceMeters, s.DurationSeconds, intensity, s.PaceMinSecondsPerKm, paceMax, s.Description.Trim(), phase);
    }

    private static void CheckText(string text, string location, DenyMatcher deny, List<PlanViolation> violations)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (SummaryValidator.HasMarkup(text))
        {
            violations.Add(new PlanViolation(PlanRuleCode.Markup, location));
        }

        if (deny.Matches(NarrativeLanguages.Words(text, deny.Language)))
        {
            violations.Add(new PlanViolation(PlanRuleCode.ForbiddenTopic, location));
        }
    }

    private static void CheckSessionsPerDay(List<PlannedSession> kept, List<PlanViolation> violations)
    {
        foreach (var day in kept.GroupBy(s => s.Date).Where(g => g.Count() > 1).OrderBy(g => g.Key))
        {
            violations.Add(new PlanViolation(PlanRuleCode.SessionsPerDay, Format(day.Key)));
        }
    }

    private static void CheckSessions(List<PlannedSession> kept, PlanFactSheet sheet, List<PlanViolation> violations)
    {
        var envelope = sheet.Envelope;
        var weeks = envelope.Weeks.ToDictionary(w => w.Start);
        var reference = envelope.ReferencePaceSecondsPerKm;

        foreach (var s in kept.OrderBy(s => s.Date))
        {
            var where = Format(s.Date);
            if (weeks.GetValueOrDefault(IsoWeek.MondayOf(s.Date)) is not { } week)
            {
                // Unreachable for a session inside [planStart, planEnd]; a defensive range violation.
                violations.Add(new PlanViolation(PlanRuleCode.DateRange, where));
                continue;
            }

            if (s.DistanceMeters is { } distance && distance > week.MaxLongRunMeters * Tolerance)
            {
                violations.Add(new PlanViolation(PlanRuleCode.LongRun, where, Meters(distance), Meters(week.MaxLongRunMeters)));
            }

            // taperIntensity and pace are item-level repairs now (the sessions are dropped before this point); kept as a safety net.
            if (IsHardInQuietWindow(s, sheet))
            {
                violations.Add(new PlanViolation(PlanRuleCode.TaperIntensity, where));
            }

            if (IsTooFast(s, reference))
            {
                violations.Add(new PlanViolation(PlanRuleCode.Pace, where));
            }
        }
    }

    private static void CheckWeeks(List<PlannedSession> kept, PlanFactSheet sheet, List<PlanViolation> violations)
    {
        var envelope = sheet.Envelope;
        var hardLimit = envelope.MaxSessionsPerWeek >= 6 ? 3 : 2;
        foreach (var week in envelope.Weeks)
        {
            var sunday = week.Start.AddDays(6);
            var inWeek = kept.Where(s => s.Date >= week.Start && s.Date <= sunday).ToList();
            var running = inWeek.Where(s => s.Intensity != TrainingIntensity.Rest).ToList();
            var where = Format(week.Start);

            var suggested = running.Sum(s => s.DistanceMeters ?? 0);
            if (suggested > 0 && week.FixedDistanceMeters + suggested > week.MaxDistanceMeters * Tolerance)
            {
                violations.Add(new PlanViolation(PlanRuleCode.WeeklyVolume, where, Meters(suggested), Meters(week.AvailableDistanceMeters)));
            }

            if (running.Count > 0 && week.FixedRunningSessions + running.Count > envelope.MaxSessionsPerWeek)
            {
                violations.Add(new PlanViolation(PlanRuleCode.Frequency, where));
            }

            var hardSuggestions = running.Count(s => s.Intensity.IsHard());
            if (hardSuggestions > 0)
            {
                var hardCommitments = sheet.Commitments.Count(c => c.Date >= week.Start && c.Date <= sunday && IsHardCommitment(c));
                if (hardSuggestions + hardCommitments > hardLimit)
                {
                    violations.Add(new PlanViolation(PlanRuleCode.HardSessions, where));
                }
            }

            var full = week.Start >= envelope.PlanStart && sunday <= envelope.PlanEnd;
            if (full && week.Phase != TrainingPhase.Taper && week.FixedRunningSessions == 0 && running.Count == 0)
            {
                violations.Add(new PlanViolation(PlanRuleCode.EmptyWeek, where));
            }
        }
    }

    private static void CheckHardAdjacency(List<PlannedSession> kept, PlanFactSheet sheet, List<PlanViolation> violations)
    {
        var hardSuggestionDates = kept.Where(s => s.Intensity.IsHard()).Select(s => s.Date).ToHashSet();
        var hardCommitmentDates = sheet.Commitments.Where(IsHardCommitment).Select(c => c.Date).ToHashSet();
        foreach (var date in hardSuggestionDates.Order())
        {
            foreach (var neighbour in new[] { date.AddDays(-1), date.AddDays(1) })
            {
                if (hardSuggestionDates.Contains(neighbour) || hardCommitmentDates.Contains(neighbour))
                {
                    violations.Add(new PlanViolation(PlanRuleCode.HardSessions, Format(date)));
                    break;
                }
            }
        }
    }

    private static List<PlanViolation> Distinct(List<PlanViolation> violations) => [.. violations.Distinct()];

    internal static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static int Meters(double meters) => (int)Math.Clamp(Math.Round(meters, MidpointRounding.AwayFromZero), 0, int.MaxValue);
}
