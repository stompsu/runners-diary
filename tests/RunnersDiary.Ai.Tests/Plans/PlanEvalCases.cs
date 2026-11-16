using System.Globalization;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Ai.Tests.Plans;

/// <summary>
/// One plan evaluation case (ADR 0040 §7, plan §9): a fixed fact sheet, what the inputs must look like, a corruption of a good plan that the
/// validator must reject with a given rule code (CI mode), and what every accepted plan must satisfy (CI and live mode).
/// </summary>
/// <param name="AssertInputs">Checks of the deterministic inputs (estimate warnings, envelope shape).</param>
/// <param name="Corrupt">Turns a valid plan into one that breaks a rule, and names the rule.</param>
/// <param name="AssertPlan">Holds for every accepted plan of the case (the model's or the fake's).</param>
/// <param name="Extra">Sessions the scripted "model" adds to a good plan on blocked days (collision case): they must be dropped and counted.</param>
internal sealed record PlanEvalCase(
    string Name,
    PlanFactSheet Sheet,
    Action<PlanFactSheet> AssertInputs,
    Func<TrainingPlanDraft, PlanFactSheet, (TrainingPlanDraft Draft, PlanRuleCode Rule)> Corrupt,
    Action<PlanValidation, PlanFactSheet> AssertPlan,
    Func<PlanFactSheet, IReadOnlyList<DraftSession>>? Extra = null)
{
    public override string ToString() => Name;
}

internal static class PlanEvalCases
{
    public const string HalfMarathon10Weeks = "half-marathon-10-weeks";
    public const string Ambitious10k = "ambitious-10k-under-40";
    public const string Trail44k = "trail-44k-1000m";
    public const string TaperOnly = "goal-3-weeks-taper-only";
    public const string Collisions = "collisions-with-planned-runs";
    public const string CappedHorizon = "capped-horizon-30-weeks";

    private static readonly DateOnly Today = PlanSheets.Today;

    public static IReadOnlyList<PlanEvalCase> All { get; } = Build();

