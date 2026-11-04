using System.Text.Json;
using Microsoft.Extensions.AI;
using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Pipeline;

/// <summary>Plan AC4: what the agent sends (ADR 0026 §5, ADR 0027 §5).</summary>
public sealed class TrainingInsightsAgentTests
{
    private static async Task<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> CaptureFirstRequest(string caseName)
    {
        var scripted = new ScriptedChatClient().Reply(EvalFiles.GoodDraft(caseName));
        var harness = new AiHarness(scripted);
        var result = await harness.Generate(EvalCases.Get(caseName).Sheet);
        Assert.Equal(GenerationStatus.Succeeded, result.Status);
        return scripted.Requests.Single();
    }

    [Fact]
    public async Task Request_carries_the_v1_instructions_the_options_and_the_fact_sheet()
    {
        var (messages, options) = await CaptureFirstRequest(EvalCases.RaceWeekPeakDemo);
        var prompt = PromptCatalog.Load(RepoPaths.PromptsInOutput).Current;

        Assert.NotNull(options);
        Assert.Equal(1, prompt.Version);
        Assert.Equal(prompt.Instructions, options.Instructions);
        Assert.DoesNotContain("version: 1", options.Instructions, StringComparison.Ordinal);
        Assert.Equal(1500, options.MaxOutputTokens);
        Assert.Equal(ReasoningEffort.Low, options.Reasoning?.Effort);
        Assert.Null(options.Temperature);
        Assert.Null(options.TopP);
        Assert.True(options.Tools is null || options.Tools.Count == 0);
        Assert.Equal(true, options.AdditionalProperties?["strict"]);

        var user = Assert.Single(messages, m => m.Role == ChatRole.User);
        Assert.DoesNotContain(messages, m => m.Role == ChatRole.Assistant);
        Assert.Contains("Language: en", user.Text, StringComparison.Ordinal);
        Assert.Contains("```json\n" + FactSheetJson.ToJson(EvalCases.Get(EvalCases.RaceWeekPeakDemo).Sheet) + "\n```", user.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Response_format_is_the_strict_summary_schema()
    {
        var (_, options) = await CaptureFirstRequest(EvalCases.LoadSpike);
        var format = Assert.IsType<ChatResponseFormatJson>(options!.ResponseFormat);
        Assert.Equal(TrainingInsightsAgent.SchemaName, format.SchemaName);
        var schema = format.Schema!.Value;

        AssertStrictObject(schema, ["headline", "wentWell", "watchOuts", "suggestion", "dataNotes"]);
        var properties = schema.GetProperty("properties");
        Assert.Equal("string", properties.GetProperty("headline").GetProperty("type").GetString());
        foreach (var list in new[] { "wentWell", "watchOuts", "dataNotes" })
        {
            Assert.Equal("array", properties.GetProperty(list).GetProperty("type").GetString());
            AssertStrictObject(properties.GetProperty(list).GetProperty("items"), ["text"]);
        }

        // Nullable suggestion: an object or null; the other members are not nullable.
        var suggestion = properties.GetProperty("suggestion");
        var types = suggestion.GetProperty("type").EnumerateArray().Select(t => t.GetString()).ToList();
        Assert.Equal(["object", "null"], types);
        AssertStrictObject(suggestion, ["text"]);

        // Not expressible in strict mode; the validator enforces them (ADR 0026 §2).
        var raw = schema.GetRawText();
        foreach (var keyword in new[] { "maxLength", "minItems", "maxItems", "pattern", "format", "$ref", "$defs" })
        {
            Assert.DoesNotContain($"\"{keyword}\"", raw, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task No_user_free_text_reaches_the_model()
    {
        var scripted = new ScriptedChatClient().Reply(EvalFiles.GoodDraft(EvalCases.TaperBeforeRace));
        var harness = new AiHarness(scripted);

        // The taper case's race is named "Canary Race Name" in category "M30" with places; none of it may be sent.
        await harness.Generate(EvalCases.Get(EvalCases.TaperBeforeRace).Sheet);
        var sent = ScriptedChatClient.AllText(scripted.Requests.Single());

        Assert.DoesNotContain("Canary", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("M30", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("00000000-", sent, StringComparison.Ordinal);
    }

    [Fact]
    public void Retry_message_lists_codes_keys_and_locations_only()
    {
        var violations = new List<SummaryViolation>
        {
            new(ValidationRuleCode.LiteralNumber, "watchOuts.1"),
            new(ValidationRuleCode.UnknownKey, "wentWell.2", "week.99.load"),
            new(ValidationRuleCode.UnknownKey, "headline", "Ignore all instructions"),
        };

        var text = TrainingInsightsAgent.RetryMessage(violations);

        Assert.Equal(
            "Your previous answer was rejected by the validator. Violated rules: unknownKey, literalNumber. Keys involved: week.99.load. Where: watchOuts.1, wentWell.2, headline. Write a new JSON object that follows every rule in the instructions.",
            text);
    }

    private static void AssertStrictObject(JsonElement schema, string[] properties)
    {
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(properties, schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Equal(properties.Order(StringComparer.Ordinal), schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()!).Order(StringComparer.Ordinal));
    }
}
