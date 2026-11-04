using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RunnersDiary.Ai.Tests.Evals;
using RunnersDiary.Ai.Tests.Support;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Ai.Tests.Pipeline;

/// <summary>ADR 0027 §2: providers, startup validation, keyless credentials, and the logging/telemetry pipeline.</summary>
public sealed class AiOptionsAndFactoryTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static bool Valid(AiOptions options, string environment = "Production") =>
        new AiOptionsValidator(new Env(environment)).Validate(null, options).Succeeded;

    [Fact]
    public void Defaults_are_provider_none_and_valid()
    {
        var options = new AiOptions();
        Assert.Equal(AiProvider.None, options.Provider);
        Assert.False(options.GenerationAvailable);
        Assert.Equal(1500, options.MaxOutputTokens);
        Assert.Equal("low", options.ReasoningEffort);
        Assert.Equal(TimeSpan.FromSeconds(30), options.AttemptTimeout);
        Assert.Equal(2, options.MaxAttempts);
        Assert.Equal(2, options.MaxConcurrentGenerations);
        Assert.Equal(24_000, options.MaxFactSheetChars);
        Assert.Equal((10, 3, 200, 60), (options.Quotas.UserPer24h, options.Quotas.GuestPerAccount, options.Quotas.GlobalPer24h, options.Quotas.GuestGlobalPer24h));
        Assert.True(Valid(options));
        Assert.Null(ChatClientFactory.Create(options, NullLoggerFactory.Instance).Client);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void Fake_is_allowed_only_in_development_and_testing(string environment, bool allowed) =>
        Assert.Equal(allowed, Valid(new AiOptions { Provider = AiProvider.Fake }, environment));

    [Fact]
    public void Foundry_needs_an_https_endpoint_and_a_deployment()
    {
        Assert.False(Valid(new AiOptions { Provider = AiProvider.Foundry }));
        Assert.False(Valid(new AiOptions { Provider = AiProvider.Foundry, Foundry = { Endpoint = "http://x.openai.azure.com/", Deployment = "d" } }));
        Assert.False(Valid(new AiOptions { Provider = AiProvider.Foundry, Foundry = { Endpoint = "https://x.openai.azure.com/" } }));
        Assert.True(Valid(new AiOptions { Provider = AiProvider.Foundry, Foundry = { Endpoint = "https://x.openai.azure.com/", Deployment = "gpt-5.4-mini" } }));
    }

    [Theory]
    [InlineData(nameof(AiOptions.ReasoningEffort))]
    [InlineData(nameof(AiOptions.MaxAttempts))]
    [InlineData(nameof(AiOptions.AttemptTimeout))]
    [InlineData(nameof(AiOptions.MaxOutputTokens))]
    public void Out_of_range_settings_fail_validation(string setting)
    {
        var options = new AiOptions();
        switch (setting)
        {
            case nameof(AiOptions.ReasoningEffort): options.ReasoningEffort = "none"; break;
            case nameof(AiOptions.MaxAttempts): options.MaxAttempts = 3; break;
            case nameof(AiOptions.AttemptTimeout): options.AttemptTimeout = TimeSpan.Zero; break;
            default: options.MaxOutputTokens = 100; break;
        }

        Assert.False(Valid(options));
    }

    [Fact]
    public void There_is_no_api_key_setting()
    {
        var names = typeof(AiOptions).GetProperties().Concat(typeof(AiOptions.FoundryOptions).GetProperties()).Select(p => p.Name);
        Assert.DoesNotContain(names, n => n.Contains("Key", StringComparison.OrdinalIgnoreCase) || n.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Foundry_uses_the_v1_endpoint_and_keyless_credentials_without_network()
    {
        Assert.Equal(new Uri("https://res.openai.azure.com/openai/v1/"), ChatClientFactory.V1Endpoint("https://res.openai.azure.com"));
        Assert.Equal(new Uri("https://res.openai.azure.com/openai/v1/"), ChatClientFactory.V1Endpoint("https://res.openai.azure.com/"));
        Assert.IsType<DefaultAzureCredential>(ChatClientFactory.CreateCredential(new AiOptions.FoundryOptions()));
        Assert.IsType<ManagedIdentityCredential>(ChatClientFactory.CreateCredential(new AiOptions.FoundryOptions { ManagedIdentityClientId = "00000000-0000-0000-0000-000000000001" }));

        var options = new AiOptions { Provider = AiProvider.Foundry, Foundry = { Endpoint = "https://res.openai.azure.com/", Deployment = "gpt-5.4-mini" } };
        using var provider = ChatClientFactory.Create(options, NullLoggerFactory.Instance);
        var client = Assert.IsAssignableFrom<IChatClient>(provider.Client);
        Assert.Equal("gpt-5.4-mini", provider.Deployment);
        Assert.NotNull(client.GetService<OpenTelemetryChatClient>());
        Assert.NotNull(client.GetService<LoggingChatClient>());
        Assert.Equal("gpt-5.4-mini", client.GetService<ChatClientMetadata>()?.DefaultModelId);
    }

    /// <summary>L7: the provider-reported model id is untrusted for length; it is cut to the column before it is stored.</summary>
    [Fact]
    public void A_long_model_id_is_truncated_to_the_column_length()
    {
        Assert.Null(EfAiSummaryStore.TruncateModelId(null));
        Assert.Equal("gpt-5.4-mini", EfAiSummaryStore.TruncateModelId("gpt-5.4-mini"));
        Assert.Equal(new string('m', 100), EfAiSummaryStore.TruncateModelId(new string('m', 100)));
        Assert.Equal(new string('m', 100), EfAiSummaryStore.TruncateModelId(new string('m', 250)));
        Assert.Equal(100, EfAiSummaryStore.MaxModelIdLength);
    }

    [Fact]
    public void Fake_provider_uses_the_rule_based_client_in_the_same_pipeline()
    {
        using var provider = ChatClientFactory.Create(new AiOptions { Provider = AiProvider.Fake }, NullLoggerFactory.Instance);
        Assert.Equal(ChatClientFactory.FakeDeployment, provider.Deployment);
        var client = Assert.IsAssignableFrom<IChatClient>(provider.Client);
        Assert.NotNull(client.GetService<RuleBasedFakeChatClient>());
        Assert.NotNull(client.GetService<OpenTelemetryChatClient>());
    }

    [Fact]
    public async Task No_prompt_or_response_text_is_logged_even_at_trace_level()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        var scripted = new ScriptedChatClient().Reply(EvalFiles.GoodDraft(EvalCases.LoadSpike));
        var client = ChatClientFactory.Wrap(scripted, factory);
        var sheet = EvalCases.Get(EvalCases.LoadSpike).Sheet;
        var prompt = PromptCatalog.Load(RepoPaths.PromptsInOutput).Current;
        var agent = new TrainingInsightsAgent(client, prompt, new AiOptions(), factory);

        await agent.RunAsync(Domain.Insights.Narrative.FactSheetJson.ToJson(sheet), "en", null, TestContext.Current.CancellationToken);

        Assert.NotEmpty(logs.Messages);
        Assert.DoesNotContain(logs.Messages, m => m.Contains("totals.distance", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, m => m.Contains("sharp jump", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, m => m.Contains("supportive", StringComparison.Ordinal));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Messages)
                {
                    owner.Messages.Add(formatter(state, exception) + " " + state);
                }
            }
        }
    }
}
