using System.ClientModel.Primitives;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAIChatClient = OpenAI.Chat.ChatClient;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>The configured <see cref="IChatClient"/> pipeline, or none (<see cref="AiProvider.None"/>). A singleton; owns the client.</summary>
public sealed class AiChatClientProvider(IChatClient? client, string? deployment) : IDisposable
{
    public IChatClient? Client { get; } = client;

    /// <summary>The configured deployment ("fake" for the fake): part of a cached summary's validity tuple and of every ledger row.</summary>
    public string? Deployment { get; } = deployment;

    public void Dispose() => Client?.Dispose();
}

/// <summary>
/// Builds the client stack of ADR 0027 §1: OpenAI SDK <c>ChatClient</c> over the Foundry v1 endpoint with a keyless
/// <c>BearerTokenPolicy</c> → <c>AsIChatClient()</c> → <c>UseOpenTelemetry</c> (sensitive data off) → <c>UseLogging</c> (never at Trace,
/// where message content would be logged). Chat Completions, not Responses: no hosted tools and no server-side state.
/// </summary>
public static class ChatClientFactory
{
    /// <summary>The ActivitySource and Meter name of the pipeline (<c>gen_ai.client.token.usage</c> etc.); exported by ServiceDefaults.</summary>
    public const string TelemetrySourceName = "RunnersDiary.Ai";

    public const string FakeDeployment = "fake";

    public static AiChatClientProvider Create(AiOptions options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Provider switch
        {
            AiProvider.Foundry => new AiChatClientProvider(Wrap(CreateFoundryClient(options), loggerFactory), options.Foundry.Deployment),
            AiProvider.Fake => new AiChatClientProvider(Wrap(new RuleBasedFakeChatClient(), loggerFactory), FakeDeployment),
            _ => new AiChatClientProvider(null, null),
        };
    }

    /// <summary>The shared middleware around any inner client (also used by tests around a scripted client).</summary>
    public static IChatClient Wrap(IChatClient inner, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        return inner.AsBuilder()
            .UseOpenTelemetry(loggerFactory, TelemetrySourceName, client => client.EnableSensitiveData = false)
            .UseLogging(new NoTraceLoggerFactory(loggerFactory))
            .Build();
    }

    /// <summary>Managed identity in Azure (user-assigned client id); otherwise the developer's credentials without interactive sources. No key exists.</summary>
    public static TokenCredential CreateCredential(AiOptions.FoundryOptions foundry)
    {
        ArgumentNullException.ThrowIfNull(foundry);
        return string.IsNullOrWhiteSpace(foundry.ManagedIdentityClientId)
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true })
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(foundry.ManagedIdentityClientId));
    }

    /// <summary>The Foundry v1 endpoint: <c>https://&lt;resource&gt;.openai.azure.com/openai/v1/</c>.</summary>
    public static Uri V1Endpoint(string endpoint)
    {
        var baseUri = new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/", UriKind.Absolute);
        return new Uri(baseUri, "openai/v1/");
    }

    /// <param name="credential">The token source; the default is <see cref="CreateCredential"/>. Tests pass a fake.</param>
    /// <param name="transport">The HTTP transport; the default is the SDK's. Tests pass a capturing transport (no network).</param>
    internal static IChatClient CreateFoundryClient(AiOptions options, TokenCredential? credential = null, PipelineTransport? transport = null)
    {
        var foundry = options.Foundry;
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = V1Endpoint(foundry.Endpoint!),
            // Transport retry at most once; the pipeline's own retry is for invalid output only (ADR 0027 §1).
            RetryPolicy = new ClientRetryPolicy(maxRetries: 1),
            NetworkTimeout = options.AttemptTimeout,
        };
        if (transport is not null)
        {
            clientOptions.Transport = transport;
        }

#pragma warning disable OPENAI001 // Keyless BearerTokenPolicy constructor of the v1 endpoint, the documented Foundry path (ADR 0027).
        var client = new OpenAIChatClient(
            model: foundry.Deployment!,
            authenticationPolicy: new BearerTokenPolicy(credential ?? CreateCredential(foundry), AiOptions.FoundryTokenScope),
            options: clientOptions);
#pragma warning restore OPENAI001
        return client.AsIChatClient();
    }

    /// <summary>
    /// <c>LoggingChatClient</c> logs messages, options and responses only at <see cref="LogLevel.Trace"/>; this factory never enables
    /// Trace, so no prompt or response text is ever logged, whatever the host's log configuration.
    /// </summary>
    private sealed class NoTraceLoggerFactory(ILoggerFactory inner) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new NoTraceLogger(inner.CreateLogger(categoryName));

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose()
        {
            // The inner factory belongs to the host.
        }

        private sealed class NoTraceLogger(ILogger inner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel) => logLevel > LogLevel.Trace && inner.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    inner.Log(logLevel, eventId, state, exception, formatter);
                }
            }
        }
    }
}
