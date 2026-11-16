using System.Globalization;
using RunnersDiary.Domain.Goals;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Runs;

namespace RunnersDiary.Domain.Tests.Goals.Planning;

/// <summary>ADR 0040 §5: one adversarial draft per rule code, the collision drop, and a valid baseline (plan §9).</summary>
public sealed class TrainingPlanValidatorTests
{
    // A Monday; the goal is a 10K on Sunday 2027-01-24 with a 45:00 target (reference pace 270 s/km). W = 10 weeks: BBBUUUUPPT.
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly DateOnly Target = new(2027, 1, 24);
    private static readonly PlanValidationPolicy Policy = PlanValidationPolicy.Default;
    private static readonly int[] VolumeDays = [0, 2, 4, 5, 6];
    private static readonly int[] FrequencyDays = [0, 1, 2, 6];

    internal static PlanFactSheet Sheet(IReadOnlyList<CommitmentFact>? commitments = null, double sessionsPerWeek = 3.0, DateOnly? target = null, int? referencePace = 270)
    {
        var goal = new GoalSpec(GoalType.Performance, 10_000, null, target ?? Target, 2_700, Surface.Road);
        var history = new TrainingHistory(Today, [], 30_000, 12_000, sessionsPerWeek, 330, 12, [DayOfWeek.Tuesday, DayOfWeek.Sunday]);
        var envelope = PlanEnvelopeBuilder.Build(goal, Today, history, commitments ?? [], referencePace);
        return new PlanFactSheet(
            PlanFactSheet.CurrentVersion,
            Today,
            "en",
            new PlanGoalFacts(goal.Type, goal.DistanceMeters, null, goal.Surface, goal.TargetDate, goal.TargetDurationSeconds, goal.TargetPaceSecondsPerKm, null, 9),
            new PlanHistoryFacts([], history.UsualWeekdays, sessionsPerWeek, new PlanAcwrFact(null, null), []),
            commitments ?? [],
            [],
            envelope);
    }

    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A plan inside every bound: per week an easy run on Tuesday (30 % of the available distance, at most 60 % of the long-run cap) and a long run on Sunday or the last plan day (50 %, capped).</summary>
    internal static List<DraftSession> Good(PlanFactSheet sheet)
    {
        var e = sheet.Envelope;
        var sessions = new List<DraftSession>();
        foreach (var w in e.Weeks)
        {
            var phase = w.Phase.ToWire();
            var tuesday = w.Start.AddDays(1);
            if (tuesday >= e.PlanStart && tuesday <= e.PlanEnd)
            {
                sessions.Add(new DraftSession(D(tuesday), "easy", "easy", phase, Math.Floor(Math.Min(w.AvailableDistanceMeters * 0.3, w.MaxLongRunMeters * 0.6) / 100) * 100, null, 320, 360, "Easy run at a relaxed effort."));
            }

            var longDay = w.Start.AddDays(6) <= e.PlanEnd ? w.Start.AddDays(6) : e.PlanEnd;
            var meters = Math.Floor(Math.Min(w.AvailableDistanceMeters * 0.5, w.MaxLongRunMeters) / 100) * 100;
            sessions.Add(new DraftSession(D(longDay), "long", "easy", phase, meters, null, 330, 380, "Long run at an easy effort."));
        }

        return sessions;
    }

    private static PlanValidation Validate(PlanFactSheet sheet, IEnumerable<DraftSession> sessions, string notes = "A gradual plan.") =>
        TrainingPlanValidator.Validate(new TrainingPlanDraft(notes, [.. sessions]), sheet, Policy);

    private static DraftSession Hard(DateOnly date, PlanFactSheet sheet, string intensity = "tempo") =>
        new(D(date), "tempo", intensity, sheet.Envelope.Weeks.Single(w => w.Start <= date && date <= w.Start.AddDays(6)).Phase.ToWire(), 6_000, null, 270, 290, "Tempo run.");

    private static void AssertOnly(PlanValidation result, PlanRuleCode code, string? location = null)
    {
        Assert.False(result.IsValid);
        Assert.Equal([code], result.Codes);
        if (location is not null)
        {
            Assert.Contains(result.Violations, v => v.Location == location);
        }
    }

