using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Ai;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>
/// One ledger row (<c>AiGenerations</c>, plan §3.4): one per generation request that reached the model. No content, only counts.
/// <see cref="Attempts"/> counts the model calls that may have been billed (a timed-out or failed call included, a throttled or
/// never-sent one not); <see cref="InputTokens"/>/<see cref="OutputTokens"/> are the provider-reported usage, so they are a lower bound
/// when an attempt timed out (no usage comes back) and the global budgets therefore count attempts, not tokens.
/// </summary>
public sealed record AiGenerationRecord(
    Guid UserId,
    bool IsGuest,
    DateTimeOffset CreatedAt,
    AiGenerationOutcome Outcome,
    int Attempts,
    int InputTokens,
    int OutputTokens,
    int DurationMs,
    int PromptVersion,
    string ModelDeployment,
    AiFeature Feature = AiFeature.Summary);

/// <summary>
/// Global usage in a window, in <b>weighted model attempts</b> of everybody and of guests (ADR 0028 §4 cost ceiling, ADR 0040 §6): a summary
/// attempt counts 1, a plan attempt <c>Ai:Plan:GlobalAttemptWeight</c>.
/// </summary>
public sealed record GlobalAiUsage(int Attempts, int GuestAttempts);

/// <summary>
/// Persistence of the usage ledger (ADR 0028 §4). A <b>system ledger</b>: not owner-filtered and without a foreign key to users, so
/// global budgets survive guest cleanup. Implemented over EF Core in step 3 (backend-dotnet). Only <see cref="AiUsageLedger"/> uses it,
/// and every per-user read filters by the explicit <c>userId</c> argument.
/// </summary>
public interface IAiGenerationStore
{
    Task AddAsync(AiGenerationRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// The <c>CreatedAt</c> of the user's rows <b>of <paramref name="feature"/></b> with <c>CreatedAt &gt; since</c> (all rows when
    /// <paramref name="since"/> is null), ascending, <b>excluding</b> <see cref="AiGenerationOutcome.Throttled"/> rows: a provider 429 never
    /// costs the caller a generation. Small by construction: bounded by the quota plus the per-replica overshoot.
    /// </summary>
    Task<IReadOnlyList<DateTimeOffset>> GetUserGenerationTimesAsync(Guid userId, AiFeature feature, DateTimeOffset? since, CancellationToken cancellationToken);

    /// <summary>
    /// The weighted sum of <c>Attempts</c> of all rows and of guest rows with <c>CreatedAt &gt; since</c> (exclusive, so a row stops counting
    /// exactly at <c>CreatedAt + 24 h</c>), every outcome and feature included: <c>SUM(Attempts × (Feature = Plan ? planAttemptWeight : 1))</c>.
    /// </summary>
    Task<GlobalAiUsage> GetGlobalUsageAsync(DateTimeOffset since, int planAttemptWeight, CancellationToken cancellationToken);

    /// <summary>Deletes rows with <c>CreatedAt &lt; cutoff</c>; returns how many. Called by the guest cleanup run (30-day retention).</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

/// <summary>The account quota window: users have a rolling 24 h, guests a per-account lifetime (≤ 24 h).</summary>
public enum QuotaWindow
{
    Rolling24h,
    Account,
}

/// <summary>Which quota blocks a generation (problem <c>ai-quota</c> with <c>scope</c>), if any.</summary>
public enum QuotaBlock
{
    None,
    Account,
    Global,
}

/// <param name="Used">Generations counted in the account window.</param>
/// <param name="Limit">The account limit.</param>
/// <param name="ResetsAt">When the account window frees a generation (users only; null for guests and when not exhausted).</param>
/// <param name="Block">The blocking quota; <see cref="QuotaBlock.Account"/> wins over <see cref="QuotaBlock.Global"/>.</param>
/// <param name="BlockLimit">The limit of the blocking quota.</param>
public sealed record QuotaStatus(int Used, int Limit, QuotaWindow Window, DateTimeOffset? ResetsAt, QuotaBlock Block, int? BlockLimit)
{
    public int Remaining => Math.Max(0, Limit - Used);
}

/// <summary>
/// Durable quotas from the usage ledger (ADR 0028 §4, ADR 0040 §6), configurable:
/// <list type="bullet">
/// <item>Per account and <b>per feature</b>, in <b>generations</b> (ledger rows of that feature): summaries users 10 per rolling 24 h, guests
/// 3 per account; plans users 5 per rolling 24 h, guests 2 per account. Every generation that reached the provider counts whatever its
/// outcome, except <see cref="AiGenerationOutcome.Throttled"/> (the provider's capacity, not the caller's).</item>
/// <item>Global and guest-global, shared by all features, in <b>weighted model attempts</b>: 200 and 60 per 24 h, a plan attempt counting
/// <c>Ai:Plan:GlobalAttemptWeight</c> (3). One generation is up to <c>MaxAttempts</c> attempts, so counting attempts keeps the daily cost
/// ceiling a fixed number of model calls.</item>
/// </list>
/// The check-then-insert race can overshoot by at most <c>MaxConcurrentGenerations − 1</c> generations per replica (accepted); the
/// global check runs before a generation starts, so a generation admitted just under the limit may add up to <c>MaxAttempts</c> attempts.
/// </summary>
public sealed class AiUsageLedger(IAiGenerationStore store, IOptions<AiOptions> options, TimeProvider clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>Ledger rows are kept 30 days (ADR 0028 §4).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>The summary quotas (M6); same as <see cref="GetStatusAsync(AiFeature, Guid, bool, CancellationToken)"/> with <see cref="AiFeature.Summary"/>.</summary>
    public Task<QuotaStatus> GetStatusAsync(Guid userId, bool isGuest, CancellationToken cancellationToken) =>
        GetStatusAsync(AiFeature.Summary, userId, isGuest, cancellationToken);

    public async Task<QuotaStatus> GetStatusAsync(AiFeature feature, Guid userId, bool isGuest, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var quotas = settings.Quotas;
        var now = clock.GetUtcNow();
        var since = now - Window;

        var window = isGuest ? QuotaWindow.Account : QuotaWindow.Rolling24h;
        var limit = feature switch
        {
            AiFeature.Plan => isGuest ? settings.Plan.Quotas.GuestPerAccount : settings.Plan.Quotas.UserPer24h,
            _ => isGuest ? quotas.GuestPerAccount : quotas.UserPer24h,
        };
        var times = await store.GetUserGenerationTimesAsync(userId, feature, isGuest ? null : since, cancellationToken);
        var used = times.Count;

        if (used >= limit)
        {
            // The window frees a generation when enough of the oldest rows age out to bring the count below the limit.
            DateTimeOffset? resetsAt = isGuest || times.Count == 0 ? null : times[used - limit] + Window;
            return new QuotaStatus(used, limit, window, resetsAt, QuotaBlock.Account, limit);
        }

        var global = await store.GetGlobalUsageAsync(since, settings.Plan.GlobalAttemptWeight, cancellationToken);
        if (global.Attempts >= quotas.GlobalPer24h)
        {
            return new QuotaStatus(used, limit, window, null, QuotaBlock.Global, quotas.GlobalPer24h);
        }

        if (isGuest && global.GuestAttempts >= quotas.GuestGlobalPer24h)
        {
            return new QuotaStatus(used, limit, window, null, QuotaBlock.Global, quotas.GuestGlobalPer24h);
        }

        return new QuotaStatus(used, limit, window, null, QuotaBlock.None, null);
    }

    public Task RecordAsync(AiGenerationRecord record, CancellationToken cancellationToken) => store.AddAsync(record, cancellationToken);

    /// <summary>The purge hook for the guest cleanup run: removes ledger rows older than <see cref="Retention"/>.</summary>
    public Task<int> PurgeAsync(CancellationToken cancellationToken) => store.PurgeOlderThanAsync(clock.GetUtcNow() - Retention, cancellationToken);
}
