using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using RunnersDiary.Infrastructure.Ai;

namespace RunnersDiary.Api.Tests.Infrastructure;

/// <summary>
/// A scripted <see cref="IChatClient"/> for API tests: answers from a queue of steps and records every request. Never touches a network.
/// <see cref="ReplyValid"/> delegates to the rule-based fake, which writes a draft that passes the validator for the request's fact sheet.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<ChatResponse>>> _steps = new();
    private readonly object _gate = new();

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return Requests.Count;
            }
        }
    }

    /// <summary>Completed when the next request starts.</summary>
    public TaskCompletionSource RequestStarted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ScriptedChatClient ReplyValid(int inputTokens = 1000, int outputTokens = 300, string modelId = "scripted-model")
    {
        Enqueue(async (messages, ct) =>
        {
            using var fake = new RuleBasedFakeChatClient();
            var response = await fake.GetResponseAsync(messages, null, ct);
            response.ModelId = modelId;
            response.Usage = new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens };
            return response;
        });
        return this;
    }

    public ScriptedChatClient ReplyText(string text)
    {
        Enqueue((_, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = "scripted-model",
            Usage = new UsageDetails { InputTokenCount = 500, OutputTokenCount = 50 },
        }));
        return this;
    }

    public ScriptedChatClient Hang()
    {
        Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Unreachable.");
        });
        return this;
    }

    public ScriptedChatClient Throw(Exception exception)
    {
        Enqueue((_, _) => Task.FromException<ChatResponse>(exception));
        return this;
    }

    /// <summary>Holds the next call until <paramref name="release"/> completes, then answers like <see cref="ReplyValid"/>.</summary>
    public ScriptedChatClient ReplyValidAfter(Task release)
    {
        Enqueue(async (messages, ct) =>
        {
            await release.WaitAsync(ct);
            using var fake = new RuleBasedFakeChatClient();
            var response = await fake.GetResponseAsync(messages, null, ct);
            response.Usage = new UsageDetails { InputTokenCount = 800, OutputTokenCount = 200 };
            return response;
        });
        return this;
    }

    private void Enqueue(Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<ChatResponse>> step)
    {
        lock (_gate)
        {
            _steps.Enqueue(step);
        }
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var list = messages.ToList();
        Func<IReadOnlyList<ChatMessage>, CancellationToken, Task<ChatResponse>> step;
        TaskCompletionSource started;
        lock (_gate)
        {
            Requests.Add((list, options?.Clone()));
            started = RequestStarted;
            RequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            step = _steps.Count > 0 ? _steps.Dequeue() : throw new InvalidOperationException("The scripted chat client has no step left.");
        }

        started.TrySetResult();
        return await step(list, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType?.IsInstanceOfType(this) == true ? this : null;

    public void Dispose()
    {
    }

    /// <summary>Everything the model received in one request (instructions, messages, schema options) as one string.</summary>
    public static string AllText((IReadOnlyList<ChatMessage> Messages, ChatOptions? Options) request) =>
        (request.Options?.Instructions ?? string.Empty) + "\n" + string.Join("\n", request.Messages.Select(m => m.Text));
}

/// <summary>Builds API hosts for the AI summary tests on top of the shared containers.</summary>
internal static class AiTestHost
{
    public const string Deployment = "gpt-test";

    /// <summary>
    /// A host whose model is <paramref name="client"/> (provider <c>Foundry</c> with a dummy https endpoint, the real client replaced
    /// by the scripted one: no network call is ever made), or no model at all when <paramref name="client"/> is null. The burst limiter
    /// is raised so quota tests see the quota; <paramref name="settings"/> override anything.
    /// </summary>
    public static WebApplicationFactory<Program> Create(
        ContainersApiFactory factory,
        ScriptedChatClient? client,
        IReadOnlyDictionary<string, string>? settings = null,
        Action<IServiceCollection>? services = null,
        string deployment = Deployment)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:AiSummary:UserPermitLimit", "1000");
            builder.UseSetting("RateLimiting:AiSummary:GuestPermitLimit", "1000");
            if (client is not null)
            {
                builder.UseSetting("Ai:Provider", "Foundry");
                builder.UseSetting("Ai:Foundry:Endpoint", "https://example.invalid/");
                builder.UseSetting("Ai:Foundry:Deployment", deployment);
            }

            foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(s =>
            {
                if (client is not null)
                {
                    s.RemoveAll<AiChatClientProvider>();
                    s.AddSingleton(new AiChatClientProvider(ChatClientFactory.Wrap(client, NullLoggerFactory.Instance), deployment));
                }

                services?.Invoke(s);
            });
        });
    }
}