    [Fact]
    public void The_envelope_used_here_has_the_expected_shape()
    {
        var e = Sheet().Envelope;
        Assert.Equal((new DateOnly(2026, 11, 17), new DateOnly(2027, 1, 23), false, 10, 4), (e.PlanStart, e.PlanEnd, e.HorizonCapped, e.Weeks.Count, e.MaxSessionsPerWeek));
        Assert.Equal("BBBUUUUPPT", string.Concat(e.Weeks.Select(w => w.Phase switch
        {
            TrainingPhase.Base => 'B',
            TrainingPhase.Build => 'U',
            TrainingPhase.Peak => 'P',
            _ => 'T',
        })));
    }

    [Fact]
    public void A_plan_inside_every_bound_is_valid_and_kept_by_date()
    {
        var sheet = Sheet();
        var result = Validate(sheet, Good(sheet).AsEnumerable().Reverse(), "  A gradual plan.  ");

        Assert.True(result.IsValid, string.Join(", ", result.Violations));
        Assert.Equal(20, result.Kept.Count);
        Assert.Equal(result.Kept.OrderBy(s => s.Date).Select(s => s.Date), result.Kept.Select(s => s.Date));
        Assert.Equal("A gradual plan.", result.Notes);
        Assert.Equal(0, result.DroppedCount);
        var first = result.Kept[0];
        Assert.Equal((new DateOnly(2026, 11, 17), (short)20, TrainingIntensity.Easy, TrainingPhase.Base, 320, 360),
            (first.Date, first.RunTypeId, first.Intensity, first.Phase, first.TargetPaceMinSecondsPerKm!.Value, first.TargetPaceMaxSecondsPerKm!.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"notes\":\"x\",\"sessions\":[{\"date\":\"2026-11-17\"")]
    [InlineData("{\"notes\":\"x\"}")]
    [InlineData("{\"notes\":\"x\",\"sessions\":[],\"extra\":1}")]
    [InlineData("{\"notes\":1,\"sessions\":[]}")]
    [InlineData("[]")]
    public void Unparsable_or_schema_breaking_json_fails_schema_as_a_whole(string? json)
    {
        var result = TrainingPlanValidator.Validate(json, Sheet(), Policy);
        AssertOnly(result, PlanRuleCode.Schema, "draft");
        Assert.Empty(result.Kept);
    }

    [Fact]
    public void A_session_with_a_missing_member_or_a_wrong_type_fails_the_whole_draft()
    {
        const string MissingDescription = """{"notes":"x","sessions":[{"date":"2026-11-17","runType":"easy","intensity":"easy","phase":"base","distanceMeters":8000,"durationSeconds":null,"paceMinSecondsPerKm":null,"paceMaxSecondsPerKm":null}]}""";
        const string FractionalDuration = """{"notes":"x","sessions":[{"date":"2026-11-17","runType":"easy","intensity":"easy","phase":"base","distanceMeters":8000,"durationSeconds":2700.5,"paceMinSecondsPerKm":null,"paceMaxSecondsPerKm":null,"description":"Easy."}]}""";
        AssertOnly(TrainingPlanValidator.Validate(MissingDescription, Sheet(), Policy), PlanRuleCode.Schema, "draft");
        AssertOnly(TrainingPlanValidator.Validate(FractionalDuration, Sheet(), Policy), PlanRuleCode.Schema, "draft");
    }

    [Fact]
    public void The_json_round_trip_of_a_good_plan_is_valid()
    {
        var sheet = Sheet();
        var json = TrainingPlanDraftJson.ToJson(new TrainingPlanDraft("Plan.", Good(sheet)));
        Assert.True(TrainingPlanValidator.Validate(json, sheet, Policy).IsValid);
        Assert.Equal(Good(sheet), TrainingPlanDraftJson.TryParse(json)!.Sessions);
    }

    public static TheoryData<string, string> SchemaCases() => new()
    {
        { "date", "2026-13-01" },
        { "date", "17.11.2026" },
        { "runType", "bodyBar" },
        { "runType", "roadRace" },
        { "intensity", "hard" },
        { "phase", "recovery" },
        { "distanceMeters", "999" },
        { "distanceMeters", "60001" },
        { "distanceMeters", "null" },
        { "durationSeconds", "599" },
        { "durationSeconds", "21601" },
        { "paceMinSecondsPerKm", "149/200" },
        { "paceMinSecondsPerKm", "400/901" },
        { "paceMinSecondsPerKm", "360/320" },
        { "paceMinSecondsPerKm", "300/391" },
        { "paceMinSecondsPerKm", "300/null" },
        { "description", "" },
        { "description", "   " },
        { "description", "x301" },
        { "intensity", "rest-with-easy" },
    };

    [Theory]
    [MemberData(nameof(SchemaCases))]
    public void Each_session_field_rule_is_schema(string member, string value)
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        var s = sessions[2];
        static int? Int(string v) => v == "null" ? null : int.Parse(v, CultureInfo.InvariantCulture);
        sessions[2] = member switch
        {
            "date" => s with { Date = value },
            "runType" => s with { RunType = value },
            "intensity" when value == "rest-with-easy" => s with { Intensity = "rest" },
            "intensity" => s with { Intensity = value },
            "phase" => s with { Phase = value },
            "distanceMeters" => s with { DistanceMeters = value == "null" ? null : double.Parse(value, CultureInfo.InvariantCulture) },
            "durationSeconds" => s with { DurationSeconds = Int(value) },
            "paceMinSecondsPerKm" => s with { PaceMinSecondsPerKm = Int(value.Split('/')[0]), PaceMaxSecondsPerKm = Int(value.Split('/')[1]) },
            _ => s with { Description = value == "x301" ? new string('x', 301) : value },
        };

        var result = Validate(sheet, sessions);

        Assert.Contains(new PlanViolation(PlanRuleCode.Schema, $"sessions[2].{member}"), result.Violations);
        Assert.DoesNotContain(result.Kept, k => k.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == s.Date);
    }

