using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Pipeline;

/// <summary>
/// The Foundry client as it goes over the wire (ADR 0027 §1), with a capturing transport and a fake credential: no network, no key.
/// </summary>
public sealed class FoundryWireTests
{
    private static readonly AiOptions Options = new()
    {
        Provider = AiProvider.Foundry,
        Foundry = { Endpoint = "https://res.openai.azure.com/", Deployment = "gpt-5.4-mini" },
    };

    [Fact]
    public async Task The_request_is_a_keyless_v1_chat_completion_with_strict_structured_output_and_the_caps()
    {
        using var transport = new CapturingTransport().RespondCompletion(EvalFiles.GoodDraft(EvalCases.LoadSpike));
        var credential = new RecordingCredential();
        var client = ChatClientFactory.Wrap(ChatClientFactory.CreateFoundryClient(Options, credential, transport.Transport), NullLoggerFactory.Instance);
        var agent = new TrainingInsightsAgent(client, PromptCatalog.Load(RepoPaths.PromptsInOutput).Current, Options);

        var attempt = await agent.RunAsync(FactSheetJson.ToJson(EvalCases.Get(EvalCases.LoadSpike).Sheet), "en", null, TestContext.Current.CancellationToken);

        var request = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("res.openai.azure.com", request.Uri.Host);
        Assert.Equal("/openai/v1/chat/completions", request.Uri.AbsolutePath);
        Assert.Equal("Bearer " + RecordingCredential.Token, request.Authorization);
        Assert.Equal(["https://ai.azure.com/.default"], Assert.Single(credential.RequestedScopes));

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("gpt-5.4-mini", root.GetProperty("model").GetString());
        Assert.Equal(1500, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal("low", root.GetProperty("reasoning_effort").GetString());
        Assert.False(root.TryGetProperty("temperature", out _), "temperature is not supported by reasoning models.");
        Assert.False(root.TryGetProperty("max_tokens", out _));
        Assert.False(root.TryGetProperty("strict", out _), "strict belongs inside response_format.json_schema, not at the top level.");
        Assert.False(root.TryGetProperty("tools", out _));

        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var jsonSchema = format.GetProperty("json_schema");
        Assert.Equal(TrainingInsightsAgent.SchemaName, jsonSchema.GetProperty("name").GetString());
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());
        var schemaText = jsonSchema.GetProperty("schema").GetRawText();
        foreach (var unsupported in new[] { "\"$ref\"", "\"maxLength\"", "\"minItems\"", "\"maxItems\"" })
        {
            Assert.DoesNotContain(unsupported, schemaText, StringComparison.Ordinal);
        }

        Assert.Equal(JsonValueKind.False, jsonSchema.GetProperty("schema").GetProperty("additionalProperties").ValueKind);

        // Instructions travel as the system/developer message; the fact sheet as the user message.
        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[1].GetProperty("role").GetString());

        Assert.Equal((5000, 600, "gpt-5.4-mini-2026-03-17"), (attempt.InputTokens, attempt.OutputTokens, attempt.ModelId));
    }
}
