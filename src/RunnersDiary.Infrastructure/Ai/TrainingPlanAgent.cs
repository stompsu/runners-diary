using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights.Narrative.Languages;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>
/// The "TrainingPlan" agent (ADR 0040 §4): a second Microsoft Agent Framework <see cref="ChatClientAgent"/> on the same client as the summary
/// agent, with the versioned plan prompt as instructions, no tools and no memory. Each run sends one user message (the canonical plan fact sheet
/// in a fenced JSON block and the language) and asks for structured output with the strict <c>training_plan</c> schema, the plan token cap and
/// low reasoning effort. On a retry, a second user message lists only the violated rule codes and the affected week starts, dates or fixed
/// paths, never the rejected text.
/// </summary>
public sealed class TrainingPlanAgent
{
    public const string AgentName = "TrainingPlan";
    public const string SchemaName = "training_plan";

    /// <summary>The header of the user message; the fake model also recognises a plan request by it.</summary>
    public const string FactSheetHeader = "Plan fact sheet:";

    /// <summary>The most locations listed in a retry message (a long list helps the model no more than a short one).</summary>
    public const int MaxRetryLocations = 40;

    /// <summary>
    /// The strict output schema, wire form, exactly as ADR 0040 §4: every property required, <c>additionalProperties: false</c>, 11 properties,
    /// 3 levels; only keywords Azure OpenAI structured outputs support (no <c>minimum</c>, <c>maxItems</c>, <c>pattern</c>, <c>format</c>;
    /// ranges are validator rules).
    /// </summary>
    public const string ResponseSchemaJson = """
        {"type":"object","additionalProperties":false,"required":["notes","sessions"],
         "properties":{
          "notes":{"type":"string"},
          "sessions":{"type":"array","items":{
            "type":"object","additionalProperties":false,
            "required":["date","runType","intensity","phase","distanceMeters","durationSeconds","paceMinSecondsPerKm","paceMaxSecondsPerKm","description"],
            "properties":{
              "date":{"type":"string"},
              "runType":{"type":"string","enum":["easy","recovery","long","tempo","intervals","fartlek","hillRepeats","progression","racePace","stridesDrills","trailTraining","treadmill","rest"]},
              "intensity":{"type":"string","enum":["recovery","easy","steady","tempo","threshold","interval","racePace","rest"]},
              "phase":{"type":"string","enum":["base","build","peak","taper"]},
              "distanceMeters":{"type":["number","null"]},
              "durationSeconds":{"type":["integer","null"]},
              "paceMinSecondsPerKm":{"type":["integer","null"]},
              "paceMaxSecondsPerKm":{"type":["integer","null"]},
              "description":{"type":"string"}}}}}}
        """;

    private readonly ChatClientAgent _agent;
    private readonly AiOptions.PlanOptions _options;

