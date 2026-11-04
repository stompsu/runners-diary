using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using RunnersDiary.Domain.Insights.Narrative;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>
/// A deterministic <see cref="IChatClient"/> for local development and tests (provider <c>Fake</c>, never in production, ADR 0027 §2).
/// It reads the fact sheet from the user message and writes a valid draft from fixed rules, so the UI can be exercised without a cloud.
/// </summary>
public sealed class RuleBasedFakeChatClient : IChatClient
{
    public const string ModelId = "fake-rule-based";

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var list = messages.ToList();
        var input = string.Concat(list.Select(m => m.Text));
        var sheet = FactSheetJson.Parse(ExtractFactSheet(input));
        var json = SummaryDraftJson.ToJson(Write(sheet));
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
        {
            ModelId = ModelId,
            Usage = new UsageDetails { InputTokenCount = (input.Length + (options?.Instructions?.Length ?? 0)) / 4, OutputTokenCount = json.Length / 4 },
        };
        return Task.FromResult(response);
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
        // Nothing to release.
    }

    /// <summary>The draft the fake writes; it always passes <see cref="SummaryValidator"/> with the default policy.</summary>
    public static SummaryDraft Write(FactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var partial = sheet.Flag("period.isPartial") == true && sheet.Contains("period.asOf");
        var headline = partial
            ? "Your training so far, from {period.from} up to {period.asOf}."
            : "Your training from {period.from} to {period.to}.";

        var wentWell = new List<SummaryItem> { new("You completed {totals.runCount} runs covering {totals.distance} in {totals.duration}.") };
        if (sheet.Number("pace.slope") is < 0)
        {
            wentWell.Add(new SummaryItem("Your easy pace trended faster ({pace.slope})."));
        }

        var watchOuts = new List<SummaryItem>();
        if (sheet.Text("acwr.peak.band") == "high")
        {
            watchOuts.Add(sheet.Flag("acwr.peak.isRaceWeek") == true
                ? new SummaryItem("Your highest load ratio, {acwr.peak.value} on {acwr.peak.date}, came from your {acwr.peak.raceType}.")
                : new SummaryItem("Your load ratio peaked at {acwr.peak.value} on {acwr.peak.date}, in the {acwr.peak.band} band."));
        }

        var dataNotes = new List<SummaryItem>();
        if (sheet.Number("dq.estimatedLoadShare") >= SummaryValidationPolicy.Default.EstimatedLoadThreshold)
        {
            dataNotes.Add(new SummaryItem("{dq.estimatedLoadShare} of the load uses estimated effort ratings."));
        }

        if (sheet.Flag("acwr.asOf.unavailable") == true)
        {
            dataNotes.Add(new SummaryItem("The load ratio needs {thresholds.acwrMinHistoryDays} of training history."));
        }

        return new SummaryDraft(headline, wentWell, watchOuts, new SummaryItem("Keep easy days easy and build load gradually."), dataNotes);
    }

    private static string ExtractFactSheet(string text)
    {
        const string open = "```json";
        var start = text.IndexOf(open, StringComparison.Ordinal);
        var end = start < 0 ? -1 : text.IndexOf("```", start + open.Length, StringComparison.Ordinal);
        return start < 0 || end < 0
            ? throw new InvalidOperationException("The fake client found no fact sheet in the request.")
            : text[(start + open.Length)..end].Trim();
    }
}
