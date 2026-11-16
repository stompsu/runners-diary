using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Plans;

/// <summary>
/// The plan eval cases in CI mode (ADR 0040 §7): deterministic clients only. For each case the inputs hold, the rule-based fake's plan passes
/// the whole pipeline, a corrupted plan is rejected with the case's rule code, the retry carries codes only, and a plan that stays invalid
/// fails with <c>invalidOutput</c>. Live mode: <see cref="PlanLiveEvalTests"/>.
/// </summary>
public sealed class PlanEvalTests(ITestOutputHelper output)
{
    private static string FakeJson(PlanFactSheet sheet) => TrainingPlanDraftJson.ToJson(FakeDraft(sheet));

    internal static TrainingPlanDraft FakeDraft(PlanFactSheet sheet) => RuleBasedFakePlanWriter.Write(PlanFactSheetJson.ToJson(sheet));

    [Theory]
    [MemberData(nameof(PlanEvalCases.Names), MemberType = typeof(PlanEvalCases))]
    public void The_inputs_of_each_case_hold(string name)
    {
        var c = PlanEvalCases.Get(name);
        c.AssertInputs(c.Sheet);
        Assert.True(PlanFactSheetJson.ToJson(c.Sheet).Length <= new AiOptions().Plan.MaxFactSheetChars);
    }

