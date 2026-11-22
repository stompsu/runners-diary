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
    public const string DenseDiary = "dense-diary-like-demo";
    public const string EasyOnlyHalf4Weeks = "easy-only-hm-sub-1-40-4-weeks";
    public const string EasyOnlyHalf12Weeks = "easy-only-hm-sub-1-40-12-weeks";

    /// <summary>The M9.1 scenario's numbers (plan §3.1, §5): the fallback prediction 7,245 s at 343 s/km; 0.85 x 343 and 0.97 x 343, rounded up.</summary>
    public const int EasyOnlyPredictedSeconds = 7_245;
    public const int EasyOnlyReferencePace = 343;
    public const int EasyOnlyPaceFloor = 292;
    public const int EasyOnlyRacePaceFloor = 333;

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

        // 2. An over-ambitious 10K: under 40:00 in 6 weeks with a 48:00 race result. The estimate warns; since M9.1 the reference pace is the
        //    fitness pace (the race prediction, 276 s/km), not the 239 s/km target pace, and no session is faster than fitness allows.
        var tenK = new GoalSpec(GoalType.Performance, 10_000, null, new DateOnly(2026, 12, 27), 2_390, Surface.Road);
        var tenKCase = new PlanEvalCase(
            Ambitious10k,
            PlanSheets.Build(tenK, Today, log, races),
            sheet =>
            {
                var estimate = GoalEstimator.Estimate(tenK, Today, log, races);
                Assert.Contains(estimate.Warnings, w => w.Code == EstimateWarningCode.TargetMuchFasterThanPredicted);
                Assert.Equal(239, sheet.Goal.TargetPaceSecondsPerKm);
                Assert.Equal(276, sheet.Goal.FitnessPaceSecondsPerKm);
                Assert.Equal(sheet.Goal.FitnessPaceSecondsPerKm, sheet.Envelope.ReferencePaceSecondsPerKm); // the slower of 239 and 276
                Assert.True(sheet.Goal.PredictedDurationSeconds > 2_700);
                Assert.Equal((EffortKind.Race, PredictionConfidence.High, true), (sheet.Goal.PredictionBasis, sheet.Goal.PredictionConfidence, sheet.Goal.AmbitiousTarget));
                Assert.False(sheet.Goal.TargetFarBeyondPrediction); // under 15 % faster: "ambitious" only
                Assert.True(sheet.Envelope.EasyPaceMinSecondsPerKm >= sheet.Envelope.ReferencePaceSecondsPerKm);
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
            (plan, sheet) =>
            {
                AssertPacesWithinFitnessBounds(plan, sheet);
                AssertAmbitiousWording(plan, sheet);
            });

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

        // 7. The guest demo diary (runs.json + planned runs and Body Bar classes in the next weeks) with the demo trail goal 10 weeks out: weeks +1 to +5
        // are mostly full of fixed planned runs, so a model that adds sessions as usual goes over the weekly session limit (G-P4 live finding, repaired).
        var denseSheet = DemoTrainingPlanFixtureTests.DemoSheet(Today);
        var denseCase = new PlanEvalCase(
            DenseDiary,
            denseSheet,
            sheet =>
            {
                var e = sheet.Envelope;
                Assert.Equal(Surface.Trail, sheet.Goal.Surface);
                Assert.Equal(11, e.Weeks.Count);
                Assert.Contains(e.Weeks.Take(5), w => w.FixedRunningSessions >= e.MaxSessionsPerWeek - 1);
            },
            (draft, sheet) =>
            {
                var sessions = draft.Sessions.ToList();
                sessions.Add(sessions[^1] with { Date = D(sheet.Envelope.PlanEnd.AddDays(1)) });
                return (draft with { Sessions = sessions }, PlanRuleCode.DateRange);
            },
            (plan, sheet) =>
            {
                var e = sheet.Envelope;
                foreach (var w in e.Weeks)
                {
                    var running = plan.Kept.Count(s => s.Intensity != TrainingIntensity.Rest && s.Date >= w.Start && s.Date <= w.Start.AddDays(6));
                    Assert.True(w.FixedRunningSessions + running <= e.MaxSessionsPerWeek, $"{w.Start}: {w.FixedRunningSessions} + {running} > {e.MaxSessionsPerWeek}");
                }

                var blocked = TrainingPlanValidator.BlockedDates(sheet.Commitments);
                Assert.DoesNotContain(plan.Kept, s => blocked.Contains(s.Date));
            });

        // 8./9. The M9.1 user scenario (plan §3.1, §5): an easy-only diary (10 x 8 km at 6:00/km), half marathon target 1:40:00 (284 s/km) in 4 and
        //    12 weeks. The fallback prediction from recent training (7,245 s, 343 s/km) is the reference pace; the target is ambitious.
        var easyLog = PlanSheets.EasyOnlyLog(Today);
        var easyHalf4 = new GoalSpec(GoalType.Performance, 21_097.5, null, new DateOnly(2026, 12, 13), 6_000, Surface.Road);
        var easyHalf12 = easyHalf4 with { TargetDate = new DateOnly(2027, 2, 7) };
        var easy4Case = new PlanEvalCase(
            EasyOnlyHalf4Weeks,
            PlanSheets.Build(easyHalf4, Today, easyLog, []),
            sheet =>
            {
                AssertEasyOnlyInputs(sheet, easyHalf4, easyLog);
                Assert.Equal(4, sheet.Envelope.Weeks.Count);
            },
            (draft, sheet) =>
            {
                // Two hard sessions on consecutive days in the first week.
                var first = sheet.Envelope.PlanStart;
                var phase = WeekOf(sheet, first).Phase.ToWire();
                var sessions = draft.Sessions.Where(s => Date(s.Date) != first && Date(s.Date) != first.AddDays(1)).ToList();
                sessions.Add(new DraftSession(D(first), "tempo", "tempo", phase, 5_000, null, 300, 320, "Tempo run."));
                sessions.Add(new DraftSession(D(first.AddDays(1)), "intervals", "interval", phase, 5_000, null, 300, 320, "Intervals."));
                return (draft with { Sessions = [.. sessions.OrderBy(s => s.Date, StringComparer.Ordinal)] }, PlanRuleCode.HardSessions);
            },
            AssertEasyOnlyPlan);
        var easy12Case = new PlanEvalCase(
            EasyOnlyHalf12Weeks,
            PlanSheets.Build(easyHalf12, Today, easyLog, []),
            sheet =>
            {
                AssertEasyOnlyInputs(sheet, easyHalf12, easyLog);
                Assert.Equal(12, sheet.Envelope.Weeks.Count);
                Assert.False(sheet.Envelope.HorizonCapped);
            },
            (draft, sheet) =>
            {
                // Triple one week's cap across its sessions (weekly volume, beyond what the repair step scales down).
                var week = sheet.Envelope.Weeks[5];
                var inWeek = draft.Sessions.Count(s => InWeek(s, week));
                var each = Math.Min(59_000, Math.Floor(week.MaxDistanceMeters * 3 / inWeek));
                return (draft with { Sessions = [.. draft.Sessions.Select(s => InWeek(s, week) ? s with { DistanceMeters = each, DurationSeconds = null } : s)] }, PlanRuleCode.WeeklyVolume);
            },
            AssertEasyOnlyPlan);

        return [halfCase, tenKCase, trailCase, closeCase, collisionCase, cappedCase, denseCase, easy4Case, easy12Case];
    }

    /// <summary>The deterministic inputs of the M9.1 scenario (plan §5): fallback prediction, fitness reference pace, easy-pace bound, the flag, fact sheet v3.</summary>
    private static void AssertEasyOnlyInputs(PlanFactSheet sheet, GoalSpec goal, IReadOnlyList<RunFact> log)
    {
        var estimate = GoalEstimator.Estimate(goal, Today, log, []);
        var warning = Assert.Single(estimate.Warnings, w => w.Code == EstimateWarningCode.TargetMuchFasterThanPredicted);
        Assert.Equal(17.2, warning.Value);
        Assert.DoesNotContain(estimate.Warnings, w => w.Code == EstimateWarningCode.NoRecentEffort);
        Assert.Equal(3, sheet.FactSheetVersion);
        Assert.Equal(PlanEnvelopeRules.Version, sheet.Envelope.Version);
        Assert.Equal(EasyOnlyPredictedSeconds, sheet.Goal.PredictedDurationSeconds); // what TrainingPlanService stores as PlanPredictedDurationSeconds
        Assert.Equal(EasyOnlyReferencePace, sheet.Goal.FitnessPaceSecondsPerKm);
        Assert.Equal((EffortKind.RecentTraining, PredictionConfidence.Low), (sheet.Goal.PredictionBasis, sheet.Goal.PredictionConfidence));
        Assert.Equal(284, sheet.Goal.TargetPaceSecondsPerKm);
        Assert.True(sheet.Goal.AmbitiousTarget);
        Assert.True(sheet.Goal.TargetFarBeyondPrediction); // 17.2 % faster than the prediction (> 15 %)
        Assert.Equal(360, sheet.History.RecentEasyPaceSecondsPerKm);
        Assert.Equal(EasyOnlyReferencePace, sheet.Envelope.ReferencePaceSecondsPerKm);
        Assert.Equal(EasyOnlyReferencePace, sheet.Envelope.EasyPaceMinSecondsPerKm); // max(343, ceil(0.95 x 360) = 342)

        using var json = System.Text.Json.JsonDocument.Parse(PlanFactSheetJson.ToJson(sheet));
        var root = json.RootElement;
        Assert.Equal("recentTraining", root.GetProperty("goal").GetProperty("predictionBasis").GetString());
        Assert.Equal("low", root.GetProperty("goal").GetProperty("predictionConfidence").GetString());
        Assert.True(root.GetProperty("goal").GetProperty("ambitiousTarget").GetBoolean());
        Assert.True(root.GetProperty("goal").GetProperty("targetFarBeyondPrediction").GetBoolean());
        Assert.Equal(EasyOnlyReferencePace, root.GetProperty("envelope").GetProperty("easyPaceMinSecondsPerKm").GetInt32());
        Assert.Equal(360, root.GetProperty("history").GetProperty("recentEasyPaceSecondsPerKm").GetDouble());
    }

    /// <summary>Every accepted plan of the M9.1 scenario (after repair): no pace from the target.</summary>
    private static void AssertEasyOnlyPlan(PlanValidation plan, PlanFactSheet sheet)
    {
        Assert.DoesNotContain(plan.Kept, s => s.TargetPaceMinSecondsPerKm < EasyOnlyPaceFloor);
        Assert.DoesNotContain(plan.Kept, s => s.Intensity == TrainingIntensity.RacePace && s.TargetPaceMinSecondsPerKm < EasyOnlyRacePaceFloor);
        Assert.DoesNotContain(plan.Kept, s => s.Intensity is TrainingIntensity.Easy or TrainingIntensity.Recovery && s.TargetPaceMinSecondsPerKm < EasyOnlyReferencePace);
        AssertPacesWithinFitnessBounds(plan, sheet);
        AssertAmbitiousWording(plan, sheet);
    }

    /// <summary>
    /// No kept session is faster than fitness allows (ADR 0040 amendment 2026-11-21): faster bound at least 0.85 x the reference pace, race pace at
    /// least 0.97 x it, easy and recovery at least the easy-pace bound.
    /// </summary>
    public static void AssertPacesWithinFitnessBounds(PlanValidation plan, PlanFactSheet sheet)
    {
        var e = sheet.Envelope;
        foreach (var s in plan.Kept.Where(s => s.TargetPaceMinSecondsPerKm is not null))
        {
            var min = s.TargetPaceMinSecondsPerKm!.Value;
            if (e.ReferencePaceSecondsPerKm is { } reference)
            {
                Assert.True(min >= TrainingPlanValidator.MinPaceShareOfReference * reference, $"{s.Date}: {min} < 0.85 x {reference}");
                Assert.True(s.Intensity != TrainingIntensity.RacePace || min >= TrainingPlanValidator.MinRacePaceShareOfReference * reference, $"{s.Date}: race pace {min} < 0.97 x {reference}");
            }

            if (e.EasyPaceMinSecondsPerKm is { } easyMin && s.Intensity is TrainingIntensity.Easy or TrainingIntensity.Recovery)
            {
                Assert.True(min >= easyMin, $"{s.Date}: easy {min} < {easyMin}");
            }
        }
    }

    /// <summary>
    /// Prompt v4 (user decision on M9.1 review L3): an ambitious target is "ambitious"; only a target far beyond the prediction
    /// (<c>goal.targetFarBeyondPrediction</c>, more than 15 % faster) may be called not realistic by its date; "impossible" never. Checked on every
    /// accepted plan's notes, en and pl.
    /// </summary>
    private static void AssertAmbitiousWording(PlanValidation plan, PlanFactSheet sheet)
    {
        var notes = plan.Notes ?? string.Empty;
        foreach (var word in new[] { "impossib", "niemożliw", "niemozliw" })
        {
            Assert.DoesNotContain(word, notes, StringComparison.OrdinalIgnoreCase);
        }

        if (!sheet.Goal.TargetFarBeyondPrediction)
        {
            foreach (var word in new[] { "unrealistic", "not realistic", "not look realistic", "n't look realistic", "nierealn", "nie wygląda realnie", "nie wyglada realnie" })
            {
                Assert.DoesNotContain(word, notes, StringComparison.OrdinalIgnoreCase);
            }
        }
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
