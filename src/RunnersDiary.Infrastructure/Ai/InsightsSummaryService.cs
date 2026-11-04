using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Domain.Insights.Narrative;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Insights;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>The cache key of a summary (<c>UX_AiSummaries_Key</c> without the user, which the owner filter supplies).</summary>
public sealed record SummaryKey(DateOnly From, DateOnly To, ComparisonKind Compare, string Language);

/// <summary>
/// One stored model summary (<c>AiSummaries</c> row, plan §3.4) with its validity tuple: <see cref="Fingerprint"/>,
/// <see cref="MetricsVersion"/>, <see cref="FactSheetVersion"/>, <see cref="PromptVersion"/>, <see cref="ModelDeployment"/>.
/// </summary>
public sealed record StoredSummary(
    SummaryKey Key,
    int MetricsVersion,
    int FactSheetVersion,
    int PromptVersion,
    string ModelDeployment,
    string? ModelId,
    ReadOnlyMemory<byte> Fingerprint,
    string ContentJson,
    int InputTokens,
    int OutputTokens,
    DateTimeOffset CreatedAt);

/// <summary>
/// The current caller's stored summaries (ADR 0028 §1). Scoped and owner-filtered like every user-owned read (ADR 0010): an
/// implementation (EF Core, step 3) never takes a user id from its caller.
/// </summary>
public interface IAiSummaryStore
{
    Task<StoredSummary?> FindAsync(SummaryKey key, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces the row for <see cref="StoredSummary.Key"/>; on insert, enforces the per-account cap (users 1 000, guests 20) by deleting the oldest row by <c>CreatedAt</c>.</summary>
    Task UpsertAsync(StoredSummary summary, CancellationToken cancellationToken);
}

public enum SummaryState
{
    None,
    Fresh,
    Stale,
}

public enum SummarySource
{
    Model,
    Demo,
    Template,
}

public enum GenerationAvailability
{
    Available,
    NotConfigured,
    QuotaExhausted,

    /// <summary>A template period: nothing to generate.</summary>
    NotNeeded,
}

/// <summary>A summary as returned (contract <c>InsightsSummaryDto</c>): its source, metadata and resolved content.</summary>
public sealed record SummaryResult(
    SummarySource Source,
    DateTimeOffset? CreatedAt,
    int MetricsVersion,
    int FactSheetVersion,
    int? PromptVersion,
    string? Model,
    SummaryContent Content);

/// <summary>Contract <c>generation</c>: availability and the account quota (nulls when not configured or not needed).</summary>
public sealed record GenerationInfo(GenerationAvailability Availability, int? Used, int? Limit, DateTimeOffset? ResetsAt, QuotaWindow? Scope);

/// <summary>Contract <c>InsightsSummaryResponse</c> minus the echo of the request.</summary>
public sealed record SummaryView(SummaryKey Key, SummaryState State, SummaryResult? Summary, GenerationInfo Generation);

public enum GenerateStatus
{
    /// <summary>200 with <see cref="GenerateOutcome.View"/> (a new, reused fresh or template summary).</summary>
    Ok,
    Unavailable,
    InProgress,
    QuotaExceeded,
    Busy,
    Failed,
}

/// <param name="RetryAfter">For <see cref="GenerateStatus.Busy"/>: the provider's wait hint when it throttled; null for a busy replica.</param>
public sealed record GenerateOutcome(
    GenerateStatus Status, SummaryView? View = null, QuotaStatus? Quota = null, GenerationFailure? Failure = null, TimeSpan? RetryAfter = null);

/// <summary>
/// The summary use cases behind <c>GET</c>/<c>POST /api/insights/summary</c> (plan §3.1–3.3, ADR 0028). The report comes from
/// <see cref="IInsightsReportService"/> in the request scope (owner filter); nothing is recomputed here.
/// </summary>
public sealed class InsightsSummaryService(
    IInsightsReportService reports,
    ICurrentUser currentUser,
    IAiSummaryStore store,
    SummaryGenerator generator,
    AiUsageLedger ledger,
    PromptCatalog prompts,
    TemplateCatalog templates,
    DemoSummaryCatalog demos,
    TrainingInsightsAgentProvider agents,
    IOptionsMonitor<AiOptions> options)
{
    /// <summary>Read precedence: template period → fresh stored → guest demo fixture → stale stored → none.</summary>
    public async Task<SummaryView> GetAsync(SummaryKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var (report, sheet) = await BuildAsync(key, cancellationToken);

        if (SummaryTemplates.Select(report) is { } kind)
        {
            return TemplateView(key, sheet, kind);
        }

        var generation = await AvailabilityAsync(cancellationToken);
        var stored = await store.FindAsync(key, cancellationToken);
        if (stored is not null && IsFresh(stored, sheet))
        {
            return new SummaryView(key, SummaryState.Fresh, FromStored(stored), generation);
        }

        if (currentUser.IsGuest && Demo(key, report.Today, sheet) is { } demo)
        {
            return new SummaryView(key, SummaryState.Fresh, demo, generation);
        }

        return stored is not null
            ? new SummaryView(key, SummaryState.Stale, FromStored(stored), generation)
            : new SummaryView(key, SummaryState.None, null, generation);
    }

    /// <summary>Plan §3.1 order: provider/kill switch → report → template → reuse a fresh row unless regenerating → generate → store.</summary>
    public async Task<GenerateOutcome> GenerateAsync(SummaryKey key, bool regenerate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!options.CurrentValue.GenerationAvailable || agents.Agent is null)
        {
            return new GenerateOutcome(GenerateStatus.Unavailable);
        }

        var (report, sheet) = await BuildAsync(key, cancellationToken);
        if (SummaryTemplates.Select(report) is { } kind)
        {
            return new GenerateOutcome(GenerateStatus.Ok, TemplateView(key, sheet, kind));
        }

        if (!regenerate && await store.FindAsync(key, cancellationToken) is { } stored && IsFresh(stored, sheet))
        {
            return new GenerateOutcome(GenerateStatus.Ok, new SummaryView(key, SummaryState.Fresh, FromStored(stored), await AvailabilityAsync(cancellationToken)));
        }

        var result = await generator.GenerateAsync(new GenerationRequest(currentUser.UserId, currentUser.IsGuest, sheet), cancellationToken);
        switch (result.Status)
        {
            case GenerationStatus.Succeeded:
                var generated = result.Summary!;
                var row = new StoredSummary(
                    key, sheet.MetricsVersion, sheet.FactSheetVersion, generated.PromptVersion, generated.ModelDeployment, generated.ModelId,
                    FactSheetJson.Fingerprint(sheet), SummaryContentJson.ToJson(generated.Content), generated.InputTokens, generated.OutputTokens,
                    generated.CreatedAt);
                await store.UpsertAsync(row, CancellationToken.None);
                return new GenerateOutcome(GenerateStatus.Ok, new SummaryView(key, SummaryState.Fresh, FromStored(row), await AvailabilityAsync(CancellationToken.None)));
            case GenerationStatus.Unavailable:
                return new GenerateOutcome(GenerateStatus.Unavailable);
            case GenerationStatus.InProgress:
                return new GenerateOutcome(GenerateStatus.InProgress);
            case GenerationStatus.QuotaExceeded:
                return new GenerateOutcome(GenerateStatus.QuotaExceeded, Quota: result.Quota);
            case GenerationStatus.Busy:
                return new GenerateOutcome(GenerateStatus.Busy, RetryAfter: result.RetryAfter);
            default:
                return new GenerateOutcome(GenerateStatus.Failed, Failure: result.Failure);
        }
    }

