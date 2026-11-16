using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Goals.Planning;

public sealed class PlanEnvelopeBuilderTests
{
    // A Monday, so the first plan day (Tuesday) makes a 6/7 partial first week.
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static TrainingHistory History(double weekly = 30_000, double longest = 12_000, double sessions = 2.0, double? pace = 330) =>
        new(Today, [], weekly, longest, sessions, pace, 10, []);

    private static GoalSpec Goal(DateOnly target, double meters = 10_000) => new(GoalType.Distance, meters, null, target, null, Surface.Road);

    private static string Letters(IEnumerable<TrainingPhase> phases) =>
        string.Concat(phases.Select(p => p switch
        {
            TrainingPhase.Base => 'B',
            TrainingPhase.Build => 'U',
            TrainingPhase.Peak => 'P',
            _ => 'T',
        }));

    // Hand-derived from ADR 0040 §2: taper T, peak P = (W >= 8 ? 2 : 1), R = W - T - P, base = R >= 6 ? R / 2 : (R >= 3 ? 1 : 0); W <= T + 1 is all taper.
    // B = base, U = build, P = peak, T = taper; index = W - 1.
    private static readonly string[][] ExpectedPhases =
    [
        ["T", "TT", "UPT", "UUPT", "BUUPT", "BUUUPT", "BUUUUPT", "BUUUUPPT", "BBBUUUPPT", "BBBUUUUPPT", "BBBBUUUUPPT", "BBBBUUUUUPPT", "BBBBBUUUUUPPT", "BBBBBUUUUUUPPT", "BBBBBBUUUUUUPPT", "BBBBBBUUUUUUUPPT", "BBBBBBBUUUUUUUPPT", "BBBBBBBUUUUUUUUPPT", "BBBBBBBBUUUUUUUUPPT", "BBBBBBBBUUUUUUUUUPPT"],
        ["T", "TT", "TTT", "UPTT", "UUPTT", "BUUPTT", "BUUUPTT", "BUUUPPTT", "BUUUUPPTT", "BBBUUUPPTT", "BBBUUUUPPTT", "BBBBUUUUPPTT", "BBBBUUUUUPPTT", "BBBBBUUUUUPPTT", "BBBBBUUUUUUPPTT", "BBBBBBUUUUUUPPTT", "BBBBBBUUUUUUUPPTT", "BBBBBBBUUUUUUUPPTT", "BBBBBBBUUUUUUUUPPTT", "BBBBBBBBUUUUUUUUPPTT"],
        ["T", "TT", "TTT", "TTTT", "UPTTT", "UUPTTT", "BUUPTTT", "BUUPPTTT", "BUUUPPTTT", "BUUUUPPTTT", "BBBUUUPPTTT", "BBBUUUUPPTTT", "BBBBUUUUPPTTT", "BBBBUUUUUPPTTT", "BBBBBUUUUUPPTTT", "BBBBBUUUUUUPPTTT", "BBBBBBUUUUUUPPTTT", "BBBBBBUUUUUUUPPTTT", "BBBBBBBUUUUUUUPPTTT", "BBBBBBBUUUUUUUUPPTTT"],
    ];

    public static TheoryData<int, int> PhaseCases()
    {
        var data = new TheoryData<int, int>();
        for (var taper = 1; taper <= 3; taper++)
        {
            for (var weeks = 1; weeks <= 20; weeks++)
            {
                data.Add(taper, weeks);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PhaseCases))]
    public void Phases_for_one_to_twenty_weeks_and_each_taper_length(int taper, int weeks) =>
        Assert.Equal(ExpectedPhases[taper - 1][weeks - 1], Letters(PlanEnvelopeBuilder.Phases(weeks, taper)));

    [Fact]
    public void Peak_is_one_week_up_to_seven_weeks_and_two_from_eight()
    {
        Assert.Equal(1, PlanEnvelopeBuilder.Phases(7, 1).Count(p => p == TrainingPhase.Peak));
        Assert.Equal(2, PlanEnvelopeBuilder.Phases(8, 1).Count(p => p == TrainingPhase.Peak));
        Assert.Empty(PlanEnvelopeBuilder.Phases(0, 1));
    }

    [Theory]
    [InlineData(10_000, 1)]
    [InlineData(14_999, 1)]
    [InlineData(15_000, 2)]
    [InlineData(29_999, 2)]
    [InlineData(30_000, 3)]
    public void Taper_length_by_goal_distance(double meters, int weeks) => Assert.Equal(weeks, PlanEnvelopeRules.TaperWeeks(meters));

