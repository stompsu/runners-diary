using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;
using RunnersDiary.Infrastructure.Users;

namespace RunnersDiary.Infrastructure.Guests;

/// <summary>
/// Removes expired guests (ADR 0011 §4-5): per guest, blobs by prefix first, then the user row (FK cascades remove runs and
/// tracks). A blob failure keeps the row so the next run retries; the reverse order could leave blobs nobody knows about.
/// Idempotent and safe to run on several replicas at once.
/// <para>
/// After deleting a guest the <c>UserProvisioner</c> cache entry for it is evicted, so a deleted guest's token (still valid by
/// <c>exp</c> if cleanup ran early) is refused at once instead of up to five minutes later.
/// </para>
/// </summary>
public sealed partial class GuestCleanup(
    IServiceScopeFactory scopes,
    IBlobStore blobs,
    TimeProvider clock,
    IOptions<GuestOptions> options,
    ILogger<GuestCleanup> logger)
{
    private long _lastOpportunisticTicks;

    /// <summary>Removes up to one batch of expired guests; returns how many were fully removed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Cleanup;
        var cutoff = clock.GetUtcNow().UtcDateTime - settings.Grace;

        await using var scope = scopes.CreateAsyncScope();
        // Explicitly anonymous, whatever request (if any) started this run: only the Users table (no owner filter) is queried and
        // deleted from, and the ownership guard would reject any owned-entity write from a caller-less context.
        var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>();
        await using var db = new RunnersDiaryDbContext(dbOptions, AnonymousCurrentUser.Instance);
        var provisioner = scope.ServiceProvider.GetService<UserProvisioner>();

        var expired = await db.Users.AsNoTracking()
            .Where(u => u.Kind == UserKind.Guest && u.ExpiresAt < cutoff)
            .OrderBy(u => u.ExpiresAt)
            .Select(u => u.Id)
            .Take(Math.Max(1, settings.BatchSize))
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var id in expired)
        {
            try
            {
                await blobs.DeleteByPrefixAsync(BlobPaths.UserPrefix(id), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBlobDeleteFailed(ex, id);
                continue;
            }

            // Kind is repeated so a real user can never be deleted by this path.
            await db.Users.Where(u => u.Id == id && u.Kind == UserKind.Guest).ExecuteDeleteAsync(cancellationToken);
            provisioner?.EvictGuest(id);
            removed++;
        }

        if (expired.Count > 0)
        {
            LogRun(removed, expired.Count);
        }

        return removed;
    }

    /// <summary>
    /// Starts a background run unless one started within <see cref="GuestOptions.CleanupOptions.OpportunisticMinInterval"/>.
    /// Fire-and-forget by design: failures are logged and the periodic service retries.
    /// </summary>
    public void RequestOpportunisticRun()
    {
        var settings = options.Value.Cleanup;
        if (!settings.Enabled)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _lastOpportunisticTicks);
        if (last != 0 && now - last < settings.OpportunisticMinInterval.Ticks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastOpportunisticTicks, now, last) != last)
        {
            return;
        }

        // The run must not inherit the triggering request's ExecutionContext (AsyncLocal HttpContext, i.e. its user).
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunOnceAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    LogRunFailed(ex);
                }
            });
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Guest cleanup removed {Removed} of {Expired} expired guests.")]
    private partial void LogRun(int removed, int expired);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete the blobs of expired guest {UserId}; the user row is kept and cleanup will retry.")]
    private partial void LogBlobDeleteFailed(Exception exception, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Opportunistic guest cleanup failed.")]
    private partial void LogRunFailed(Exception exception);
}
