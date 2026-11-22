using System.Globalization;
using RunnersDiary.Domain.Goals.Planning;

namespace RunnersDiary.Domain.Tests.Goals.Planning;

/// <summary>ADR 0040 amendment "repair": what the repair step changes, what it leaves to the validator, and that it is idempotent.</summary>
public sealed class TrainingPlanRepairTests
{
    private static PlannedSession Session(DateOnly date, double distance, int? duration = null, TrainingIntensity intensity = TrainingIntensity.Easy, TrainingPhase phase = TrainingPhase.Base, int? paceMin = null, int? paceMax = null) =>
        new(date, 20, distance, duration, intensity, paceMin, paceMax, "Run.", phase);

    /// <summary>A messy but repairable plan over the shared test sheet: wrong phase, a long run and a week above their caps, a too-fast and a hard-in-taper session.</summary>
    private static (PlanFactSheet Sheet, List<PlannedSession> Sessions) Messy()
    {
        var sheet = TrainingPlanValidatorTests.Sheet(sessionsPerWeek: 6.0);
        var week1 = sheet.Envelope.Weeks[1];
        var week3 = sheet.Envelope.Weeks[3];
        var eve = Target.AddDays(-1);
        var tolerant = week3.MaxDistanceMeters * TrainingPlanValidator.Tolerance;
        var each = Math.Floor(tolerant * 1.15 / 5 / 100) * 100;
        var sessions = new List<PlannedSession>
        {
            Session(week1.Start, 6_000, phase: TrainingPhase.Peak),
            Session(week1.Start.AddDays(2), Math.Ceiling(week1.MaxLongRunMeters * 1.1), 4_000),
            Session(week1.Start.AddDays(4), 5_000, intensity: TrainingIntensity.Tempo, paceMin: 200, paceMax: 250), // too-fast hard session: dropped (easy ones are clamped)
            Session(eve, 5_000, intensity: TrainingIntensity.Tempo, phase: TrainingPhase.Taper),
        };
        sessions.AddRange(VolumeDays.Select(d => Session(week3.Start.AddDays(d), each, (int)(each * 0.36), phase: TrainingPhase.Base)));
        return (sheet, sessions);
    }

    private static readonly DateOnly Target = new(2027, 1, 24);

    private static readonly int[] VolumeDays = [0, 1, 2, 4, 6];

    [Fact]
    public void Repairs_apply_in_order_and_report_codes_locations_and_numbers()
    {
        var (sheet, sessions) = Messy();

        var result = TrainingPlanRepair.Apply(sessions, sheet);

        Assert.Equal(2, result.Dropped);
        Assert.Equivalent(
            new[] { PlanRuleCode.Phase, PlanRuleCode.Pace, PlanRuleCode.TaperIntensity, PlanRuleCode.LongRun, PlanRuleCode.WeeklyVolume },
            result.Repairs.Select(r => r.Rule).Distinct().ToArray());
        Assert.All(result.Sessions, s => Assert.Equal(sheet.Envelope.Weeks.Single(w => w.Start == RunnersDiary.Domain.Calendar.IsoWeek.MondayOf(s.Date)).Phase, s.Phase));
        Assert.Equal(sessions.Count - 2, result.Sessions.Count);
        var volume = Assert.Single(result.Repairs, r => r.Rule == PlanRuleCode.WeeklyVolume);
        Assert.Equal(PlanRepairAction.Scale, volume.Action);
        Assert.True(volume.To < volume.From);
    }

    [Fact]
    public void Repairing_twice_changes_nothing()
    {
        var (sheet, sessions) = Messy();
        var first = TrainingPlanRepair.Apply(sessions, sheet);

        var second = TrainingPlanRepair.Apply(first.Sessions, sheet);

        Assert.Empty(second.Repairs);
        Assert.Equal(0, second.Dropped);
        Assert.Equal(first.Sessions, second.Sessions);
    }

    [Fact]
    public void A_repaired_plan_passes_the_whole_validation_and_a_second_validation_of_it_needs_no_repair()
    {
        var (sheet, sessions) = Messy();
        var repaired = TrainingPlanRepair.Apply(sessions, sheet).Sessions;
        string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var draft = new TrainingPlanDraft("A gradual plan.", [.. repaired.Select(s => new DraftSession(
            D(s.Date), "easy", s.Intensity == TrainingIntensity.Easy ? "easy" : "tempo", s.Phase.ToWire(), s.DistanceMeters, s.DurationSeconds,
            s.TargetPaceMinSecondsPerKm, s.TargetPaceMaxSecondsPerKm, s.Description))]);

        var result = TrainingPlanValidator.Validate(draft, sheet, PlanValidationPolicy.Default);

        Assert.DoesNotContain(PlanRuleCode.WeeklyVolume, result.Codes);
        Assert.DoesNotContain(PlanRuleCode.LongRun, result.Codes);
        Assert.Empty(result.Repairs);
        Assert.Equal(0, result.DroppedCount);
    }