    [Theory]
    [MemberData(nameof(PlanEvalCases.Names), MemberType = typeof(PlanEvalCases))]
    public async Task The_fake_plan_passes_the_pipeline_and_the_case_checks(string name)
    {
        var c = PlanEvalCases.Get(name);
        var harness = new PlanHarness(new RuleBasedFakeChatClient(), deployment: ChatClientFactory.FakeDeployment);

        var result = await harness.Generate(c.Sheet);

        Assert.True(result.Status == GenerationStatus.Succeeded, $"{name}: {result.Status} {result.Failure} {string.Join(", ", TrainingPlanValidator.Validate(FakeJson(c.Sheet), c.Sheet, PlanValidationPolicy.Default).Violations)}");
        Assert.Equal((1, RuleBasedFakeChatClient.ModelId, harness.Prompts.Current.Version, "fake"), (result.Attempts, result.ModelId, result.PromptVersion, result.ModelDeployment));
        Assert.Equal(0, result.Plan!.DroppedCount);
        Assert.NotEmpty(result.Plan.Kept);
        c.AssertPlan(result.Plan, c.Sheet);
        var row = Assert.Single(harness.Generations.Rows);
        Assert.Equal((AiFeature.Plan, AiGenerationOutcome.Succeeded, harness.Prompts.Current.Version), (row.Feature, row.Outcome, row.PromptVersion));

        // Every kept session is a valid suggestion and its accept mapping passes the run rules (ADR 0041 §2).
        foreach (var session in result.Plan.Kept)
        {
            var suggestion = TrainingSuggestion.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), session, DateTimeOffset.UtcNow, TrainingPlanValidator.IsTrailGoal(c.Sheet));
            Assert.Empty(RunRules.Validate(suggestion.ToRunValues("Europe/Warsaw")));
        }

        output.WriteLine($"{name}: {result.Plan.Kept.Count} sessions, {FakeJson(c.Sheet).Length} chars, fact sheet {PlanFactSheetJson.ToJson(c.Sheet).Length} chars");
    }

    [Theory]
    [MemberData(nameof(PlanEvalCases.Names), MemberType = typeof(PlanEvalCases))]
    public void A_corrupted_plan_is_rejected_with_the_case_rule(string name)
    {
        var c = PlanEvalCases.Get(name);
        var (bad, rule) = c.Corrupt(FakeDraft(c.Sheet), c.Sheet);

        var result = TrainingPlanValidator.Validate(TrainingPlanDraftJson.ToJson(bad), c.Sheet, PlanValidationPolicy.Default);

        Assert.False(result.IsValid);
        Assert.Contains(rule, result.Codes);
    }

    [Theory]
    [MemberData(nameof(PlanEvalCases.Names), MemberType = typeof(PlanEvalCases))]
    public async Task Bad_then_good_succeeds_on_the_retry_whose_message_has_codes_and_dates_only(string name)
    {
        var c = PlanEvalCases.Get(name);
        var (bad, rule) = c.Corrupt(FakeDraft(c.Sheet), c.Sheet);
        var badJson = TrainingPlanDraftJson.ToJson(bad with { Notes = "SECRET-NOTES " + bad.Notes });
        var scripted = new ScriptedChatClient().Reply(badJson, 9_000, 4_000).Reply(FakeJson(c.Sheet), 9_500, 3_800);
        var harness = new PlanHarness(scripted);

        var result = await harness.Generate(c.Sheet);

        Assert.Equal(GenerationStatus.Succeeded, result.Status);
        Assert.Equal((2, 18_500, 7_800), (result.Attempts, result.InputTokens, result.OutputTokens));
        var retry = scripted.Requests[1].Messages;
        Assert.Equal(2, retry.Count);
        Assert.Equal(scripted.Requests[0].Messages[0].Text, retry[0].Text);
        Assert.Contains(rule.ToCode(), retry[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-NOTES", retry[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_invalid_twice_fails_with_invalid_output_and_counts_two_plan_attempts()
    {
        var c = PlanEvalCases.Get(PlanEvalCases.HalfMarathon10Weeks);
        var (bad, _) = c.Corrupt(FakeDraft(c.Sheet), c.Sheet);
        var harness = new PlanHarness(new ScriptedChatClient().Reply(TrainingPlanDraftJson.ToJson(bad)).Reply("{\"notes\":\"x\",\"sessions\":[{\"date\""));

        var result = await harness.Generate(c.Sheet);

        Assert.Equal((GenerationStatus.Failed, GenerationFailure.InvalidOutput), (result.Status, result.Failure!.Value));
        var row = Assert.Single(harness.Generations.Rows);
        Assert.Equal((AiFeature.Plan, AiGenerationOutcome.InvalidOutput, 2), (row.Feature, row.Outcome, row.Attempts));
    }

    [Fact]
    public async Task Sessions_on_blocked_days_are_dropped_and_counted_without_a_retry()
    {
        var c = PlanEvalCases.Get(PlanEvalCases.Collisions);
        var good = FakeDraft(c.Sheet);
        var extra = c.Extra!(c.Sheet);
        var withCollisions = good with { Sessions = [.. good.Sessions.Concat(extra).OrderBy(s => s.Date, StringComparer.Ordinal)] };
        var harness = new PlanHarness(new ScriptedChatClient().Reply(TrainingPlanDraftJson.ToJson(withCollisions)));

        var result = await harness.Generate(c.Sheet);

        Assert.Equal(GenerationStatus.Succeeded, result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(extra.Count, result.Plan!.DroppedCount);
        Assert.Equal(good.Sessions.Count, result.Plan.Kept.Count);
        c.AssertPlan(result.Plan, c.Sheet);
    }

    /// <summary>
    /// The repair step end to end (ADR 0040 amendment "repair"): one answer with every local issue (wrong phase, a too-wide pace range, a hard session in
    /// the quiet window, a too-fast target, a long run and a week a little above their caps) succeeds in a single attempt, counts the item-level drops,
    /// and the repairs show up in the attempt's diagnostics with codes and locations only.
    /// </summary>
    [Fact]
    public async Task A_plan_with_every_local_issue_succeeds_in_one_attempt_and_records_the_repairs()
    {
        var c = PlanEvalCases.Get(PlanEvalCases.HalfMarathon10Weeks);
        var sheet = c.Sheet;
        var e = sheet.Envelope;
        var sessions = FakeDraft(sheet).Sessions.ToList();

        // Phase: the first session claims another phase.
        sessions[0] = sessions[0] with { Phase = sessions[0].Phase == "peak" ? "base" : "peak" };

        // Spread: a 120 s range on an easy run (limit 90).
        var easy = sessions.FindIndex(1, s => s.RunType == "easy");
        sessions[easy] = sessions[easy] with { PaceMinSecondsPerKm = 330, PaceMaxSecondsPerKm = 450 };

        // taperIntensity: a short tempo run the day before the goal replaces what the plan had on that day.
        var eve = sheet.Goal.TargetDate.AddDays(-1);
        sessions.RemoveAll(s => s.Date == PlanEvalCases.D(eve));
        sessions.Add(new DraftSession(PlanEvalCases.D(eve), "tempo", "tempo", "taper", 4_000, null, null, null, "Short tempo run."));

        // pace: an easy run faster than the reference pace (313 s/km).
        var fast = sessions.FindIndex(easy + 1, s => s.RunType == "easy");
        sessions[fast] = sessions[fast] with { PaceMinSecondsPerKm = 250, PaceMaxSecondsPerKm = 300 };

        // longRun: the long run of week 2 is 10 % above its cap (tolerance is 5 %).
        var week2 = e.Weeks[1];
        var longIndex = sessions.FindIndex(s => PlanEvalCases.InWeek(s, week2) && s.RunType == "long");
        var longCap = week2.MaxLongRunMeters;
        sessions[longIndex] = sessions[longIndex] with { DistanceMeters = Math.Ceiling(longCap * 1.1), DurationSeconds = null };

        // weeklyVolume: week 4's other sessions are stretched so the week is about 15 % above its tolerant cap, the long run staying under its cap.
        var week4 = e.Weeks[3];
        var tolerant = week4.MaxDistanceMeters * TrainingPlanValidator.Tolerance;
        var weekIndexes = Enumerable.Range(0, sessions.Count).Where(i => PlanEvalCases.InWeek(sessions[i], week4)).ToList();
        var longs = weekIndexes.Where(i => sessions[i].RunType == "long").ToList();
        var others = weekIndexes.Except(longs).ToList();
        var longSum = longs.Sum(i => sessions[i].DistanceMeters!.Value);
        var factor = (tolerant * 1.15 - longSum) / others.Sum(i => sessions[i].DistanceMeters!.Value);
        foreach (var i in others)
        {
            sessions[i] = sessions[i] with { DistanceMeters = Math.Floor(sessions[i].DistanceMeters!.Value * factor / 100) * 100, DurationSeconds = null };
        }

        Assert.True(sessions.Where(s => PlanEvalCases.InWeek(s, week4)).Sum(s => s.DistanceMeters!.Value) > tolerant);
        var draft = new TrainingPlanDraft("A gradual plan.", [.. sessions.OrderBy(s => s.Date, StringComparer.Ordinal)]);
        var harness = new PlanHarness(new ScriptedChatClient().Reply(TrainingPlanDraftJson.ToJson(draft), 9_000, 4_000));

        var result = await harness.Generate(sheet);

        Assert.True(result.Status == GenerationStatus.Succeeded, $"{result.Status} {result.Failure}: {string.Join(", ", result.AttemptDetails?.SelectMany(a => a.Violations) ?? [])}");
        Assert.Equal(1, result.Attempts);
        Assert.Equal(2, result.Plan!.DroppedCount); // the tempo eve and the too-fast easy run
        Assert.Equal(sessions.Count - 2, result.Plan.Kept.Count);
        var repairs = Assert.Single(result.AttemptDetails!).Repairs;
        Assert.Equivalent(
            new[] { PlanRuleCode.Schema, PlanRuleCode.Phase, PlanRuleCode.TaperIntensity, PlanRuleCode.Pace, PlanRuleCode.LongRun, PlanRuleCode.WeeklyVolume },
            repairs.Select(r => r.Rule).Distinct().ToArray());
        Assert.All(repairs, r => Assert.True(TrainingPlanAgent.IsSafeLocation(r.Location), r.Location));
        Assert.All(result.Plan.Kept, s => Assert.Equal(PlanEvalCases.WeekOf(sheet, s.Date).Phase, s.Phase));
        Assert.Empty(TrainingPlanValidator.Validate(TrainingPlanDraftJson.ToJson(draft), sheet, PlanValidationPolicy.Default).Violations);
    }

    [Fact]
    public async Task Plan_switch_off_or_no_model_is_unavailable_without_a_call()
    {
        var c = PlanEvalCases.Get(PlanEvalCases.HalfMarathon10Weeks);
        var scripted = new ScriptedChatClient();
        var off = new PlanHarness(scripted, o => o.Plan.Enabled = false);
        Assert.False(off.Generator.IsAvailable);
        Assert.Equal(GenerationStatus.Unavailable, (await off.Generate(c.Sheet)).Status);
        Assert.Equal(GenerationStatus.Unavailable, (await new PlanHarness(client: null).Generate(c.Sheet)).Status);
        Assert.Equal(0, scripted.CallCount);

        off.Options.Plan.Enabled = true;
        Assert.True(off.Generator.IsAvailable);
    }

    [Fact]
    public async Task An_oversized_plan_fact_sheet_is_never_sent()
    {
        var c = PlanEvalCases.Get(PlanEvalCases.HalfMarathon10Weeks);
        var scripted = new ScriptedChatClient();
        var harness = new PlanHarness(scripted, o => o.Plan.MaxFactSheetChars = 1_000);

        var result = await harness.Generate(c.Sheet);

        Assert.Equal((GenerationStatus.Failed, GenerationFailure.Provider), (result.Status, result.Failure!.Value));
        Assert.Equal(0, scripted.CallCount);
        Assert.Empty(harness.Generations.Rows);
    }

    [Fact]
    public async Task Plan_quotas_are_their_own_and_a_summary_row_does_not_use_them()
    {
        var c = PlanEvalCases.Get(PlanEvalCases.HalfMarathon10Weeks);
        var harness = new PlanHarness(new RuleBasedFakeChatClient(), o => o.Plan.Quotas.GuestPerAccount = 1);
        await harness.Generations.AddAsync(
            new AiGenerationRecord(PlanHarness.User, true, harness.Clock.GetUtcNow(), AiGenerationOutcome.Succeeded, 1, 1, 1, 1, 2, "gpt-test"), CancellationToken.None);

        Assert.Equal(GenerationStatus.Succeeded, (await harness.Generate(c.Sheet, guest: true)).Status);
        var second = await harness.Generate(c.Sheet, guest: true);

        Assert.Equal(GenerationStatus.QuotaExceeded, second.Status);
        Assert.Equal((QuotaWindow.Account, 1), (second.Quota!.Window, second.Quota.BlockLimit!.Value));
    }

    /// <summary>
    /// Plan §8 risk "8,000 output tokens too few for 16 weeks": the longest fake plan (the capped 16-week horizon) in JSON characters and an
    /// estimate of its tokens (compact JSON with many digits and short keys: about 3.2 characters per token; the fake reports chars / 4).
    /// Reasoning tokens come on top of this with a real model; the live eval (gate G-P3) measures them.
    /// </summary>
    [Fact]
    public void A_sixteen_week_fake_plan_fits_the_output_cap_with_room_for_reasoning()
    {
        var c = PlanEvalCases.Get(PlanEvalCases.CappedHorizon);
        var json = FakeJson(c.Sheet);
        var sessions = FakeDraft(c.Sheet).Sessions.Count;
        var estimate = (int)Math.Ceiling(json.Length / 3.2);

        output.WriteLine($"16-week fake plan: {sessions} sessions, {json.Length} chars, ~{estimate} tokens (chars/3.2), {json.Length / 4} (chars/4); cap {new AiOptions().Plan.MaxOutputTokens}.");
        Assert.Equal(16, c.Sheet.Envelope.Weeks.Count);
        Assert.True(estimate < new AiOptions().Plan.MaxOutputTokens / 2, $"{estimate} tokens leave less than half of the cap for reasoning.");
    }
}