    /// <summary>
    /// The validity tuple (ADR 0028 §2). The deployment is compared only while one is configured: with no provider, a stored summary
    /// stays fresh as long as its data and versions match (it cannot be regenerated anyway).
    /// </summary>
    public bool IsFresh(StoredSummary stored, FactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(sheet);
        return stored.MetricsVersion == sheet.MetricsVersion
            && stored.FactSheetVersion == sheet.FactSheetVersion
            && stored.PromptVersion == prompts.Current.Version
            && (agents.Deployment is null || stored.ModelDeployment == agents.Deployment)
            && stored.Fingerprint.Span.SequenceEqual(FactSheetJson.Fingerprint(sheet));
    }

    private async Task<(InsightsReport Report, FactSheet Sheet)> BuildAsync(SummaryKey key, CancellationToken cancellationToken)
    {
        var report = await reports.GetReportAsync(key.From, key.To, key.Compare, cancellationToken);
        return (report, FactSheetBuilder.Build(report, key.Language));
    }

    private async Task<GenerationInfo> AvailabilityAsync(CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.GenerationAvailable || agents.Agent is null)
        {
            return new GenerationInfo(GenerationAvailability.NotConfigured, null, null, null, null);
        }

        var quota = await ledger.GetStatusAsync(currentUser.UserId, currentUser.IsGuest, cancellationToken);
        return new GenerationInfo(
            quota.Block == QuotaBlock.None ? GenerationAvailability.Available : GenerationAvailability.QuotaExhausted,
            quota.Used,
            quota.Limit,
            quota.ResetsAt,
            quota.Window);
    }

    private SummaryView TemplateView(SummaryKey key, FactSheet sheet, SummaryTemplateKind kind)
    {
        var content = SummaryValues.Resolve(templates.Get(key.Language, kind, sheet), sheet);
        return new SummaryView(
            key,
            SummaryState.Fresh,
            new SummaryResult(SummarySource.Template, null, sheet.MetricsVersion, sheet.FactSheetVersion, null, null, content),
            new GenerationInfo(GenerationAvailability.NotNeeded, null, null, null, null));
    }

    private SummaryResult? Demo(SummaryKey key, DateOnly today, FactSheet sheet)
    {
        var fixture = demos.Fixtures.FirstOrDefault(f =>
            f.Language == key.Language && DemoPresets.Matches(f.Preset, today, key.From, key.To, key.Compare));
        if (fixture is null || !fixture.Holds(sheet)
            || !SummaryValidator.Validate(fixture.Draft, sheet, prompts.Current.Policy).IsValid)
        {
            return null;
        }

        return new SummaryResult(SummarySource.Demo, null, sheet.MetricsVersion, sheet.FactSheetVersion, null, null, SummaryValues.Resolve(fixture.Draft, sheet));
    }

    private static SummaryResult FromStored(StoredSummary stored) =>
        new(SummarySource.Model, stored.CreatedAt, stored.MetricsVersion, stored.FactSheetVersion, stored.PromptVersion, stored.ModelId ?? stored.ModelDeployment,
            SummaryContentJson.Parse(stored.ContentJson));
}