    [Theory]
    [InlineData(8000.0, null, null, null, "easy")]
    [InlineData(null, 1800, null, null, "rest")]
    [InlineData(null, null, 300, 330, "rest")]
    public void A_rest_day_has_no_distance_duration_or_pace_and_goes_with_rest_intensity(double? meters, int? seconds, int? min, int? max, string intensity)
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        sessions.Add(new DraftSession("2026-11-18", "rest", intensity, "base", meters, seconds, min, max, "Rest day."));

        var result = Validate(sheet, sessions);

        AssertOnly(result, PlanRuleCode.Schema);
        Assert.All(result.Violations, v => Assert.StartsWith("sessions[20].", v.Location, StringComparison.Ordinal));
    }

    [Fact]
    public void A_proper_rest_day_is_valid_and_counts_for_neither_volume_nor_frequency()
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        sessions.Add(new DraftSession("2026-11-18", "rest", "rest", "base", null, null, null, null, "Rest day."));

        var result = Validate(sheet, sessions);

        Assert.True(result.IsValid, string.Join(", ", result.Violations));
        var rest = result.Kept.Single(k => k.Date == new DateOnly(2026, 11, 18));
        Assert.Equal((TrainingPlanValidatorTestsRest.RestType, TrainingIntensity.Rest, (double?)null), (rest.RunTypeId, rest.Intensity, rest.DistanceMeters));
    }

    [Fact]
    public void Notes_over_six_hundred_characters_and_a_session_count_out_of_range_are_schema()
    {
        var sheet = Sheet();
        AssertOnly(Validate(sheet, Good(sheet), new string('a', 601)), PlanRuleCode.Schema, "notes");
        Assert.Contains(new PlanViolation(PlanRuleCode.Schema, "sessions"), Validate(sheet, []).Violations);

        // 7 x 10 weeks = 70 is the most; 71 entries are refused before anything else.
        var many = Enumerable.Range(0, 71).Select(_ => Good(sheet)[0]).ToList();
        Assert.Contains(new PlanViolation(PlanRuleCode.Schema, "sessions"), Validate(sheet, many).Violations);
    }

    [Theory]
    [InlineData("2026-11-16")] // today
    [InlineData("2027-01-24")] // the goal day
    [InlineData("2027-02-01")]
    public void Dates_outside_the_plan_range_are_date_range(string date)
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        sessions.Add(sessions[0] with { Date = date });

        AssertOnly(Validate(sheet, sessions), PlanRuleCode.DateRange, "sessions[20]");
    }

    [Fact]
    public void Two_sessions_on_one_day_are_sessions_per_day()
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        sessions.Add(sessions[0] with { RunType = "recovery", Intensity = "recovery", DistanceMeters = 3_000 });

        AssertOnly(Validate(sheet, sessions), PlanRuleCode.SessionsPerDay, "2026-11-17");
    }

    [Fact]
    public void A_session_with_another_phase_than_its_week_is_phase()
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        sessions[0] = sessions[0] with { Phase = "build" };

        AssertOnly(Validate(sheet, sessions), PlanRuleCode.Phase, "2026-11-17");
    }

    [Fact]
    public void A_week_above_its_cap_by_more_than_five_percent_is_weekly_volume_and_at_five_percent_it_is_not()
    {
        var sheet = Sheet();
        var week = sheet.Envelope.Weeks[3];
        var cap = week.MaxDistanceMeters * TrainingPlanValidator.Tolerance;
        var sessions = Good(sheet).Where(s => DateOnly.ParseExact(s.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture) is var d && (d < week.Start || d > week.Start.AddDays(6))).ToList();
        // Five sessions in the week (Mon, Wed, Fri easy; Sat and Sun long), each under the long-run cap; frequency is raised by the history.
        var each = Math.Floor(cap / 5) + 1;
        var phase = week.Phase.ToWire();
        var over = VolumeDays.Select(i => new DraftSession(D(week.Start.AddDays(i)), "easy", "easy", phase, each, null, null, null, "Easy run.")).ToList();
        var sheetWithFrequency = Sheet(sessionsPerWeek: 6.0);
        Assert.True(each <= week.MaxLongRunMeters);

        AssertOnly(Validate(sheetWithFrequency, sessions.Concat(over)), PlanRuleCode.WeeklyVolume, D(week.Start));

        var at = over.Select(s => s with { DistanceMeters = Math.Floor(cap / 5) }).ToList();
        Assert.True(Validate(sheetWithFrequency, sessions.Concat(at)).IsValid);
    }

    [Fact]
    public void A_session_longer_than_the_long_run_cap_by_more_than_five_percent_is_long_run()
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        var week = sheet.Envelope.Weeks[0];
        sessions[1] = sessions[1] with { DistanceMeters = Math.Floor(week.MaxLongRunMeters * 1.05) + 1 };
        var result = Validate(sheet, sessions);
        Assert.Contains(PlanRuleCode.LongRun, result.Codes);
        Assert.Contains(new PlanViolation(PlanRuleCode.LongRun, sessions[1].Date), result.Violations);

        sessions[1] = sessions[1] with { DistanceMeters = Math.Floor(week.MaxLongRunMeters * 1.05) };
        Assert.DoesNotContain(PlanRuleCode.LongRun, Validate(sheet, sessions).Codes);
    }

    [Fact]
    public void More_sessions_than_max_sessions_per_week_with_the_fixed_ones_is_frequency()
    {
        // History of 3 sessions a week: at most 4. Week 2 already has a planned run, so 4 suggestions make 5.
        var commitment = new CommitmentFact(new DateOnly(2026, 11, 26), 20, 5_000, null, CommitmentKind.Planned, false);
        var sheet = Sheet([commitment]);
        var week = sheet.Envelope.Weeks[1];
        var sessions = Good(sheet).Where(s => s.Date != "2026-11-24" && s.Date != "2026-11-29").ToList();
        sessions.AddRange(FrequencyDays.Select(i => new DraftSession(D(week.Start.AddDays(i)), "easy", "easy", "base", 3_000, null, null, null, "Easy run.")));

        AssertOnly(Validate(sheet, sessions), PlanRuleCode.Frequency, "2026-11-23");
        Assert.True(Validate(sheet, sessions.Where(s => s.Date != "2026-11-23")).IsValid);
    }

    [Fact]
    public void Three_hard_sessions_in_a_week_are_hard_sessions_and_hard_commitments_count()
    {
        var sheet = Sheet(sessionsPerWeek: 4.0); // maxSessionsPerWeek 5, so the hard limit stays 2
        var week = sheet.Envelope.Weeks[4];
        var sessions = Good(sheet).Where(s => s.Date != D(week.Start.AddDays(1))).ToList();
        sessions.Add(Hard(week.Start, sheet));
        sessions.Add(Hard(week.Start.AddDays(2), sheet));
        Assert.True(Validate(sheet, sessions).IsValid, string.Join(", ", Validate(sheet, sessions).Violations));

        sessions.Add(Hard(week.Start.AddDays(4), sheet, "interval"));
        AssertOnly(Validate(sheet, sessions), PlanRuleCode.HardSessions, D(week.Start));

        // Two hard suggestions plus a race commitment (not adjacent) in the same week: three.
        var race = new CommitmentFact(week.Start.AddDays(4), 1, 10_000, null, CommitmentKind.Race, false);
        var withRace = Sheet([race], sessionsPerWeek: 4.0);
        AssertOnly(Validate(withRace, sessions.Where(s => s.Date != D(week.Start.AddDays(4)))), PlanRuleCode.HardSessions, D(week.Start));
    }

    [Fact]
    public void With_six_or_more_sessions_a_week_three_hard_sessions_are_allowed()
    {
        var sheet = Sheet(sessionsPerWeek: 5.0); // maxSessionsPerWeek 6
        var week = sheet.Envelope.Weeks[4];
        var sessions = Good(sheet).Where(s => s.Date != D(week.Start.AddDays(1))).ToList();
        sessions.AddRange([Hard(week.Start, sheet), Hard(week.Start.AddDays(2), sheet), Hard(week.Start.AddDays(4), sheet)]);

        Assert.True(Validate(sheet, sessions).IsValid, string.Join(", ", Validate(sheet, sessions).Violations));
    }

    [Fact]
    public void A_hard_session_next_to_another_hard_session_or_a_hard_commitment_is_hard_sessions()
    {
        var sheet = Sheet();
        var week = sheet.Envelope.Weeks[4];
        var sessions = Good(sheet).Where(s => s.Date != D(week.Start.AddDays(1))).ToList();
        AssertOnly(Validate(sheet, sessions.Append(Hard(week.Start.AddDays(2), sheet)).Append(Hard(week.Start.AddDays(3), sheet))), PlanRuleCode.HardSessions, D(week.Start.AddDays(2)));

        var intervals = new CommitmentFact(week.Start.AddDays(2), 24, 8_000, null, CommitmentKind.Planned, false);
        var withCommitment = Sheet([intervals]);
        AssertOnly(Validate(withCommitment, sessions.Append(Hard(week.Start.AddDays(3), withCommitment))), PlanRuleCode.HardSessions, D(week.Start.AddDays(3)));

        // An easy run next to a hard commitment is fine.
        Assert.True(Validate(withCommitment, sessions.Append(Good(withCommitment)[0] with { Date = D(week.Start.AddDays(3)), Phase = week.Phase.ToWire() })).IsValid);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(-3, true)]
    [InlineData(-4, false)]
    public void No_hard_session_in_the_last_three_days_before_the_goal(int daysBefore, bool violation)
    {
        var sheet = Sheet();
        var date = Target.AddDays(daysBefore);
        var sessions = Good(sheet).Where(s => s.Date != D(date)).ToList();
        // Keep the other sessions of the taper week away from the hard one.
        sessions = [.. sessions.Where(s => Math.Abs(DateOnly.ParseExact(s.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - date.DayNumber) > 1)];
        sessions.Add(Hard(date, sheet) with { DistanceMeters = 3_000 });

        var result = Validate(sheet, sessions);

        Assert.Equal(violation, result.Codes.Contains(PlanRuleCode.TaperIntensity));
        Assert.Equal(violation ? [PlanRuleCode.TaperIntensity] : [], result.Codes);
    }

    [Fact]
    public void With_a_capped_horizon_the_taper_rule_does_not_apply()
    {
        var far = Sheet(target: Today.AddDays(30 * 7));
        Assert.True(far.Envelope.HorizonCapped);
        Assert.True(Validate(far, Good(far)).IsValid, string.Join(", ", Validate(far, Good(far)).Violations));
    }

    [Theory]
    [InlineData("tempo", 229, true)]   // below 0.85 x 270 = 229.5
    [InlineData("tempo", 230, false)]
    [InlineData("easy", 269, true)]    // easy faster than the reference
    [InlineData("recovery", 269, true)]
    [InlineData("easy", 270, false)]
    [InlineData("steady", 250, false)]
    public void Paces_are_bounded_by_the_reference_pace(string intensity, int min, bool violation)
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        var type = intensity switch { "tempo" => "tempo", "recovery" => "recovery", "steady" => "progression", _ => "easy" };
        sessions[0] = sessions[0] with { RunType = type, Intensity = intensity, PaceMinSecondsPerKm = min, PaceMaxSecondsPerKm = min + 30 };

        var result = Validate(sheet, sessions);

        Assert.Equal(violation ? [PlanRuleCode.Pace] : [], result.Codes);
    }

    [Fact]
    public void Without_a_reference_pace_paces_are_not_checked_against_it()
    {
        var sheet = Sheet(referencePace: null);
        var sessions = Good(sheet);
        sessions[0] = sessions[0] with { PaceMinSecondsPerKm = 200, PaceMaxSecondsPerKm = 230 };
        Assert.True(Validate(sheet, sessions).IsValid);
    }

    [Fact]
    public void A_full_non_taper_week_without_running_is_empty_week_but_not_a_partial_or_taper_week_or_one_with_a_commitment()
    {
        var sheet = Sheet();
        var weeks = sheet.Envelope.Weeks;
        bool In(DraftSession s, PlanWeek w) => DateOnly.ParseExact(s.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture) is var d && d >= w.Start && d <= w.Start.AddDays(6);

        // The first week is partial (from Tuesday) and the last is taper: dropping them is fine.
        Assert.True(Validate(sheet, Good(sheet).Where(s => !In(s, weeks[0]) && !In(s, weeks[^1]))).IsValid);

        AssertOnly(Validate(sheet, Good(sheet).Where(s => !In(s, weeks[2]))), PlanRuleCode.EmptyWeek, D(weeks[2].Start));

        // A rest suggestion does not fill the week.
        var restOnly = Good(sheet).Where(s => !In(s, weeks[2])).Append(new DraftSession(D(weeks[2].Start), "rest", "rest", "base", null, null, null, null, "Rest day."));
        AssertOnly(Validate(sheet, restOnly), PlanRuleCode.EmptyWeek, D(weeks[2].Start));

        var withRun = Sheet([new CommitmentFact(weeks[2].Start.AddDays(2), 22, 15_000, null, CommitmentKind.Planned, true)]);
        Assert.True(Validate(withRun, Good(withRun).Where(s => !In(s, withRun.Envelope.Weeks[2]))).IsValid);
    }

    [Theory]
    [InlineData("<b>Easy</b> run.")]
    [InlineData("See [this](x) plan.")]
    [InlineData("**Easy** run.")]
    [InlineData("# Easy run")]
    [InlineData("Easy run, details at https://example.com")]
    [InlineData("Easy run, see www.example.com")]
    [InlineData("Easy\u0007run")]
    public void Markup_in_a_description_or_the_notes_is_markup(string text)
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        sessions[3] = sessions[3] with { Description = text };
        AssertOnly(Validate(sheet, sessions), PlanRuleCode.Markup, "sessions[3].description");
        AssertOnly(Validate(sheet, Good(sheet), text), PlanRuleCode.Markup, "notes");
    }

    [Theory]
    [InlineData("Stop if you feel pain.")]
    [InlineData("Easy, to avoid injury.")]
    [InlineData("Ask your doctor first.")]
    [InlineData("Lowers the risk of overtraining.")]
    public void A_deny_listed_topic_is_forbidden_topic(string text)
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        sessions[4] = sessions[4] with { Description = text };
        Assert.Contains(new PlanViolation(PlanRuleCode.ForbiddenTopic, "sessions[4].description"), Validate(sheet, sessions).Violations);
        Assert.Contains(new PlanViolation(PlanRuleCode.ForbiddenTopic, "notes"), Validate(sheet, Good(sheet), text).Violations);
    }

    [Fact]
    public void Digits_are_allowed_in_descriptions()
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        sessions[0] = sessions[0] with { Description = "6 x 800 m at 5K effort, 2 min jog between." };
        Assert.True(Validate(sheet, sessions).IsValid);
    }

    [Fact]
    public void Sessions_on_running_rest_or_other_goal_days_are_dropped_and_counted_but_cross_training_does_not_block()
    {
        CommitmentFact[] commitments =
        [
            new(new DateOnly(2026, 11, 17), 20, 6_000, null, CommitmentKind.Planned, false),        // easy run on the first Tuesday
            new(new DateOnly(2026, 11, 24), 41, null, null, CommitmentKind.Planned, false),          // rest day
            new(new DateOnly(2026, 12, 1), null, null, null, CommitmentKind.OtherGoal, false),       // another goal's date
            new(new DateOnly(2026, 12, 8), 42, null, 2_700, CommitmentKind.Planned, false),          // Body Bar: does not block
            new(new DateOnly(2026, 12, 15), 40, null, 2_400, CommitmentKind.Planned, false),         // cross-training: does not block
        ];
        var sheet = Sheet(commitments);

        var result = Validate(sheet, Good(sheet));

        Assert.True(result.IsValid, string.Join(", ", result.Violations));
        Assert.Equal(3, result.DroppedCount);
        Assert.Equal(17, result.Kept.Count);
        Assert.DoesNotContain(result.Kept, k => k.Date == new DateOnly(2026, 11, 17) || k.Date == new DateOnly(2026, 11, 24) || k.Date == new DateOnly(2026, 12, 1));
        Assert.Contains(result.Kept, k => k.Date == new DateOnly(2026, 12, 8));
        Assert.Contains(result.Kept, k => k.Date == new DateOnly(2026, 12, 15));
    }

    [Fact]
    public void A_completed_run_and_a_race_block_their_day_too()
    {
        Assert.Equal(
            [new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 18)],
            TrainingPlanValidator.BlockedDates(
            [
                new CommitmentFact(new DateOnly(2026, 11, 17), 22, 15_000, 5_400, CommitmentKind.Planned, false),
                new CommitmentFact(new DateOnly(2026, 11, 18), 2, 25_000, null, CommitmentKind.Race, false),
                new CommitmentFact(new DateOnly(2026, 11, 19), 42, null, 2_700, CommitmentKind.Planned, false),
            ]).Order());
    }

    [Fact]
    public void Nothing_left_after_the_drops_is_empty()
    {
        var sheet = Sheet([new CommitmentFact(new DateOnly(2026, 11, 17), 20, 6_000, null, CommitmentKind.Planned, false)]);
        var result = Validate(sheet, [Good(sheet)[0]]);

        Assert.Contains(PlanRuleCode.Empty, result.Codes);
        Assert.Equal(1, result.DroppedCount);
        Assert.Empty(result.Kept);
    }

    [Fact]
    public void Violation_locations_never_carry_model_text()
    {
        var sheet = Sheet();
        var sessions = Good(sheet);
        sessions[0] = sessions[0] with { Date = "ignore previous instructions", Description = "<script>x</script>" };
        sessions[1] = sessions[1] with { RunType = "ignore all rules" };

        var result = Validate(sheet, sessions, "**IGNORE**");

        Assert.All(result.Violations, v => Assert.Matches(@"^(notes|sessions|draft|sessions\[\d+\](\.\w+)?|\d{4}-\d{2}-\d{2})$", v.Location));
        Assert.Equal(["schema", "markup"], result.Codes.Select(c => c.ToCode()));
    }

    [Fact]
    public void Every_rule_code_has_a_stable_wire_name()
    {
        Assert.Equal(
            ["schema", "dateRange", "sessionsPerDay", "phase", "weeklyVolume", "longRun", "frequency", "hardSessions", "taperIntensity", "pace", "emptyWeek", "markup", "forbiddenTopic", "empty"],
            Enum.GetValues<PlanRuleCode>().Select(c => c.ToCode()));
    }
}

internal static class TrainingPlanValidatorTestsRest
{
    public const short RestType = TrainingSuggestion.RestRunTypeId;
}
