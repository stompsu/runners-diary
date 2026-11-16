using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Goals.Planning;

public sealed class TrainingSuggestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Goal = Guid.NewGuid();

    private static PlannedSession Easy(Func<PlannedSession, PlannedSession>? change = null)
    {
        var s = new PlannedSession(
            new DateOnly(2026, 11, 24), 20, 8_000, 2_700, TrainingIntensity.Easy, 330, 360, "Easy run, keep it relaxed.", TrainingPhase.Base);
        return change is null ? s : change(s);
    }

    private static PlannedSession Rest() =>
        new(new DateOnly(2026, 11, 25), 41, null, null, TrainingIntensity.Rest, null, null, "Rest day.", TrainingPhase.Base);

    private static TrainingSuggestion Create(PlannedSession s) => TrainingSuggestion.Create(User, Goal, Guid.NewGuid(), s, Now);

    [Fact]
    public void Create_copies_the_session_and_starts_pending()
    {
        var generation = Guid.NewGuid();

        var s = TrainingSuggestion.Create(User, Goal, generation, Easy(), Now);

        Assert.Equal((User, Goal, generation), (s.UserId, s.GoalId, s.GenerationId));
        Assert.Equal(SuggestionStatus.Pending, s.Status);
        Assert.Equal(new DateOnly(2026, 11, 24), s.Date);
        Assert.Equal((330, 360), (s.TargetPaceMinSecondsPerKm, s.TargetPaceMaxSecondsPerKm));
        Assert.Equal(TrainingPhase.Base, s.Phase);
        Assert.Equal(DateTimeKind.Utc, s.CreatedAt.Kind);
        Assert.NotEqual(Guid.Empty, s.Id);
    }

    [Fact]
    public void Dismiss_is_idempotent()
    {
        var s = Create(Easy());

        s.Dismiss();
        s.Dismiss();

        Assert.Equal(SuggestionStatus.Dismissed, s.Status);
    }

    public static TheoryData<string, PlannedSession> Invalid() => new()
    {
        { "run type", Easy(s => s with { RunTypeId = 42 }) }, // Body Bar
        { "race type", Easy(s => s with { RunTypeId = 1 }) },
        { "unknown type", Easy(s => s with { RunTypeId = 999 }) },
        { "no distance", Easy(s => s with { DistanceMeters = null }) },
        { "short distance", Easy(s => s with { DistanceMeters = 999 }) },
        { "long distance", Easy(s => s with { DistanceMeters = 60_001 }) },
        { "short duration", Easy(s => s with { DurationSeconds = 599 }) },
        { "long duration", Easy(s => s with { DurationSeconds = 21_601 }) },
        { "implied pace too fast (30 km in 10 min)", Easy(s => s with { DistanceMeters = 30_000, DurationSeconds = 600 }) },
        { "implied pace just under 2:30/km", Easy(s => s with { DistanceMeters = 10_000, DurationSeconds = 1_499 }) },
        { "implied pace just over 15:00/km", Easy(s => s with { DistanceMeters = 1_000, DurationSeconds = 901 }) },
        { "one pace bound", Easy(s => s with { TargetPaceMaxSecondsPerKm = null }) },
        { "pace order", Easy(s => s with { TargetPaceMinSecondsPerKm = 361 }) },
        { "pace spread", Easy(s => s with { TargetPaceMaxSecondsPerKm = 421 }) },
        { "pace low", Easy(s => s with { TargetPaceMinSecondsPerKm = 149 }) },
        { "empty description", Easy(s => s with { Description = " " }) },
        { "long description", Easy(s => s with { Description = new string('x', 301) }) },
        { "rest intensity on a run", Easy(s => s with { Intensity = TrainingIntensity.Rest }) },
        { "run intensity on rest", Rest() with { Intensity = TrainingIntensity.Easy } },
        { "rest with distance", Rest() with { DistanceMeters = 5_000 } },
        { "rest with pace", Rest() with { TargetPaceMinSecondsPerKm = 300, TargetPaceMaxSecondsPerKm = 330 } },
        { "unknown phase", Easy(s => s with { Phase = (TrainingPhase)9 }) },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_sessions_are_rejected(string reason, PlannedSession session)
    {
        _ = reason;

        Assert.Throws<DomainRuleException>(() => Create(session));
    }

    [Fact]
    public void Boundary_values_are_accepted()
    {
        Create(Easy(s => s with { DistanceMeters = 1_000, DurationSeconds = 600, TargetPaceMinSecondsPerKm = 150, TargetPaceMaxSecondsPerKm = 240, Description = new string('x', 300) }));
        Create(Easy(s => s with { DistanceMeters = 60_000, DurationSeconds = 21_600, TargetPaceMinSecondsPerKm = 810, TargetPaceMaxSecondsPerKm = 900 }));
        Create(Easy(s => s with { DurationSeconds = null, TargetPaceMinSecondsPerKm = null, TargetPaceMaxSecondsPerKm = null }));
        // The implied pace bounds (GoalRules, 2:30 to 15:00 min/km) are inclusive.
        Create(Easy(s => s with { DistanceMeters = 10_000, DurationSeconds = 1_500 }));
        Create(Easy(s => s with { DistanceMeters = 1_000, DurationSeconds = 900 }));
    }

    [Fact]
    public void An_implausible_implied_pace_is_reported_at_the_duration()
    {
        var ex = Assert.Throws<DomainRuleException>(() => Create(Easy(s => s with { DistanceMeters = 30_000, DurationSeconds = 600 })));

        Assert.Equal("durationSeconds", Assert.Single(ex.Errors).Path);
    }

    [Fact]
    public void A_missing_owner_or_goal_is_rejected()
    {
        Assert.Throws<DomainRuleException>(() => TrainingSuggestion.Create(Guid.Empty, Goal, Guid.NewGuid(), Easy(), Now));
        Assert.Throws<DomainRuleException>(() => TrainingSuggestion.Create(User, Guid.Empty, Guid.NewGuid(), Easy(), Now));
    }

    public static TheoryData<short> SuggestibleTypes() =>
        [.. RunTypeCatalog.All.Where(t => t.Category == RunTypeCategory.Training || t.IsRestDay).Select(t => t.Id)];

    [Theory]
    [MemberData(nameof(SuggestibleTypes))]
    public void The_default_accept_mapping_passes_the_run_rules_for_every_suggestible_type(short runTypeId)
    {
        var session = runTypeId == 41 ? Rest() : Easy(s => s with { RunTypeId = runTypeId });
        var suggestion = Create(session);

        var values = suggestion.ToRunValues("Europe/Warsaw");

        Assert.Empty(RunRules.Validate(values));
        Assert.Equal(RunStatus.Planned, values.Status);
        Assert.Equal(session.Date, values.Date);
        Assert.Equal(runTypeId, values.RunTypeId);
        Assert.Equal(session.Description, values.Notes);
        Assert.Null(values.StartTime);
        Assert.Equal("Europe/Warsaw", values.TimeZoneId);
        Assert.Equal(session.DistanceMeters, values.DistanceMeters);
        Assert.Equal(session.DurationSeconds, values.DurationSeconds);
        Assert.Null(values.Title);
        Assert.NotNull(Run.Create(User, values, Now));
    }

    [Fact]
    public void Only_training_types_and_rest_are_suggestible()
    {
        Assert.Equal(
            Enumerable.Range(20, 12).Select(i => (short)i).Append((short)41).Order(),
            RunTypeCatalog.All.Where(t => TrainingSuggestion.IsSuggestibleRunType(t.Id)).Select(t => t.Id).Order());
    }

    [Fact]
    public void Intensity_codes_round_trip_and_hard_means_tempo_threshold_interval_or_race_pace()
    {
        foreach (var intensity in Enum.GetValues<TrainingIntensity>())
        {
            Assert.True(TrainingIntensities.TryFromWire(intensity.ToWire(), out var back));
            Assert.Equal(intensity, back);
        }

        Assert.Equal(
            [TrainingIntensity.Tempo, TrainingIntensity.Threshold, TrainingIntensity.Interval, TrainingIntensity.RacePace],
            Enum.GetValues<TrainingIntensity>().Where(i => i.IsHard()));
        Assert.False(TrainingIntensities.TryFromWire("Easy", out _));
        foreach (var phase in Enum.GetValues<TrainingPhase>())
        {
            Assert.True(TrainingPhases.TryFromWire(phase.ToWire(), out var back));
            Assert.Equal(phase, back);
        }
    }
}
