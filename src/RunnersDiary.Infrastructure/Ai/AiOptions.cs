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

    /// <summary>Training plan generation (M9, ADR 0040 §4, §6; plan §4.7).</summary>
    public PlanOptions Plan { get; set; } = new();

    /// <summary>True when generation can run: a provider is configured and the kill switch is on.</summary>
    public bool GenerationAvailable => Enabled && Provider != AiProvider.None;

    /// <summary>True when plan generation can run: <see cref="GenerationAvailable"/> and the plan-only switch <c>Ai:Plan:Enabled</c>.</summary>
    public bool PlanGenerationAvailable => GenerationAvailable && Plan.Enabled;

    /// <summary>
    /// The network timeout of the shared client: the longest per-attempt timeout of any feature (ADR 0040 §4), so a plan attempt is not cut at
    /// the summary's 30 s. Each feature still enforces its own attempt timeout with a cancellation token.
    /// </summary>
    public TimeSpan ClientNetworkTimeout => AttemptTimeout >= Plan.AttemptTimeout ? AttemptTimeout : Plan.AttemptTimeout;

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

    /// <summary>The <c>Ai:Plan</c> section. Quotas are per feature; the global budgets of <see cref="QuotaOptions"/> are shared, with a plan attempt weighted.</summary>
    public sealed class PlanOptions
    {
        /// <summary>Plan-only kill switch on top of <see cref="Enabled"/> (Bicep keeps it false until gate G-P4).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Output cap per attempt, reasoning tokens included; a 16-week plan needs about 5-6k tokens.</summary>
        public int MaxOutputTokens { get; set; } = 8_000;

        public string ReasoningEffort { get; set; } = "low";

        public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(90);

        /// <summary>The whole generation (both attempts); at most 200 s, the per-route proxy timeout of the web tier.</summary>
        public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(190);

        /// <summary>1 or 2; the second attempt runs only if at least 30 s of <see cref="TotalTimeout"/> remain.</summary>
        public int MaxAttempts { get; set; } = 2;

        public int MaxFactSheetChars { get; set; } = 24_000;

        /// <summary>Directory of the plan prompt manifest and prompts; relative paths resolve against the app base directory.</summary>
        public string PromptsPath { get; set; } = "prompts/training-plan";

        public PlanQuotaOptions Quotas { get; set; } = new();

        /// <summary>What one plan attempt counts in the shared global budgets (a plan attempt costs about three summary attempts).</summary>
        public int GlobalAttemptWeight { get; set; } = 3;
    }

    public sealed class PlanQuotaOptions
    {
        public int UserPer24h { get; set; } = 5;

        public int GuestPerAccount { get; set; } = 2;
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

        ValidatePlan(options.Plan, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>The ceiling of <c>Ai:Plan:TotalTimeout</c>: the web tier's per-route proxy timeout for the plan POST (ADR 0040 §6).</summary>
    public static readonly TimeSpan MaxPlanTotalTimeout = TimeSpan.FromSeconds(200);

    private static void ValidatePlan(AiOptions.PlanOptions plan, List<string> failures)
    {
        if (!Efforts.Contains(plan.ReasoningEffort, StringComparer.Ordinal))
        {
            failures.Add("Ai:Plan:ReasoningEffort must be low, medium or high.");
        }

        if (plan.MaxOutputTokens is < 256 or > 16_000)
        {
            failures.Add("Ai:Plan:MaxOutputTokens must be between 256 and 16000.");
        }

        if (plan.MaxAttempts is < 1 or > 2)
        {
            failures.Add("Ai:Plan:MaxAttempts must be 1 or 2.");
        }

        if (plan.AttemptTimeout <= TimeSpan.Zero || plan.AttemptTimeout > TimeSpan.FromMinutes(2))
        {
            failures.Add("Ai:Plan:AttemptTimeout must be positive and at most 2 minutes.");
        }

        if (plan.TotalTimeout < plan.AttemptTimeout || plan.TotalTimeout > MaxPlanTotalTimeout)
        {
            failures.Add("Ai:Plan:TotalTimeout must be at least Ai:Plan:AttemptTimeout and at most 200 seconds.");
        }

        if (plan.MaxFactSheetChars < 1_000)
        {
            failures.Add("Ai:Plan:MaxFactSheetChars must be >= 1000.");
        }

        if (string.IsNullOrWhiteSpace(plan.PromptsPath))
        {
            failures.Add("Ai:Plan:PromptsPath is required.");
        }

        if (plan.Quotas.UserPer24h < 0 || plan.Quotas.GuestPerAccount < 0)
        {
            failures.Add("Ai:Plan:Quotas values must not be negative.");
        }

        if (plan.GlobalAttemptWeight is < 1 or > 10)
        {
            failures.Add("Ai:Plan:GlobalAttemptWeight must be between 1 and 10.");
        }
    }
}