    [Fact]
    public void A_plan_inside_every_bound_is_left_alone()
    {
        var sheet = TrainingPlanValidatorTests.Sheet();
        var sessions = TrainingPlanValidator.Validate(new TrainingPlanDraft("x", TrainingPlanValidatorTests.Good(sheet)), sheet, PlanValidationPolicy.Default).Kept;

        var result = TrainingPlanRepair.Apply(sessions, sheet);

        Assert.Empty(result.Repairs);
        Assert.Equal(sessions, result.Sessions);
    }

    /// <summary>A week at the session limit: <paramref name="fixedSessions"/> planned runs plus the given suggestions (day offset, intensity, distance).</summary>
    private static (PlanFactSheet Sheet, PlanWeek Week, List<PlannedSession> Sessions) Crowded(int fixedSessions, params (int Day, TrainingIntensity Intensity, double Meters)[] suggestions)
    {
        var commitments = Enumerable.Range(0, fixedSessions)
            .Select(i => new CommitmentFact(new DateOnly(2026, 11, 30).AddDays(i * 2), 20, 5_000, null, CommitmentKind.Planned, false)).ToList();
        var sheet = TrainingPlanValidatorTests.Sheet(commitments);
        var week = sheet.Envelope.Weeks.Single(w => w.Start == new DateOnly(2026, 11, 30));
        var sessions = suggestions.Select(x => Session(week.Start.AddDays(x.Day), x.Meters, intensity: x.Intensity, phase: week.Phase)).ToList();
        return (sheet, week, sessions);
    }

    [Fact]
    public void An_over_full_week_loses_recovery_first_then_easy_then_other_non_hard_then_hard_and_later_dates_first()
    {
        // 3 sessions a week history: at most 4. One fixed run leaves room for 3 suggestions, so 5 suggestions are 2 over.
        var (sheet, week, sessions) = Crowded(
            1,
            (1, TrainingIntensity.Easy, 6_000), (3, TrainingIntensity.Recovery, 4_000), (5, TrainingIntensity.Easy, 5_000), (6, TrainingIntensity.Interval, 6_000), (0, TrainingIntensity.Steady, 7_000));
        Assert.Equal(4, sheet.Envelope.MaxSessionsPerWeek);
        Assert.Equal(1, week.FixedRunningSessions);

        var result = TrainingPlanRepair.Apply(sessions, sheet);

        // Dropped: the recovery run (Thu) and the later easy run (Sat). The longest (Steady, Mon 7,000 m) is protected anyway.
        Assert.Equal(2, result.Dropped);
        Assert.Equal([week.Start, week.Start.AddDays(1), week.Start.AddDays(6)], result.Sessions.Select(s => s.Date));
        Assert.Equal(new PlanRepair(PlanRuleCode.Frequency, "2026-11-30", PlanRepairAction.Drop, 5, 3), Assert.Single(result.Repairs));
    }

    [Fact]
    public void Hard_sessions_go_after_the_non_hard_ones_and_the_longest_run_is_protected()
    {
        // Limit 4 with 2 fixed runs: room for 2. Four suggestions: easy 9,000 (the longest), steady, tempo, interval.
        var (sheet, week, sessions) = Crowded(
            2, (0, TrainingIntensity.Easy, 9_000), (2, TrainingIntensity.Steady, 5_000), (4, TrainingIntensity.Tempo, 4_000), (6, TrainingIntensity.Interval, 3_000));

        var result = TrainingPlanRepair.Apply(sessions, sheet);

        // Steady goes first, then the later hard session (interval, Sun); the longest easy run and the tempo stay.
        Assert.Equal([week.Start, week.Start.AddDays(4)], result.Sessions.Select(s => s.Date));
        Assert.Equal(2, result.Dropped);
    }

    [Fact]
    public void The_longest_run_is_kept_while_any_other_candidate_exists_even_when_it_is_recovery()
    {
        var (sheet, week, sessions) = Crowded(
            2, (0, TrainingIntensity.Recovery, 12_000), (2, TrainingIntensity.Interval, 5_000), (4, TrainingIntensity.Tempo, 4_000));

        var result = TrainingPlanRepair.Apply(sessions, sheet);

        Assert.Equal(1, result.Dropped);
        Assert.Contains(result.Sessions, s => s.Date == week.Start);
        Assert.DoesNotContain(result.Sessions, s => s.Date == week.Start.AddDays(4));
    }

