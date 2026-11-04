using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>
/// A deterministic <see cref="IChatClient"/> for CI evals and pipeline tests: it answers from a script of steps (reply, hang until
/// cancelled, throw) and records every request (messages and options) so tests can assert what reached the "model".
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly ConcurrentQueue<Func<CancellationToken, Task<ChatResponse>>> _steps = new();

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

    /// <summary>Completed when a request starts (lets a test advance a fake clock while a step hangs).</summary>
    public TaskCompletionSource RequestStarted { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount => Requests.Count;

    public ScriptedChatClient Reply(string text, int inputTokens = 1000, int outputTokens = 300, string modelId = "scripted-model")
    {
        _steps.Enqueue(_ => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = modelId,
            Usage = new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens },
        }));
        return this;
    }

    public ScriptedChatClient Hang()
    {
        _steps.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Unreachable.");
        });
        return this;
    }

    public ScriptedChatClient Throw(Exception exception)
    {
        _steps.Enqueue(_ => Task.FromException<ChatResponse>(exception));
        return this;
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        lock (Requests)
        {
            Requests.Add(([.. messages], options?.Clone()));
        }

        var started = RequestStarted;
        RequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        started.TrySetResult();

        if (!_steps.TryDequeue(out var step))
        {
            throw new InvalidOperationException("The scripted chat client has no step left.");
        }

        return await step(cancellationToken);
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

    /// <summary>Everything the model received in one request, as one string (instructions included).</summary>
    public static string AllText((IReadOnlyList<ChatMessage> Messages, ChatOptions? Options) request) =>
        (request.Options?.Instructions ?? string.Empty) + "\n" + string.Join("\n", request.Messages.Select(m => m.Text));
}
