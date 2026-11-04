using Microsoft.EntityFrameworkCore;
using RunnersDiary.Domain.Ai;
using RunnersDiary.Infrastructure.Persistence;

namespace RunnersDiary.Infrastructure.Ai;

/// <summary>
/// EF Core implementation of the usage ledger (ADR 0028 §4). <c>AiGenerations</c> is a system table without the Owner filter, so
/// every per-user read here filters by the explicit <c>userId</c> argument and nothing else reads this table. Rows hold counts and
/// outcomes only, never prompt or answer text. Timestamps are truncated to whole seconds (the column is <c>datetime2(0)</c>), so a
/// stored row is never later than the instant it was recorded.
/// </summary>
internal sealed class EfAiGenerationStore(RunnersDiaryDbContext db) : IAiGenerationStore
{
    public async Task AddAsync(AiGenerationRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var row = AiGeneration.Create(
            record.UserId,
            record.IsGuest,
            Truncate(record.CreatedAt),
            record.Outcome,
            (byte)Math.Clamp(record.Attempts, 0, byte.MaxValue),
            Math.Max(0, record.InputTokens),
            Math.Max(0, record.OutputTokens),
            Math.Max(0, record.DurationMs),
            checked((short)record.PromptVersion),
            record.ModelDeployment);
        db.AiGenerations.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        db.Entry(row).State = EntityState.Detached;
    }

    public async Task<IReadOnlyList<DateTimeOffset>> GetUserGenerationTimesAsync(Guid userId, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var query = db.AiGenerations.AsNoTracking().Where(g => g.UserId == userId && g.Outcome != AiGenerationOutcome.Throttled);
        if (since is { } from)
        {
            var sinceUtc = from.UtcDateTime;
            query = query.Where(g => g.CreatedAt > sinceUtc);
        }

        var times = await query.OrderBy(g => g.CreatedAt).Select(g => g.CreatedAt).ToListAsync(cancellationToken);
        return [.. times.Select(t => new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)))];
    }

    public async Task<GlobalAiUsage> GetGlobalUsageAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var sinceUtc = since.UtcDateTime;
        var window = db.AiGenerations.AsNoTracking().Where(g => g.CreatedAt > sinceUtc);
        var attempts = await window.SumAsync(g => (int)g.Attempts, cancellationToken);
        var guestAttempts = await window.Where(g => g.IsGuest).SumAsync(g => (int)g.Attempts, cancellationToken);
        return new GlobalAiUsage(attempts, guestAttempts);
    }

    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var cutoffUtc = cutoff.UtcDateTime;
        return db.AiGenerations.Where(g => g.CreatedAt < cutoffUtc).ExecuteDeleteAsync(cancellationToken);
    }

    private static DateTime Truncate(DateTimeOffset value) =>
        new(value.UtcDateTime.Ticks - (value.UtcDateTime.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
}