    [Theory]
    [InlineData(10_000, 18_000)]
    [InlineData(14_999, 18_000)]
    [InlineData(15_000, 24_000)]
    [InlineData(29_999, 24_000)]
    [InlineData(30_000, 35_000)]
    [InlineData(42_200, 35_000)]
    [InlineData(50_000, 30_000)] // 0.6 x 50 km
    [InlineData(100_000, 50_000)] // capped at 50 km
    public void Long_run_ceiling_by_goal_distance(double meters, double ceiling) => Assert.Equal(ceiling, PlanEnvelopeRules.LongRunCeilingMeters(meters));

    [Fact]
    public void A_10K_goal_ten_weeks_away_with_a_partial_first_week()
    {
        // Goal Sunday 2027-01-24; plan Tue 11-17 .. Sat 01-23; W = 10 (T = 1, peak 2, base 3, build 4).
        var e = PlanEnvelopeBuilder.Build(Goal(new DateOnly(2027, 1, 24)), Today, History(), [], 297);

        Assert.Equal(new DateOnly(2026, 11, 17), e.PlanStart);
        Assert.Equal(new DateOnly(2027, 1, 23), e.PlanEnd);
        Assert.False(e.HorizonCapped);
        Assert.Equal(10, e.TotalWeeks);
        Assert.Equal(1, e.TaperWeeks);
        Assert.Equal(3, e.MaxSessionsPerWeek); // ceil(2.0) + 1
        Assert.Equal(297, e.ReferencePaceSecondsPerKm);
        Assert.Equal(30_000, e.BaseVolumeMeters);
        Assert.Equal("BBBUUUUPPT", Letters(e.Weeks.Select(w => w.Phase)));
        Assert.Equal(new DateOnly(2026, 11, 16), e.Weeks[0].Start);
        Assert.Equal(new DateOnly(2027, 1, 18), e.Weeks[9].Start);

        // 30,000 x 1.1^k; week 1 scaled by 6/7 (33,000 x 6/7 = 28,285.7); taper = 30,000 x 1.1^9 x 0.6 = 42,443; all rounded down to 500 m (v2).
        Assert.Equal([28_000, 36_000, 39_500, 43_500, 48_000, 53_000, 58_000, 64_000, 70_500, 42_000], e.Weeks.Select(w => w.MaxDistanceMeters));
        // 12,000 x 1.1^k up to the 18 km ceiling of a 10K goal; taper 18,000 x 0.6; rounded down to 500 m.
        Assert.Equal([13_000, 14_500, 15_500, 17_500, 18_000, 18_000, 18_000, 18_000, 18_000, 10_500], e.Weeks.Select(w => w.MaxLongRunMeters));
        Assert.All(e.Weeks, w => Assert.Equal(0, w.MaxDistanceMeters % PlanEnvelopeRules.CapStepMeters));
        Assert.All(e.Weeks, w => Assert.Equal(w.MaxDistanceMeters, w.AvailableDistanceMeters));
        Assert.Equal(PlanEnvelopeRules.AllowedRunTypeCodes, e.AllowedRunTypes);
    }

    [Fact]
    public void A_far_goal_is_capped_at_sixteen_weeks_and_the_weekly_ceiling_applies()
    {
        // Half marathon on Sunday 2027-06-13: W = 30, T = 2, peak 2, base 13, build 13; only the first 16 weeks are emitted.
        var e = PlanEnvelopeBuilder.Build(Goal(new DateOnly(2027, 6, 13), 21_097.5), Today, History(weekly: 40_000), [], null);

        Assert.True(e.HorizonCapped);
        Assert.Equal(new DateOnly(2027, 3, 7), e.PlanEnd);
        Assert.Equal(30, e.TotalWeeks);
        Assert.Equal(16, e.Weeks.Count);
        Assert.Equal(new DateOnly(2027, 3, 1), e.Weeks[^1].Start);
        Assert.Equal(new string('B', 13) + "UUU", Letters(e.Weeks.Select(w => w.Phase)));
        Assert.Null(e.ReferencePaceSecondsPerKm);
        // 40,000 x 1.1^k (week 1 scaled by 6/7: 44,000 x 6/7 = 37,714.3), held at the 160 km ceiling from week 15; rounded down to 500 m.
        Assert.Equal(
            [37_500, 48_000, 53_000, 58_500, 64_000, 70_500, 77_500, 85_500, 94_000, 103_500, 114_000, 125_500, 138_000, 151_500, 160_000, 160_000],
            e.Weeks.Select(w => w.MaxDistanceMeters));
    }

