using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using RunnersDiary.Domain.Goals.Planning;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Domain.Insights.Narrative.Languages;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>
/// A deterministic <see cref="IChatClient"/> for local development and tests (provider <c>Fake</c>, never in production, ADR 0027 §2).
/// It reads the fact sheet from the user message and writes a valid draft from fixed rules, so the UI can be exercised without a cloud.
/// A request for the <c>training_plan</c> schema (or, without options, a message with the plan fact sheet header) gets a plan from
/// <see cref="RuleBasedFakePlanWriter"/> instead (ADR 0040 §7).
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
        var json = IsPlanRequest(options, input)
            ? TrainingPlanDraftJson.ToJson(RuleBasedFakePlanWriter.Write(ExtractFactSheet(input)))
            : SummaryDraftJson.ToJson(Write(FactSheetJson.Parse(ExtractFactSheet(input))));
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

    /// <summary>
    /// The draft the fake writes, in the sheet's language (<c>en</c> or <c>pl</c>, ADR 0043 §5); it always passes <see cref="SummaryValidator"/>
    /// with the policy of that language. Any other language gets the English texts.
    /// </summary>
    public static SummaryDraft Write(FactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var t = sheet.Language == NarrativeLanguages.Polish ? FakeSummaryTexts.Polish : FakeSummaryTexts.English;
        var partial = sheet.Flag("period.isPartial") == true && sheet.Contains("period.asOf");
        var headline = partial ? t.HeadlinePartial : t.Headline;

        var wentWell = new List<SummaryItem> { new(t.Totals) };
        if (sheet.Number("pace.slope") is < 0)
        {
            wentWell.Add(new SummaryItem(t.PaceFaster));
        }

        var watchOuts = new List<SummaryItem>();
        if (sheet.Text("acwr.peak.band") == "high")
        {
            watchOuts.Add(new SummaryItem(sheet.Flag("acwr.peak.isRaceWeek") == true ? t.PeakRace : t.PeakHigh));
        }

        var dataNotes = new List<SummaryItem>();
        if (sheet.Number("dq.estimatedLoadShare") >= SummaryValidationPolicy.Default.EstimatedLoadThreshold)
        {
            dataNotes.Add(new SummaryItem(t.EstimatedLoad));
        }

        if (sheet.Flag("acwr.asOf.unavailable") == true)
        {
            dataNotes.Add(new SummaryItem(t.RatioUnavailable));
        }

        return new SummaryDraft(headline, wentWell, watchOuts, new SummaryItem(t.Suggestion), dataNotes);
    }

    private static bool IsPlanRequest(ChatOptions? options, string input) =>
        options?.ResponseFormat is ChatResponseFormatJson format
            ? format.SchemaName == TrainingPlanAgent.SchemaName
            : input.Contains(TrainingPlanAgent.FactSheetHeader, StringComparison.Ordinal);

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

/// <summary>The fixed texts of <see cref="RuleBasedFakeChatClient.Write"/> per language.</summary>
internal sealed record FakeSummaryTexts(
    string Headline,
    string HeadlinePartial,
    string Totals,
    string PaceFaster,
    string PeakRace,
    string PeakHigh,
    string EstimatedLoad,
    string RatioUnavailable,
    string Suggestion)
{
    public static FakeSummaryTexts English { get; } = new(
        "Your training from {period.from} to {period.to}.",
        "Your training so far, from {period.from} up to {period.asOf}.",
        "You completed {totals.runCount} runs covering {totals.distance} in {totals.duration}.",
        "Your easy pace trended faster ({pace.slope}).",
        "Your highest load ratio, {acwr.peak.value} on {acwr.peak.date}, came from your {acwr.peak.raceType}.",
        "Your load ratio peaked at {acwr.peak.value} on {acwr.peak.date}, in the {acwr.peak.band} band.",
        "{dq.estimatedLoadShare} of the load uses estimated effort ratings.",
        "The load ratio needs {thresholds.acwrMinHistoryDays} of training history.",
        "Keep easy days easy and build load gradually.");

    /// <summary>Gender-neutral, placeholders only in nominative slots (after a colon or in parentheses), no counted noun after a count (prompt v3).</summary>
    public static FakeSummaryTexts Polish { get; } = new(
        "Twoje treningi w okresie od {period.from} do {period.to}.",
        "Twoje treningi do tej pory: od {period.from} do {period.asOf}.",
        "Liczba wykonanych treningów: {totals.runCount}, dystans: {totals.distance}, czas: {totals.duration}.",
        "Tempo spokojnych biegów przyspieszało ({pace.slope}).",
        "Najwyższy wskaźnik obciążenia wyniósł {acwr.peak.value} ({acwr.peak.date}) i wynika ze startu ({acwr.peak.raceType}).",
        "Wskaźnik obciążenia osiągnął {acwr.peak.value} w dniu {acwr.peak.date} (przedział: {acwr.peak.band}).",
        "Część obciążenia opiera się na szacowanym odczuwalnym wysiłku: {dq.estimatedLoadShare}.",
        "Do obliczenia wskaźnika obciążenia potrzeba {thresholds.acwrMinHistoryDays} historii treningów.",
        "Utrzymuj spokojne dni naprawdę spokojnymi i zwiększaj obciążenie stopniowo.");
}
