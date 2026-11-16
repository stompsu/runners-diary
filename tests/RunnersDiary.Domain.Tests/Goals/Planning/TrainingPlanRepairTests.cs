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
            Session(week1.Start.AddDays(4), 5_000, paceMin: 200, paceMax: 250),
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
}