    [Fact]
    public void A_week_full_of_fixed_runs_loses_every_suggestion_without_becoming_an_empty_week_violation()
    {
        var (sheet, week, sessions) = Crowded(4, (1, TrainingIntensity.Easy, 5_000), (3, TrainingIntensity.Easy, 6_000));
        Assert.Equal(4, week.FixedRunningSessions);
        var all = new List<PlannedSession>(sessions);

        var result = TrainingPlanRepair.Apply(all, sheet);

        Assert.Empty(result.Sessions);
        Assert.Equal(2, result.Dropped);
        Assert.Equal(new PlanRepair(PlanRuleCode.Frequency, "2026-11-30", PlanRepairAction.Drop, 2, 0), Assert.Single(result.Repairs));
    }

    [Fact]
    public void Rest_sessions_are_not_running_sessions_and_stay()
    {
        var (sheet, week, sessions) = Crowded(1, (0, TrainingIntensity.Easy, 5_000), (2, TrainingIntensity.Easy, 5_500), (4, TrainingIntensity.Easy, 6_000));
        var rest = new PlannedSession(week.Start.AddDays(5), TrainingSuggestion.RestRunTypeId, null, null, TrainingIntensity.Rest, null, null, "Rest.", week.Phase);

        var result = TrainingPlanRepair.Apply([.. sessions, rest], sheet);

        Assert.Empty(result.Repairs); // 1 fixed + 3 running = 4 = the limit
        Assert.Contains(result.Sessions, s => s.Intensity == TrainingIntensity.Rest);

        var over = TrainingPlanRepair.Apply([.. sessions, sessions[0] with { Date = week.Start.AddDays(6) }, rest], sheet);
        Assert.Equal(1, over.Dropped);
        Assert.Contains(over.Sessions, s => s.Intensity == TrainingIntensity.Rest);
    }

    [Fact]
    public void Frequency_repair_is_idempotent_and_runs_before_volume_scaling()
    {
        var (sheet, week, _) = Crowded(1);
        // Five easy runs of equal length: 2 over the limit. After dropping 2 the 3 remaining fit the cap, so no volume scaling is needed.
        var each = Math.Floor(week.AvailableDistanceMeters * 0.3 / 100) * 100;
        var sessions = Enumerable.Range(0, 5).Select(i => Session(week.Start.AddDays(i * 7 / 5 + (i % 2)), each, phase: week.Phase)).Select((s, i) => s with { Date = week.Start.AddDays(i + (i > 2 ? 1 : 0)) }).ToList();

        var first = TrainingPlanRepair.Apply(sessions, sheet);

        Assert.Equal(2, first.Dropped);
        Assert.DoesNotContain(first.Repairs, r => r.Rule == PlanRuleCode.WeeklyVolume);
        var second = TrainingPlanRepair.Apply(first.Sessions, sheet);
        Assert.Empty(second.Repairs);
        Assert.Equal(first.Sessions, second.Sessions);
    }

    private static (PlanFactSheet Sheet, DateOnly Day) EasySheet(int reference)
    {
        var sheet = TrainingPlanValidatorTests.Sheet(referencePace: reference);
        return (sheet, sheet.Envelope.Weeks[1].Start.AddDays(1));
    }

    [Fact]
    public void Easy_pace_clamp_raises_the_bound_keeps_the_range_inside_the_spread_and_lengthens_the_duration()
    {
        var (sheet, day) = EasySheet(340);
        PlannedSession[] sessions =
        [
            Session(day, 8_000, 2_400, paceMin: 300, paceMax: 380),                       // implied 300 s/km, faster than the bound
            Session(day.AddDays(2), 6_000, 2_400, paceMin: 300, paceMax: 700),            // slower bound beyond min + 90
            Session(day.AddDays(4), 6_000, 2_400, TrainingIntensity.Recovery, paceMin: 320, paceMax: 340), // slower bound below the new minimum
        ];

        var result = TrainingPlanRepair.Apply(sessions, sheet);

        Assert.Equal(0, result.Dropped);
        Assert.Equal((340, 380, 2_720), (result.Sessions[0].TargetPaceMinSecondsPerKm, result.Sessions[0].TargetPaceMaxSecondsPerKm, result.Sessions[0].DurationSeconds));
        Assert.Equal((340, 430, 2_400), (result.Sessions[1].TargetPaceMinSecondsPerKm, result.Sessions[1].TargetPaceMaxSecondsPerKm, result.Sessions[1].DurationSeconds)); // 400 s/km implied: kept
        Assert.Equal((340, 340), (result.Sessions[2].TargetPaceMinSecondsPerKm, result.Sessions[2].TargetPaceMaxSecondsPerKm));
        Assert.Equal(3, result.Repairs.Count(r => r.Rule == PlanRuleCode.EasyPace && r.Action == PlanRepairAction.Clamp));
        var first = result.Repairs.First(r => r.Rule == PlanRuleCode.EasyPace);
        Assert.Equal((300, 340), (first.From, first.To));
    }

