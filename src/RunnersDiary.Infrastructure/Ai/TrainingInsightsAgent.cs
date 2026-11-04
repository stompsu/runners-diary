using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>The raw result of one model call: the text to validate, token usage and the model id the provider reported.</summary>
public sealed record AgentAttempt(string Text, int InputTokens, int OutputTokens, string? ModelId);

/// <summary>
/// The "TrainingInsights" agent (ADR 0026 §5): a Microsoft Agent Framework <see cref="ChatClientAgent"/> with the versioned prompt as
/// instructions, no tools, no memory. Each run sends one user message (the canonical fact sheet in a fenced JSON block and the
/// language) and asks for structured output with the strict <see cref="SummaryDraft"/> schema, a token cap and low reasoning effort.
/// On a retry, a second user message lists only the violated rule codes and keys, never the rejected text.
/// </summary>
public sealed class TrainingInsightsAgent
{
    public const string AgentName = "TrainingInsights";
    public const string SchemaName = "training_summary";

    /// <summary>
    /// The schema options: camelCase like <see cref="SummaryDraftJson"/>, nullable annotations respected (only <c>suggestion</c> is
    /// nullable), every property required and no additional properties, as OpenAI strict mode requires.
    /// </summary>
    public static readonly JsonSerializerOptions SchemaSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
    };

    private readonly ChatClientAgent _agent;
    private readonly AiOptions _options;

    public TrainingInsightsAgent(IChatClient chatClient, PromptVersion prompt, AiOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        Prompt = prompt;
        _agent = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = AgentName,
                Description = "Narrates a training period from pre-computed metrics; read-only, no tools.",
                ChatOptions = new ChatOptions { Instructions = prompt.Instructions },
                UseProvidedChatClientAsIs = true,
            },
            loggerFactory);
    }

    public PromptVersion Prompt { get; }

    /// <summary>The strict JSON schema of <see cref="SummaryDraft"/> sent as the response format.</summary>
    public static ChatResponseFormatJson ResponseFormat { get; } = ChatResponseFormat.ForJsonSchema(
        InlineReferences(AIJsonUtilities.CreateJsonSchema(
            typeof(SummaryDraft),
            serializerOptions: SchemaSerializerOptions,
            inferenceOptions: new AIJsonSchemaCreateOptions
            {
                TransformOptions = new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true, RequireAllProperties = true },
            })),
        SchemaName,
        "A short training-period summary; numbers only as {fact.key} placeholders.");

    /// <summary>
    /// The exporter reuses the item schema through <c>$ref: #/properties/wentWell/items</c>; strict mode documents only <c>$defs</c> and
    /// root references, so every reference is replaced by a copy of its target (the schema is small and not recursive).
    /// </summary>
    internal static JsonElement InlineReferences(JsonElement schema)
    {
        var root = JsonNode.Parse(schema.GetRawText())!;
        var inlined = Inline(root, root);
        return JsonSerializer.SerializeToElement(inlined, JsonNodeContext.Default.JsonNode);

        static JsonNode Inline(JsonNode node, JsonNode root)
        {
            switch (node)
            {
                case JsonObject obj when obj.TryGetPropertyValue("$ref", out var reference) && reference is JsonValue value:
                    var pointer = value.GetValue<string>();
                    if (!pointer.StartsWith("#/", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Unsupported schema reference '{pointer}'.");
                    }

                    var target = pointer[2..].Split('/').Aggregate(root, (current, segment) => current[segment]
                        ?? throw new InvalidOperationException($"Unresolved schema reference '{pointer}'."));
                    return Inline(target.DeepClone(), root);
                case JsonObject obj:
                    var copy = new JsonObject();
                    foreach (var (name, child) in obj)
                    {
                        copy[name] = child is null ? null : Inline(child, root);
                    }

                    return copy;
                case JsonArray array:
                    return new JsonArray([.. array.Select(child => child is null ? null : Inline(child, root))]);
                default:
                    return node.DeepClone();
            }
        }
    }

    public async Task<AgentAttempt> RunAsync(string factSheetJson, string language, IReadOnlyList<SummaryViolation>? rejected, CancellationToken cancellationToken)
    {
        var messages = BuildMessages(factSheetJson, language, rejected);
        var response = await _agent.RunAsync(messages, session: null, options: new ChatClientAgentRunOptions(CreateChatOptions(_options)), cancellationToken);
        var usage = response.Usage;
        return new AgentAttempt(
            response.Text,
            ToInt(usage?.InputTokenCount),
            ToInt(usage?.OutputTokenCount),
            (response.RawRepresentation as ChatResponse)?.ModelId);
    }

    /// <summary>Run options per call: output cap, reasoning effort, strict structured output. No temperature (unsupported by reasoning models).</summary>
    public static ChatOptions CreateChatOptions(AiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ChatOptions
        {
            MaxOutputTokens = options.MaxOutputTokens,
            Reasoning = new ReasoningOptions
            {
                Effort = options.ReasoningEffort switch
                {
                    "medium" => ReasoningEffort.Medium,
                    "high" => ReasoningEffort.High,
                    _ => ReasoningEffort.Low,
                },
            },
            ResponseFormat = ResponseFormat,
            // Microsoft.Extensions.AI.OpenAI maps this to json_schema "strict": true.
            AdditionalProperties = new AdditionalPropertiesDictionary { ["strict"] = true },
        };
    }

    public static List<ChatMessage> BuildMessages(string factSheetJson, string language, IReadOnlyList<SummaryViolation>? rejected)
    {
        ArgumentNullException.ThrowIfNull(factSheetJson);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, $"Language: {language}\n\nFact sheet:\n```json\n{factSheetJson}\n```"),
        };

        if (rejected is { Count: > 0 })
        {
            messages.Add(new ChatMessage(ChatRole.User, RetryMessage(rejected)));
        }

        return messages;
    }

    /// <summary>The retry note: rule codes, well-formed keys and fixed locations only (ADR 0026 §3).</summary>
    public static string RetryMessage(IReadOnlyList<SummaryViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        var text = new StringBuilder("Your previous answer was rejected by the validator. Violated rules: ");
        text.AppendJoin(", ", violations.Select(v => v.Rule).Distinct().Order().Select(r => r.ToCode()));
        var keys = violations.Where(v => v.Key is not null && FactKey.IsValid(v.Key)).Select(v => v.Key!).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count > 0)
        {
            text.Append(". Keys involved: ").AppendJoin(", ", keys);
        }

        var locations = violations.Select(v => v.Location).Distinct(StringComparer.Ordinal).ToList();
        text.Append(". Where: ").AppendJoin(", ", locations);
        text.Append(". Write a new JSON object that follows every rule in the instructions.");
        return text.ToString();
    }

    private static int ToInt(long? value) => value is { } v ? (int)Math.Clamp(v, 0, int.MaxValue) : 0;
}

[JsonSerializable(typeof(JsonNode))]
internal sealed partial class JsonNodeContext : JsonSerializerContext;
