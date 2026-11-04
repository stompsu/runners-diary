using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RunnersDiary.Infrastructure.Persistence;

/// <summary>
/// Counts the SQL connections EF Core has opened in this process (ADR 0032 §2). The periodic guest cleanup compares the counter
/// between ticks and skips a tick when nothing used SQL in the meantime, so a replica woken by an anonymous request or a crawler
/// never wakes an auto-paused serverless database by itself. Registered as a singleton and added to every <c>DbContext</c> options.
/// </summary>
public sealed class SqlActivityInterceptor : DbConnectionInterceptor
{
    private long _opened;

    /// <summary>Number of connections opened so far (monotonic).</summary>
    public long OpenedCount => Interlocked.Read(ref _opened);

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Interlocked.Increment(ref _opened);
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _opened);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
