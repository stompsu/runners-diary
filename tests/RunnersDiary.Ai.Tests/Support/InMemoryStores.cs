using RunnersDiary.Domain.Ai;
using RunnersDiary.Domain.Insights;
using RunnersDiary.Infrastructure.Ai;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Insights;

namespace RunnersDiary.Ai.Tests.Support;

/// <summary>An in-memory <see cref="IAiGenerationStore"/> with the semantics step 3 implements over EF Core.</summary>
public sealed class InMemoryAiGenerationStore : IAiGenerationStore
{
    private readonly List<AiGenerationRecord> _rows = [];

    public IReadOnlyList<AiGenerationRecord> Rows
    {
        get
        {
            lock (_rows)
            {
                return [.. _rows];
            }
        }
    }

    public Task AddAsync(AiGenerationRecord record, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            _rows.Add(record);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DateTimeOffset>> GetUserGenerationTimesAsync(Guid userId, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            IReadOnlyList<DateTimeOffset> times =
            [
                .. _rows.Where(r => r.UserId == userId && r.Outcome != AiGenerationOutcome.Throttled && (since is null || r.CreatedAt > since))
                    .Select(r => r.CreatedAt).Order(),
            ];
            return Task.FromResult(times);
        }
    }

    public Task<GlobalAiUsage> GetGlobalUsageAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            var window = _rows.Where(r => r.CreatedAt > since).ToList();
            return Task.FromResult(new GlobalAiUsage(window.Sum(r => r.Attempts), window.Where(r => r.IsGuest).Sum(r => r.Attempts)));
        }
    }

    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        lock (_rows)
        {
            return Task.FromResult(_rows.RemoveAll(r => r.CreatedAt < cutoff));
        }
    }
}

/// <summary>An in-memory <see cref="IAiSummaryStore"/> for one caller (the owner filter is implicit).</summary>
public sealed class InMemoryAiSummaryStore : IAiSummaryStore
{
    public Dictionary<SummaryKey, StoredSummary> Rows { get; } = [];

    public Task<StoredSummary?> FindAsync(SummaryKey key, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(key));

    public Task UpsertAsync(StoredSummary summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Rows[summary.Key] = summary;
        return Task.CompletedTask;
    }
}

public sealed class TestCurrentUser(Guid userId, bool isGuest) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid UserId { get; } = userId;

    public Guid? UserIdOrNull => UserId;

    public bool IsGuest { get; } = isGuest;
}

/// <summary>Builds the report from an in-memory log (the real service reads the same inputs through EF).</summary>
public sealed class TestReportService(Func<IReadOnlyList<RunFact>> facts, Func<IReadOnlyList<RaceFact>> races, Func<DateOnly> today) : IInsightsReportService
{
    public Task<InsightsReport> GetReportAsync(DateOnly firstDay, DateOnly lastDay, ComparisonKind compare, CancellationToken cancellationToken)
    {
        var list = facts();
        return Task.FromResult(InsightsReportBuilder.Build(new InsightsPeriod(firstDay, lastDay), compare, today(), list, list, races(), null));
    }
}
