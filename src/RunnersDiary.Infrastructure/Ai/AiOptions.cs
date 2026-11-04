using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace RunnersDiary.Infrastructure.Ai;

public enum AiProvider
{
    /// <summary>No model: generation answers 503 <c>feature-unavailable</c>; cached, demo and template summaries are still served.</summary>
    None,

    /// <summary>A Microsoft Foundry deployment over the OpenAI v1 endpoint, keyless (ADR 0027 §1–2).</summary>
    Foundry,

    /// <summary>The deterministic rule-based fake; Development and Testing only.</summary>
    Fake,
}

/// <summary>
/// The <c>Ai</c> configuration section (ADR 0026–0028). Non-secret by design: there is no API key setting; Foundry is reached with
/// a managed identity in Azure or the developer's <c>az login</c> locally.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>The scope of a Foundry v1 endpoint token (ADR 0027, verified on Microsoft Learn).</summary>
    public const string FoundryTokenScope = "https://ai.azure.com/.default";

    public AiProvider Provider { get; set; } = AiProvider.None;

    /// <summary>Kill switch: false disables generation at runtime (503) while reads keep working.</summary>
    public bool Enabled { get; set; } = true;

    public FoundryOptions Foundry { get; set; } = new();

    /// <summary>Output cap per attempt, reasoning tokens included (<c>max_completion_tokens</c>).</summary>
    public int MaxOutputTokens { get; set; } = 1500;

    /// <summary><c>low</c>, <c>medium</c> or <c>high</c>; <c>none</c> is not documented for the GPT-5.x deployments (ADR 0027).</summary>
    public string ReasoningEffort { get; set; } = "low";

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>One retry on invalid output (ADR 0026 §3); 1 disables the retry. Never more than 2.</summary>
    public int MaxAttempts { get; set; } = 2;

    /// <summary>Concurrent generations per replica; a further request gets 503 <c>ai-busy</c>.</summary>
    public int MaxConcurrentGenerations { get; set; } = 2;

    /// <summary>A guard on the fact sheet sent to the model (about 6k tokens); exceeding it is a bug, answered 502.</summary>
    public int MaxFactSheetChars { get; set; } = 24_000;

    /// <summary>Directory of the prompt manifest, templates and demo fixtures; relative paths resolve against the app base directory.</summary>
    public string PromptsPath { get; set; } = "prompts/training-insights";

    public QuotaOptions Quotas { get; set; } = new();

    /// <summary>True when generation can run: a provider is configured and the kill switch is on.</summary>
    public bool GenerationAvailable => Enabled && Provider != AiProvider.None;

    public sealed class FoundryOptions
    {
        /// <summary>The resource endpoint, e.g. <c>https://&lt;resource&gt;.openai.azure.com/</c>; <c>openai/v1/</c> is appended.</summary>
        public string? Endpoint { get; set; }

        /// <summary>The deployment name; it is part of a cached summary's validity tuple (ADR 0028 §2).</summary>
        public string? Deployment { get; set; }

        /// <summary>The client id of the API's user-assigned identity (Azure, M7). Unset: <c>DefaultAzureCredential</c> without interactive sources.</summary>
        public string? ManagedIdentityClientId { get; set; }
    }

    public sealed class QuotaOptions
    {
        public int UserPer24h { get; set; } = 10;

        public int GuestPerAccount { get; set; } = 3;

        public int GlobalPer24h { get; set; } = 200;

        public int GuestGlobalPer24h { get; set; } = 60;
    }
}

/// <summary>Startup validation of <see cref="AiOptions"/>: a wrong setting fails the host instead of the first generation.</summary>
public sealed class AiOptionsValidator(IHostEnvironment environment) : IValidateOptions<AiOptions>
{
    public const string TestingEnvironment = "Testing";

    private static readonly string[] Efforts = ["low", "medium", "high"];

    public ValidateOptionsResult Validate(string? name, AiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.Provider == AiProvider.Fake && !(environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironment)))
        {
            failures.Add($"Ai:Provider 'Fake' is allowed only in Development and {TestingEnvironment}, not in '{environment.EnvironmentName}'.");
        }

        if (options.Provider == AiProvider.Foundry)
        {
            if (!Uri.TryCreate(options.Foundry.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add("Ai:Foundry:Endpoint must be an absolute https URL.");
            }

            if (string.IsNullOrWhiteSpace(options.Foundry.Deployment) || options.Foundry.Deployment.Length > 64)
            {
                failures.Add("Ai:Foundry:Deployment is required (at most 64 characters).");
            }
        }

        if (!Efforts.Contains(options.ReasoningEffort, StringComparer.Ordinal))
        {
            failures.Add("Ai:ReasoningEffort must be low, medium or high.");
        }

        if (options.MaxOutputTokens is < 256 or > 16_000)
        {
            failures.Add("Ai:MaxOutputTokens must be between 256 and 16000.");
        }

        if (options.MaxAttempts is < 1 or > 2)
        {
            failures.Add("Ai:MaxAttempts must be 1 or 2.");
        }

        if (options.AttemptTimeout <= TimeSpan.Zero || options.AttemptTimeout > TimeSpan.FromMinutes(2))
        {
            failures.Add("Ai:AttemptTimeout must be positive and at most 2 minutes.");
        }

        if (options.MaxConcurrentGenerations < 0 || options.MaxFactSheetChars < 1_000)
        {
            failures.Add("Ai:MaxConcurrentGenerations must be >= 0 and Ai:MaxFactSheetChars >= 1000.");
        }

        var q = options.Quotas;
        if (q.UserPer24h < 0 || q.GuestPerAccount < 0 || q.GlobalPer24h < 0 || q.GuestGlobalPer24h < 0)
        {
            failures.Add("Ai:Quotas values must not be negative.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