    [Fact]
    public void Easy_pace_clamp_leaves_unranged_and_hard_sessions_alone_and_drops_a_hard_one_at_the_pace_rule_first()
    {
        var (sheet, day) = EasySheet(340);
        PlannedSession[] sessions =
        [
            Session(day, 6_000, 1_800),                                                    // easy without a pace range: not checked
            Session(day.AddDays(2), 6_000, 1_800, TrainingIntensity.Steady, paceMin: 300, paceMax: 330),
            Session(day.AddDays(4), 6_000, null, TrainingIntensity.Tempo, paceMin: 200, paceMax: 220),           // under 0.85 x ref: the pace drop comes first
        ];

        var result = TrainingPlanRepair.Apply(sessions, sheet);

        Assert.Equal(1, result.Dropped);
        Assert.Equal(1_800, result.Sessions[0].DurationSeconds);
        Assert.Equal(300, result.Sessions[1].TargetPaceMinSecondsPerKm);
        Assert.Contains(result.Repairs, r => r.Rule == PlanRuleCode.Pace && r.Action == PlanRepairAction.Drop);
        Assert.DoesNotContain(result.Repairs, r => r.Rule == PlanRuleCode.EasyPace);
    }

    [Fact]
    public void Easy_and_recovery_runs_far_below_the_pace_drop_threshold_are_clamped_while_a_tempo_there_is_dropped()
    {
        var (sheet, day) = EasySheet(343); // 0.85 x 343 = 291.55
        PlannedSession[] sessions =
        [
            Session(day, 8_000, 2_272, paceMin: 284, paceMax: 300),                                          // easy at the target pace
            Session(day.AddDays(1), 5_000, 1_420, TrainingIntensity.Recovery, paceMin: 284, paceMax: 300),
            Session(day.AddDays(3), 6_000, 1_704, TrainingIntensity.Tempo, paceMin: 284, paceMax: 300),
        ];

        var result = TrainingPlanRepair.Apply(sessions, sheet);

        Assert.Equal(1, result.Dropped);
        Assert.Equal(2, result.Sessions.Count);
        Assert.All(result.Sessions, s => Assert.Equal(343, s.TargetPaceMinSecondsPerKm));
        Assert.Equal(2, result.Repairs.Count(r => r.Rule == PlanRuleCode.EasyPace && r.Action == PlanRepairAction.Clamp));
        var pace = Assert.Single(result.Repairs, r => r.Rule == PlanRuleCode.Pace);
        Assert.Equal(PlanRepairAction.Drop, pace.Action);
        Assert.Equal(TrainingPlanValidator.Format(day.AddDays(3)), pace.Location);
    }

    [Fact]
    public void Easy_pace_drops_a_session_the_clamp_would_push_out_of_its_range()
    {
        var (sheet, day) = EasySheet(880);

        // 26 km at 880 s/km = 22,880 s, above the 6 h duration limit.
        var result = TrainingPlanRepair.Apply([Session(day, 26_000, 20_000, paceMin: 800, paceMax: 880)], sheet);

        Assert.Empty(result.Sessions);
        Assert.Equal(1, result.Dropped);
        var drop = Assert.Single(result.Repairs, r => r.Rule == PlanRuleCode.EasyPace);
        Assert.Equal(PlanRepairAction.Drop, drop.Action);
    }

    [Fact]
    public void Easy_pace_clamp_is_idempotent()
    {
        var (sheet, day) = EasySheet(340);
        var first = TrainingPlanRepair.Apply([Session(day, 8_000, 2_400, paceMin: 300, paceMax: 380)], sheet);

        var second = TrainingPlanRepair.Apply(first.Sessions, sheet);

        Assert.Equal(first.Sessions, second.Sessions);
        Assert.DoesNotContain(second.Repairs, r => r.Rule == PlanRuleCode.EasyPace);
    }
}