    [Fact]
    public void A_goal_three_weeks_away_is_taper_only_without_growth()
    {
        // Half marathon on Sunday 2026-12-06: W = 3 <= T + 1 (T = 2). The pre-taper cap is the base volume itself; factors 0.75, 0.75, 0.55 from the end.
        var e = PlanEnvelopeBuilder.Build(Goal(new DateOnly(2026, 12, 6), 21_097.5), Today, History(), [], null);

        Assert.Equal("TTT", Letters(e.Weeks.Select(w => w.Phase)));
        Assert.Equal(new DateOnly(2026, 12, 5), e.PlanEnd);
        Assert.Equal([19_000, 22_500, 16_500], e.Weeks.Select(w => w.MaxDistanceMeters)); // 22,500 x 6/7 = 19,285.7, rounded down to 500 m
        Assert.Equal([9_000, 9_000, 6_500], e.Weeks.Select(w => w.MaxLongRunMeters)); // 6,600 rounded down
    }

    [Fact]
    public void The_base_volume_is_floored_at_ten_kilometres_and_the_long_run_at_eight()
    {
        var e = PlanEnvelopeBuilder.Build(Goal(new DateOnly(2026, 12, 6)), Today, History(weekly: 2_000, longest: 3_000), [], null);

        Assert.Equal(10_000, e.BaseVolumeMeters);
        // Week 1 of W = 3 (T = 1): U P T. Build: 10,000 x 1.1 x 6/7 = 9,428.6; long run 8,000 x 1.1 = 8,800; rounded down to 500 m.
        Assert.Equal(9_000, e.Weeks[0].MaxDistanceMeters);
        Assert.Equal(8_500, e.Weeks[0].MaxLongRunMeters);
    }

    [Theory]
    [InlineData(0.2, 3)]
    [InlineData(2.0, 3)]
    [InlineData(2.01, 4)]
    [InlineData(4.5, 6)]
    [InlineData(6.0, 7)]
    [InlineData(9.0, 7)]
    public void Max_sessions_per_week_is_the_ceiling_of_the_mean_plus_one_within_three_and_seven(double mean, int expected) =>
        Assert.Equal(expected, PlanEnvelopeBuilder.Build(Goal(new DateOnly(2027, 1, 24)), Today, History(sessions: mean), [], null).MaxSessionsPerWeek);

    [Fact]
    public void Commitments_reduce_the_available_distance_and_only_running_ones_count()
    {
        var easy = RunTypeIds.Easy;
        var commitments = new List<CommitmentFact>
        {
            new(new DateOnly(2026, 11, 19), easy, 10_000, 3_000, CommitmentKind.Planned, false), // week 1
            new(new DateOnly(2026, 11, 22), easy, 5_000, 1_500, CommitmentKind.Planned, true), // week 1 (Sunday), from this goal
            new(new DateOnly(2026, 11, 18), RunTypeIds.BodyBar, null, 3_600, CommitmentKind.Planned, false), // does not count
            new(new DateOnly(2026, 11, 20), RunTypeIds.Rest, null, null, CommitmentKind.Planned, false), // does not count
            new(new DateOnly(2026, 12, 6), RunTypeIds.RoadRace, 10_000, null, CommitmentKind.Race, false), // week 3
            new(new DateOnly(2026, 12, 13), null, null, null, CommitmentKind.OtherGoal, false), // another goal's date
            new(new DateOnly(2026, 11, 16), easy, 99_000, null, CommitmentKind.Planned, false), // today: before planStart
            new(new DateOnly(2027, 2, 1), RunTypeIds.RoadRace, 99_000, null, CommitmentKind.Race, false), // beyond planEnd
        };

        var e = PlanEnvelopeBuilder.Build(Goal(new DateOnly(2027, 1, 24)), Today, History(), commitments, null);

        Assert.Equal([15_000, 0, 10_000], e.Weeks.Take(3).Select(w => w.FixedDistanceMeters));
        Assert.Equal([13_000, 36_000, 29_500], e.Weeks.Take(3).Select(w => w.AvailableDistanceMeters)); // rounded caps minus fixed
        Assert.Equal([2, 0, 1], e.Weeks.Take(3).Select(w => w.FixedRunningSessions));
        Assert.All(e.Weeks.Skip(3), w => Assert.Equal(0, w.FixedDistanceMeters));
    }

    [Fact]
    public void Available_distance_never_goes_below_zero()
    {
        var commitments = new List<CommitmentFact> { new(new DateOnly(2026, 11, 21), RunTypeIds.Easy, 90_000, null, CommitmentKind.Planned, false) };

        var e = PlanEnvelopeBuilder.Build(Goal(new DateOnly(2027, 1, 24)), Today, History(), commitments, null);

        Assert.Equal(90_000, e.Weeks[0].FixedDistanceMeters);
        Assert.Equal(0, e.Weeks[0].AvailableDistanceMeters);
    }