    public static PlanEvalCase Get(string name) => All.Single(c => c.Name == name);

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var c in All)
        {
            data.Add(c.Name);
        }

        return data;
    }

    public static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<PlanEvalCase> Build()
    {
        var (log, races) = PlanSheets.RecreationalLog(Today);

        // 1. Half marathon in 10 weeks (Sunday 2027-01-24), target 1:50:00.
        var half = new GoalSpec(GoalType.Performance, 21_097, null, new DateOnly(2027, 1, 24), 6_600, Surface.Road);
        var halfCase = new PlanEvalCase(
            HalfMarathon10Weeks,
            PlanSheets.Build(half, Today, log, races),
            sheet =>
            {
                var e = sheet.Envelope;
                Assert.False(e.HorizonCapped);
                Assert.Equal(10, e.Weeks.Count);
                Assert.Equal([TrainingPhase.Taper, TrainingPhase.Taper], e.Weeks.TakeLast(2).Select(w => w.Phase));
                Assert.Equal(313, e.ReferencePaceSecondsPerKm); // the target pace
            },
            (draft, sheet) =>
            {
                // Triple one week's cap across its sessions: weekly volume, far beyond what the repair step scales down (25 %).
                var week = sheet.Envelope.Weeks[4];
                var inWeek = draft.Sessions.Count(s => InWeek(s, week));
                var each = Math.Min(59_000, Math.Floor(week.MaxDistanceMeters * 3 / inWeek));
                return (draft with { Sessions = [.. draft.Sessions.Select(s => InWeek(s, week) ? s with { DistanceMeters = each, DurationSeconds = null } : s)] }, PlanRuleCode.WeeklyVolume);
            },
            (plan, sheet) => AssertEveryFullTrainingWeekHasRunning(plan, sheet));

        // 2. An over-ambitious 10K: under 40:00 in 6 weeks with a 48:00 race result. The estimate warns; paces stay bounded by the target pace.
        var tenK = new GoalSpec(GoalType.Performance, 10_000, null, new DateOnly(2026, 12, 27), 2_390, Surface.Road);
        var tenKCase = new PlanEvalCase(
            Ambitious10k,
            PlanSheets.Build(tenK, Today, log, races),
            sheet =>
            {
                var estimate = GoalEstimator.Estimate(tenK, Today, log, races);
                Assert.Contains(estimate.Warnings, w => w.Code == EstimateWarningCode.TargetMuchFasterThanPredicted);
                Assert.Equal(239, sheet.Envelope.ReferencePaceSecondsPerKm);
                Assert.True(sheet.Goal.PredictedDurationSeconds > 2_700);
            },
            (draft, sheet) =>
            {
                // A long run 60 % above its week's long-run cap (the repair step only clamps up to 25 % above the tolerant cap).
                var sessions = draft.Sessions.ToList();
                var i = sessions.FindLastIndex(s => s.RunType == "long");
                var week = WeekOf(sheet, Date(sessions[i].Date));
                sessions[i] = sessions[i] with { DistanceMeters = Math.Min(59_000, Math.Ceiling(week.MaxLongRunMeters * 1.6)), DurationSeconds = null };
                return (draft with { Sessions = sessions }, PlanRuleCode.LongRun);
            },
            (plan, sheet) => Assert.All(
                plan.Kept.Where(s => s.TargetPaceMinSecondsPerKm is not null),
                s => Assert.True(s.TargetPaceMinSecondsPerKm >= 0.85 * sheet.Envelope.ReferencePaceSecondsPerKm!.Value)));

        // 3. Trail 44 km / 1,000 m in 14 weeks: the long-run ceiling is min(0.6 x 44 km, 50 km) = 26.4 km.
        var trail = new GoalSpec(GoalType.Distance, 44_000, 1_000, new DateOnly(2027, 2, 21), null, Surface.Trail);
        var trailCase = new PlanEvalCase(
            Trail44k,
            PlanSheets.Build(trail, Today, log, races),
            sheet =>
            {
                Assert.All(sheet.Envelope.Weeks, w => Assert.True(w.MaxLongRunMeters <= 26_400, $"{w.Start}: {w.MaxLongRunMeters}"));
                Assert.Equal(3, sheet.Envelope.TaperWeeks);
                Assert.Equal("trail", PlanFactSheetJsonProbe.Goal(sheet, "surface"));
            },
            (draft, sheet) =>
            {
                var sessions = draft.Sessions.ToList();
                var i = sessions.FindLastIndex(s => s.RunType == "long");
                var week = WeekOf(sheet, Date(sessions[i].Date));
                sessions[i] = sessions[i] with { DistanceMeters = Math.Ceiling(week.MaxLongRunMeters * 1.05 * 1.5), DurationSeconds = null };
                return (draft with { Sessions = sessions }, PlanRuleCode.LongRun);
            },
            (plan, sheet) => Assert.All(plan.Kept, s => Assert.True((s.DistanceMeters ?? 0) <= 26_400 * TrainingPlanValidator.Tolerance)));

        // 4. A half marathon 3 weeks away (Sunday 2026-12-06): W = 3 <= T + 1, so every week is taper; no hard session in the last 3 days.
        var close = new GoalSpec(GoalType.Distance, 21_097, null, new DateOnly(2026, 12, 6), null, Surface.Road);
        var closeCase = new PlanEvalCase(
            TaperOnly,
            PlanSheets.Build(close, Today, log, races),
            sheet => Assert.All(sheet.Envelope.Weeks, w => Assert.Equal(TrainingPhase.Taper, w.Phase)),
            (draft, sheet) =>
            {
                // Two hard sessions on consecutive days (a hard session in the quiet window is dropped by the repair step, so it cannot be the corruption).
                var first = sheet.Envelope.PlanStart;
                var sessions = draft.Sessions.Where(s => Date(s.Date) != first && Date(s.Date) != first.AddDays(1)).ToList();
                sessions.Add(new DraftSession(D(first), "tempo", "tempo", "taper", 5_000, null, null, null, "Short tempo run."));
                sessions.Add(new DraftSession(D(first.AddDays(1)), "tempo", "tempo", "taper", 5_000, null, null, null, "Short tempo run."));
                return (draft with { Sessions = [.. sessions.OrderBy(s => s.Date, StringComparer.Ordinal)] }, PlanRuleCode.HardSessions);
            },
            (plan, sheet) => Assert.DoesNotContain(plan.Kept, s => s.Intensity.IsHard() && s.Date >= sheet.Goal.TargetDate.AddDays(-3)));

        // 5. Collisions: the half marathon with planned runs already in the diary (Tuesdays, a Body Bar class and a planned 10K race).
        var planned = new List<RunFact>(log)
        {
            PlanSheets.Planned(new DateOnly(2026, 11, 24), 20, 8_000, 1),
            PlanSheets.Planned(new DateOnly(2026, 12, 1), 20, 8_000, 2),
            PlanSheets.Planned(new DateOnly(2026, 12, 10), 42, null, 3),
            PlanSheets.Planned(new DateOnly(2026, 12, 13), 1, 10_000, 4),
        };
        var collisionCase = new PlanEvalCase(
            Collisions,
            PlanSheets.Build(half, Today, planned, races),
            sheet =>
            {
                Assert.Equal(4, sheet.Commitments.Count);
                Assert.Single(sheet.Commitments, c => c.Kind == CommitmentKind.Race);
                Assert.True(sheet.Envelope.Weeks[1].FixedDistanceMeters >= 8_000);
            },
            (draft, sheet) =>
            {
                // A hard session the day after the planned race.
                var monday = new DateOnly(2026, 12, 14);
                var sessions = draft.Sessions.Where(s => Date(s.Date) != monday).ToList();
                sessions.Add(new DraftSession(D(monday), "intervals", "interval", WeekOf(sheet, monday).Phase.ToWire(), 6_000, null, null, null, "Intervals."));
                return (draft with { Sessions = [.. sessions.OrderBy(s => s.Date, StringComparer.Ordinal)] }, PlanRuleCode.HardSessions);
            },
            (plan, sheet) =>
            {
                var blocked = TrainingPlanValidator.BlockedDates(sheet.Commitments);
                Assert.DoesNotContain(plan.Kept, s => blocked.Contains(s.Date));
                Assert.Contains(new DateOnly(2026, 12, 10), sheet.Commitments.Select(c => c.Date)); // Body Bar is a commitment that does not block
                Assert.DoesNotContain(new DateOnly(2026, 12, 10), blocked);
            },
            sheet =>
            [
                new DraftSession("2026-11-24", "easy", "easy", WeekOf(sheet, new DateOnly(2026, 11, 24)).Phase.ToWire(), 5_000, null, null, null, "Easy run."),
                new DraftSession("2026-12-13", "long", "easy", WeekOf(sheet, new DateOnly(2026, 12, 13)).Phase.ToWire(), 10_000, null, null, null, "Long run."),
            ]);

        // 6. A marathon 30 weeks away: the horizon is capped at 16 weeks, so there is no taper in it.
        var marathon = new GoalSpec(GoalType.Distance, 42_195, null, Today.AddDays(30 * 7 + 6), null, Surface.Road);
        var cappedCase = new PlanEvalCase(
            CappedHorizon,
            PlanSheets.Build(marathon, Today, log, races),
            sheet =>
            {
                var e = sheet.Envelope;
                Assert.True(e.HorizonCapped);
                Assert.Equal(16, e.Weeks.Count);
                Assert.Equal(new DateOnly(2027, 3, 7), e.PlanEnd);
                Assert.DoesNotContain(e.Weeks, w => w.Phase == TrainingPhase.Taper);
            },
            (draft, sheet) =>
            {
                var sessions = draft.Sessions.ToList();
                sessions.Add(sessions[^1] with { Date = D(sheet.Envelope.PlanEnd.AddDays(1)) });
                return (draft with { Sessions = sessions }, PlanRuleCode.DateRange);
            },
            (plan, sheet) => Assert.All(plan.Kept, s => Assert.True(s.Date <= sheet.Envelope.PlanEnd)));

        return [halfCase, tenKCase, trailCase, closeCase, collisionCase, cappedCase];
    }

    public static bool InWeek(DraftSession session, PlanWeek week) => Date(session.Date) is var d && d >= week.Start && d <= week.Start.AddDays(6);

    public static PlanWeek WeekOf(PlanFactSheet sheet, DateOnly date) => sheet.Envelope.Weeks.Single(w => date >= w.Start && date <= w.Start.AddDays(6));

    private static void AssertEveryFullTrainingWeekHasRunning(PlanValidation plan, PlanFactSheet sheet)
    {
        var e = sheet.Envelope;
        foreach (var w in e.Weeks.Where(w => w.Phase != TrainingPhase.Taper && w.Start >= e.PlanStart && w.Start.AddDays(6) <= e.PlanEnd))
        {
            Assert.Contains(plan.Kept, s => s.Date >= w.Start && s.Date <= w.Start.AddDays(6) && s.Intensity != TrainingIntensity.Rest);
        }
    }
}

/// <summary>Reads a member of the canonical fact sheet JSON (what the model sees).</summary>
internal static class PlanFactSheetJsonProbe
{
    public static string? Goal(PlanFactSheet sheet, string member)
    {
        using var document = System.Text.Json.JsonDocument.Parse(PlanFactSheetJson.ToJson(sheet));
        return document.RootElement.GetProperty("goal").TryGetProperty(member, out var value) ? value.ToString() : null;
    }
}
