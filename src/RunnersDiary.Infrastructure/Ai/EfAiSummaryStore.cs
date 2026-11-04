using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>
/// EF Core implementation of the caller's stored summaries. Every read goes through the Owner filter and the owner of a new row is
/// always the current user (never a parameter), so a caller can neither see nor replace another user's summary (ADR 0010).
/// </summary>
internal sealed class EfAiSummaryStore(RunnersDiaryDbContext db, ICurrentUser currentUser) : IAiSummaryStore
{
    /// <summary>Per-account caps (ADR 0028 §1): the oldest summaries by <c>CreatedAt</c> are dropped beyond them.</summary>
    public const int MaxRowsPerUser = 1000;

    public const int MaxRowsPerGuest = 20;

    /// <summary>The <c>AiSummaries.ModelId</c> column length; a longer provider-reported id is truncated (it is audit-only).</summary>
    public const int MaxModelIdLength = 100;

    private const int SqlUniqueViolation = 2627;
    private const int SqlUniqueIndexViolation = 2601;

    public async Task<StoredSummary?> FindAsync(SummaryKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var (from, to, compare, language) = Parts(key);
        var row = await db.AiSummaries.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PeriodFrom == from && s.PeriodTo == to && s.Compare == compare && s.Language == language, cancellationToken);
        return row is null ? null : ToStored(key, row);
    }

    public async Task UpsertAsync(StoredSummary summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summary);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await UpsertOnceAsync(summary, cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt == 0 && IsUniqueViolation(ex))
            {
                // A concurrent generation (another replica) inserted the same key first: replace its row instead.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task UpsertOnceAsync(StoredSummary summary, CancellationToken cancellationToken)
    {
        var (from, to, compare, language) = Parts(summary.Key);
        var existing = await db.AiSummaries
            .FirstOrDefaultAsync(s => s.PeriodFrom == from && s.PeriodTo == to && s.Compare == compare && s.Language == language, cancellationToken);

        if (existing is null)
        {
            existing = AiSummary.Create(currentUser.UserId, from, to, compare, language);
            db.AiSummaries.Add(existing);
            await DropOldestBeyondCapAsync(cancellationToken);
        }

        existing.Replace(
            checked((short)summary.MetricsVersion),
            checked((short)summary.FactSheetVersion),
            checked((short)summary.PromptVersion),
            summary.ModelDeployment,
            TruncateModelId(summary.ModelId),
            summary.Fingerprint.ToArray(),
            summary.ContentJson,
            Math.Max(0, summary.InputTokens),
            Math.Max(0, summary.OutputTokens),
            Truncate(summary.CreatedAt));
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(existing).State = EntityState.Detached;
    }

    /// <summary>Called before an insert: makes room for one more row by removing the oldest ones beyond the cap.</summary>
    private async Task DropOldestBeyondCapAsync(CancellationToken cancellationToken)
    {
        var cap = currentUser.IsGuest ? MaxRowsPerGuest : MaxRowsPerUser;
        var count = await db.AiSummaries.CountAsync(cancellationToken);
        var excess = count - (cap - 1);
        if (excess <= 0)
        {
            return;
        }

        var oldest = await db.AiSummaries.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).Take(excess).ToListAsync(cancellationToken);
        db.AiSummaries.RemoveRange(oldest);
    }

    /// <summary>The provider's model id is untrusted for length: cut to the column, so a long id never fails the insert.</summary>
    internal static string? TruncateModelId(string? modelId) =>
        modelId is { Length: > MaxModelIdLength } ? modelId[..MaxModelIdLength] : modelId;

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: SqlUniqueViolation or SqlUniqueIndexViolation };

    private static (DateOnly From, DateOnly To, byte Compare, string Language) Parts(SummaryKey key) =>
        (key.From, key.To, (byte)key.Compare, key.Language);

    private static StoredSummary ToStored(SummaryKey key, AiSummary row) => new(
        key,
        row.MetricsVersion,
        row.FactSheetVersion,
        row.PromptVersion,
        row.ModelDeployment,
        row.ModelId,
        row.Fingerprint,
        row.ContentJson,
        row.InputTokens,
        row.OutputTokens,
        new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)));

    private static DateTime Truncate(DateTimeOffset value) =>
        new(value.UtcDateTime.Ticks - (value.UtcDateTime.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
}