    [Fact]
    public void A_goal_the_day_after_tomorrow_leaves_one_plannable_day_and_tomorrow_none()
    {
        var oneDay = PlanEnvelopeBuilder.Build(Goal(Today.AddDays(2)), Today, History(), [], null);
        var none = PlanEnvelopeBuilder.Build(Goal(Today.AddDays(1)), Today, History(), [], null);

        Assert.True(oneDay.HasPlannableDays);
        Assert.Equal(oneDay.PlanStart, oneDay.PlanEnd);
        Assert.Single(oneDay.Weeks);
        Assert.Equal(TrainingPhase.Taper, oneDay.Weeks[0].Phase);
        Assert.Equal(15_000, oneDay.Weeks[0].MaxDistanceMeters); // 30,000 x 0.6 x 6/7 = 15,428.6, rounded down to 500 m
        Assert.False(none.HasPlannableDays);
        Assert.Empty(none.Weeks);
    }

    [Fact]
    public void A_goal_on_a_monday_has_no_days_in_its_own_week_but_still_counts_it_for_the_phases()
    {
        // Goal Monday 2026-12-07: W = 4 (week of 11-16 .. week of 12-07). Plan end is Sunday 12-06; the goal week has no plannable day.
        var e = PlanEnvelopeBuilder.Build(Goal(new DateOnly(2026, 12, 7)), Today, History(), [], null);

        Assert.Equal(4, e.TotalWeeks);
        Assert.Equal(new DateOnly(2026, 12, 6), e.PlanEnd);
        Assert.Equal(3, e.Weeks.Count);
        Assert.Equal("UUP", Letters(e.Weeks.Select(w => w.Phase))); // the taper week itself has no plannable day
    }

    [Fact]
    public void Reference_pace_prefers_the_target_then_the_prediction_then_the_mean_pace()
    {
        var basis = new EffortFact(Guid.NewGuid(), Today, EffortKind.Race, 10_000, 3_000, null);
        var predicted = new TimePrediction(2_950, 295, PredictionConfidence.High, basis);
        var withTarget = new GoalSpec(GoalType.Performance, 10_000, null, Today.AddDays(60), 2_700, Surface.Road);
        var withoutTarget = Goal(Today.AddDays(60));

        Assert.Equal(270, PlanEnvelopeBuilder.ReferencePace(withTarget, predicted, History()));
        Assert.Equal(295, PlanEnvelopeBuilder.ReferencePace(withoutTarget, predicted, History()));
        Assert.Equal(297, PlanEnvelopeBuilder.ReferencePace(withoutTarget, null, History(pace: 330))); // 0.90 x 330
        Assert.Null(PlanEnvelopeBuilder.ReferencePace(withoutTarget, null, History(pace: null)));
    }

    [Fact]
    public void Allowed_run_types_are_the_training_types_and_rest()
    {
        Assert.Equal(
            ["easy", "recovery", "long", "tempo", "intervals", "fartlek", "hillRepeats", "progression", "racePace", "stridesDrills", "trailTraining", "treadmill", "rest"],
            PlanEnvelopeRules.AllowedRunTypeCodes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    [InlineData(499.9, 0)]
    [InlineData(500, 500)]
    [InlineData(28_285.7, 28_000)]
    [InlineData(28_499.999_999_9, 28_500)]
    [InlineData(160_000, 160_000)]
    public void Caps_are_rounded_down_to_the_500_metre_step(double meters, double expected) =>
        Assert.Equal(expected, PlanEnvelopeRules.FloorToStep(meters));

    [Fact]
    public void Available_distance_is_on_the_step_even_when_the_fixed_distance_is_not()
    {
        var commitments = new List<CommitmentFact> { new(new DateOnly(2026, 11, 19), RunTypeIds.Easy, 7_340, null, CommitmentKind.Planned, false) };

        var e = PlanEnvelopeBuilder.Build(Goal(new DateOnly(2027, 1, 24)), Today, History(), commitments, null);

        // 28,000 - 7,340 = 20,660 -> 20,500.
        Assert.Equal((28_000, 7_340, 20_500), (e.Weeks[0].MaxDistanceMeters, e.Weeks[0].FixedDistanceMeters, e.Weeks[0].AvailableDistanceMeters));
    }
}

internal static class RunTypeIds
{
    public const short RoadRace = 1;
    public const short Easy = 20;
    public const short Rest = 41;
    public const short BodyBar = 42;
}