    public TrainingPlanAgent(IChatClient chatClient, PlanPromptVersion prompt, AiOptions.PlanOptions options, ILoggerFactory? loggerFactory = null)
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
                Description = "Suggests training sessions inside a computed envelope; read-only, no tools.",
                ChatOptions = new ChatOptions { Instructions = prompt.Instructions },
                UseProvidedChatClientAsIs = true,
            },
            loggerFactory);
    }

    public PlanPromptVersion Prompt { get; }

    /// <summary>The strict JSON schema of the plan answer sent as the response format.</summary>
    public static ChatResponseFormatJson ResponseFormat { get; } = ChatResponseFormat.ForJsonSchema(
        ParseSchema(),
        SchemaName,
        "Suggested training sessions inside the plan envelope, and short notes on the plan.");

    private static JsonElement ParseSchema()
    {
        using var document = JsonDocument.Parse(ResponseSchemaJson);
        return document.RootElement.Clone();
    }

    public async Task<AgentAttempt> RunAsync(string factSheetJson, string language, IReadOnlyList<PlanViolation>? rejected, CancellationToken cancellationToken)
    {
        var messages = BuildMessages(factSheetJson, language, rejected);
        var response = await _agent.RunAsync(messages, session: null, options: new ChatClientAgentRunOptions(CreateChatOptions(_options)), cancellationToken);
        var usage = response.Usage;
        var raw = response.RawRepresentation as ChatResponse;
        return new AgentAttempt(
            response.Text,
            ToInt(usage?.InputTokenCount),
            ToInt(usage?.OutputTokenCount),
            raw?.ModelId,
            raw?.FinishReason?.Value);
    }

    /// <summary>Run options per call: the plan output cap, reasoning effort, strict structured output. No temperature (reasoning models).</summary>
    public static ChatOptions CreateChatOptions(AiOptions.PlanOptions options)
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

    public static List<ChatMessage> BuildMessages(string factSheetJson, string language, IReadOnlyList<PlanViolation>? rejected)
    {
        ArgumentNullException.ThrowIfNull(factSheetJson);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, $"Language: {language}\n\n{FactSheetHeader}\n```json\n{factSheetJson}\n```"),
        };

        if (rejected is { Count: > 0 })
        {
            messages.Add(new ChatMessage(ChatRole.User, RetryMessage(rejected)));
        }

        return messages;
    }

    /// <summary>
    /// The retry note (ADR 0040 §5): rule codes and the affected week starts, dates or fixed paths only, never the model's text. Since the
    /// "plan reliability" amendment, violations that carry validator-computed numbers (<see cref="PlanViolation.Value"/> and
    /// <see cref="PlanViolation.Limit"/>) are also listed with them, so the next attempt can correct by the right amount.
    /// </summary>
    public static string RetryMessage(IReadOnlyList<PlanViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        var text = new StringBuilder("Your previous plan was rejected by the validator. Violated rules: ");
        text.AppendJoin(", ", violations.Select(v => v.Rule).Distinct().Order().Select(r => r.ToCode()));
        var locations = violations.Select(v => v.Location).Where(IsSafeLocation).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (locations.Count > 0)
        {
            text.Append(". Where (week starts, dates or fields): ").AppendJoin(", ", locations.Take(MaxRetryLocations));
        }

        var details = violations
            .Where(v => v.Value is not null && v.Limit is not null && IsSafeLocation(v.Location))
            .Select(Detail)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(MaxRetryLocations)
            .ToList();
        if (details.Count > 0)
        {
            text.Append(". Numbers to fix: ").AppendJoin("; ", details);
        }

        if (violations.Any(v => v.Rule == PlanRuleCode.GenderedForm))
        {
            text.Append(". ").Append(PolishGenderedForms.RetryHint);
        }

        text.Append(". Write a new JSON object that follows every rule in the instructions and stays inside the envelope.");
        return text.ToString();
    }

    /// <summary>One numeric detail of the retry note, built from the safe location and two validator-computed integers only.</summary>
    private static string? Detail(PlanViolation v)
    {
        var value = v.Value!.Value.ToString(CultureInfo.InvariantCulture);
        var limit = v.Limit!.Value.ToString(CultureInfo.InvariantCulture);
        return v.Rule switch
        {
            PlanRuleCode.WeeklyVolume =>
                $"week {v.Location}: your running sessions add up to {value} m, its availableDistanceMeters is {limit} m",
            PlanRuleCode.LongRun => $"{v.Location}: a {value} m session, that week's maxLongRunMeters is {limit} m",
            PlanRuleCode.Schema when v.Location.EndsWith(".paceMaxSecondsPerKm", StringComparison.Ordinal) =>
                $"{v.Location}: the pace range is {value} s/km wide, at most {limit} is allowed",
            _ => null,
        };
    }

    /// <summary>A location is a parsed date, a fixed path (<c>notes</c>, <c>sessions</c>, <c>draft</c>, <c>sessions[n].member</c>), never model text.</summary>
    internal static bool IsSafeLocation(string location) =>
        DateOnly.TryParseExact(location, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
        || location is "notes" or "sessions" or "draft"
        || System.Text.RegularExpressions.Regex.IsMatch(location, @"^sessions\[\d{1,4}\](\.(date|runType|intensity|phase|distanceMeters|durationSeconds|paceMinSecondsPerKm|paceMaxSecondsPerKm|description))?$");

    private static int ToInt(long? value) => value is { } v ? (int)Math.Clamp(v, 0, int.MaxValue) : 0;
}

/// <summary>The plan agent for the configured client and the current plan prompt, or none when no provider is configured. A singleton.</summary>
public sealed class TrainingPlanAgentProvider(
    AiChatClientProvider clients, PlanPromptCatalog prompts, IOptions<AiOptions> options, ILoggerFactory loggerFactory)
{
    private readonly Lazy<TrainingPlanAgent?> _agent = new(() =>
        clients.Client is { } client ? new TrainingPlanAgent(client, prompts.Current, options.Value.Plan, loggerFactory) : null);

    public TrainingPlanAgent? Agent => _agent.Value;

    public string? Deployment => clients.Deployment;
}
