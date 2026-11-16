using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Goals;

public sealed class GoalRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static GoalValues Valid(Func<GoalValues, GoalValues>? change = null)
    {
        var v = new GoalValues(
            GoalType.Distance, "Autumn 10K", Today.AddDays(30), 10_000, null, null, Surface.Road, null, null, null, null, GoalStatus.Active);
        return change is null ? v : change(v);
    }

    private static IReadOnlyList<string> Paths(GoalValues v, DateOnly? previous = null, LinkedRaceRun? run = null) =>
        [.. GoalRules.Validate(v, Today, previous, run).Select(e => e.Path)];

    [Fact]
    public void Valid_baseline_has_no_errors() => Assert.Empty(GoalRules.Validate(Valid(), Today, null, null));

    [Theory]
    [InlineData(999, false)]
    [InlineData(1_000, true)]
    [InlineData(250_000, true)]
    [InlineData(250_001, false)]
    [InlineData(double.NaN, false)]
    public void Distance_window_is_1000_to_250000_metres(double meters, bool ok) =>
        Assert.Equal(ok, !Paths(Valid(v => v with { DistanceMeters = meters })).Contains("distanceMeters"));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void Elevation_window_is_0_to_10000_metres(double meters, bool ok) =>
        Assert.Equal(ok, !Paths(Valid(v => v with { ElevationGainMeters = meters })).Contains("elevationGainMeters"));

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(730, true)]
    [InlineData(731, false)]
    public void Target_date_window_is_today_plus_7_to_plus_730_on_create(int daysAhead, bool ok) =>
        Assert.Equal(ok, !Paths(Valid(v => v with { TargetDate = Today.AddDays(daysAhead) })).Contains("targetDate"));

    [Fact]
    public void An_unchanged_date_inside_the_past_window_is_accepted_on_update()
    {
        var soon = Today.AddDays(3);

        Assert.DoesNotContain("targetDate", Paths(Valid(v => v with { TargetDate = soon }), previous: soon));
        Assert.Contains("targetDate", Paths(Valid(v => v with { TargetDate = soon }), previous: soon.AddDays(1)));
    }

    [Fact]
    public void A_changed_date_is_checked_on_update_even_when_the_old_one_was_bad()
    {
        var past = Today.AddDays(-5);

        Assert.Contains("targetDate", Paths(Valid(v => v with { TargetDate = Today.AddDays(2) }), previous: past));
        Assert.DoesNotContain("targetDate", Paths(Valid(v => v with { TargetDate = Today.AddDays(8) }), previous: past));
    }

    [Theory]
    [InlineData(1_490, false)] // 149 s/km
    [InlineData(1_500, true)] // 150 s/km
    [InlineData(9_000, true)] // 900 s/km
    [InlineData(9_010, false)] // 901 s/km
    [InlineData(0, false)]
    public void Implied_pace_must_be_150_to_900_seconds_per_km(int seconds, bool ok) =>
        Assert.Equal(ok, !Paths(Valid(v => v with { Type = GoalType.Performance, TargetDurationSeconds = seconds })).Contains("targetDurationSeconds"));

    [Fact]
    public void Pace_is_not_checked_when_the_distance_is_already_invalid()
    {
        var paths = Paths(Valid(v => v with { Type = GoalType.Performance, DistanceMeters = 500, TargetDurationSeconds = 10 }));

        Assert.Contains("distanceMeters", paths);
        Assert.DoesNotContain("targetDurationSeconds", paths);
    }

    [Fact]
    public void Race_goal_needs_an_event_name_and_may_have_a_target_time()
    {
        Assert.Contains("eventName", Paths(Valid(v => v with { Type = GoalType.Race })));
        Assert.Contains("eventName", Paths(Valid(v => v with { Type = GoalType.Race, EventName = "  " })));
        Assert.Empty(GoalRules.Validate(Valid(v => v with { Type = GoalType.Race, EventName = "Gliwice 10K", TargetDurationSeconds = 2_700 }), Today, null, null));
    }

    [Fact]
    public void Performance_goal_needs_a_target_duration()
    {
        Assert.Contains("targetDurationSeconds", Paths(Valid(v => v with { Type = GoalType.Performance })));
        Assert.Empty(GoalRules.Validate(Valid(v => v with { Type = GoalType.Performance, TargetDurationSeconds = 2_400 }), Today, null, null));
    }

    [Fact]
    public void Distance_goal_must_not_have_a_target_duration() =>
        Assert.Contains("targetDurationSeconds", Paths(Valid(v => v with { TargetDurationSeconds = 3_000 })));

    [Fact]
    public void Text_lengths_and_required_title()
    {
        Assert.Contains("title", Paths(Valid(v => v with { Title = "" })));
        Assert.Contains("title", Paths(Valid(v => v with { Title = "   " })));
        Assert.DoesNotContain("title", Paths(Valid(v => v with { Title = new string('x', 120) })));
        Assert.Contains("title", Paths(Valid(v => v with { Title = new string('x', 121) })));
        Assert.Contains("eventName", Paths(Valid(v => v with { EventName = new string('x', 151) })));
        Assert.DoesNotContain("notes", Paths(Valid(v => v with { Notes = new string('x', 1_000) })));
        Assert.Contains("notes", Paths(Valid(v => v with { Notes = new string('x', 1_001) })));
    }

    [Fact]
    public void Location_is_validated_like_a_run_location()
    {
        Assert.Contains("location.name", Paths(Valid(v => v with { Location = new RunLocation(new GeoPoint(50, 18), new string('x', 201)) })));
        Assert.Empty(GoalRules.Validate(Valid(v => v with { Location = new RunLocation(new GeoPoint(50, 18), new string('x', 200)) }), Today, null, null));
    }

    [Fact]
    public void Unknown_enum_values_are_rejected()
    {
        var paths = Paths(Valid(v => v with { Type = (GoalType)9, Status = (GoalStatus)9, Surface = (Surface)99 }));

        Assert.Contains("type", paths);
        Assert.Contains("status", paths);
        Assert.Contains("surface", paths);
    }

    [Fact]
    public void Race_run_link_rules()
    {
        var id = Guid.NewGuid();
        var date = Today.AddDays(30);
        var good = new LinkedRaceRun(id, date, RunStatus.Planned, IsRaceCategory: true);
        GoalValues Linked() => Valid(v => v with { Type = GoalType.Race, EventName = "Gliwice 10K", RaceRunId = id });

        Assert.Empty(GoalRules.Validate(Linked(), Today, null, good));
        Assert.Contains("raceRunId", Paths(Linked(), run: null));
        Assert.Contains("raceRunId", Paths(Linked(), run: good with { Id = Guid.NewGuid() }));
        Assert.Contains("raceRunId", Paths(Linked(), run: good with { IsRaceCategory = false }));
        Assert.Contains("raceRunId", Paths(Linked(), run: good with { Status = RunStatus.Completed }));
        Assert.Contains("raceRunId", Paths(Linked(), run: good with { Date = date.AddDays(1) }));
        Assert.DoesNotContain("raceRunId", Paths(Valid(), run: null));
    }

    [Fact]
    public void An_unchanged_race_run_link_only_needs_the_run_to_exist()
    {
        var id = Guid.NewGuid();
        var moved = new LinkedRaceRun(id, Today.AddDays(40), RunStatus.Completed, IsRaceCategory: true);
        var linked = Valid(v => v with { Type = GoalType.Race, EventName = "Gliwice 10K", RaceRunId = id });

        Assert.Empty(GoalRules.Validate(linked, Today, null, moved, previousRaceRunId: id));
        Assert.Contains(GoalRules.Validate(linked, Today, null, null, previousRaceRunId: id), e => e.Path == "raceRunId");
        Assert.Contains(GoalRules.Validate(linked, Today, null, moved, previousRaceRunId: Guid.NewGuid()), e => e.Path == "raceRunId");
    }

    [Fact]
    public void EnsureValid_throws_with_all_errors()
    {
        var ex = Assert.Throws<Common.DomainRuleException>(() =>
            GoalRules.EnsureValid(Valid(v => v with { Title = "", DistanceMeters = 1 }), Today, null, null));

        Assert.Contains(ex.Errors, e => e.Path == "title");
        Assert.Contains(ex.Errors, e => e.Path == "distanceMeters");
    }
}
