using Microsoft.Extensions.AI;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>
/// Test-only middleware that keeps the raw text of every model response, so the live eval can show each attempt's draft and the
/// validator's verdict on it. Production code never records or logs response text (ADR 0026 §3, ADR 0027 §1).
/// </summary>
internal sealed class RecordingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    private readonly List<string> _responses = [];

    /// <summary>The responses recorded since the last call, oldest first; clears the record.</summary>
    public IReadOnlyList<string> Take()
    {
        lock (_responses)
        {
            var taken = _responses.ToList();
            _responses.Clear();
            return taken;
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        lock (_responses)
        {
            _responses.Add(response.Text);
        }

        return response;
    }
}
