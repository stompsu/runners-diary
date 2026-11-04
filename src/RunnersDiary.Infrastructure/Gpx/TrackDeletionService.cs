using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Infrastructure.Gpx;

public enum TrackDeleteOutcome
{
    Deleted,
    NotFound,
    StorageUnavailable,
}

/// <summary>Blobs first, then rows (ADR 0023 §5): a storage failure keeps the row so the user can retry, and no blob is ever left unrecorded.</summary>
public sealed partial class TrackDeletionService(RunnersDiaryDbContext db, ICurrentUser currentUser, IBlobStore blobs, ILogger<TrackDeletionService> logger)
{
    public async Task<TrackDeleteOutcome> DeleteTrackAsync(Guid runId, Guid trackId, CancellationToken cancellationToken)
    {
        // Both filters (owner and run) apply: a foreign track, a track of another run and a missing one are all NotFound.
        var exists = await db.GpxTracks.AsNoTracking().AnyAsync(t => t.Id == trackId && t.RunId == runId, cancellationToken);
        if (!exists)
        {
            return TrackDeleteOutcome.NotFound;
        }

        if (!await TryDeleteBlobsAsync([trackId], cancellationToken))
        {
            return TrackDeleteOutcome.StorageUnavailable;
        }

        await db.GpxTracks.Where(t => t.Id == trackId && t.RunId == runId).ExecuteDeleteAsync(cancellationToken);
        return TrackDeleteOutcome.Deleted;
    }

    public async Task<TrackDeleteOutcome> DeleteRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await db.Runs.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => new { r.Id, TrackIds = r.Tracks.Select(t => t.Id).ToList() })
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
        {
            return TrackDeleteOutcome.NotFound;
        }

        if (!await TryDeleteBlobsAsync(run.TrackIds, cancellationToken))
        {
            return TrackDeleteOutcome.StorageUnavailable;
        }

        // Under the user's track lock (which every insert also takes) re-read the run's tracks and delete the run; the cascade removes
        // the rows. Tracks committed by an upload that raced the first read are now known, and their blobs are removed best effort
        // after the commit (the run is gone, so no later insert can succeed). A run deleted concurrently counts as deleted.
        var knownIds = run.TrackIds.ToHashSet();
        var late = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await UserTrackLock.AcquireAsync(db, currentUser.UserId, cancellationToken);
            var current = await db.GpxTracks.AsNoTracking().Where(t => t.RunId == runId).Select(t => t.Id).ToListAsync(cancellationToken);
            await db.Runs.Where(r => r.Id == runId).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return current.Where(id => !knownIds.Contains(id)).ToList();
        });

        foreach (var trackId in late)
        {
            try
            {
                await blobs.DeleteByPrefixAsync(BlobPaths.TrackPrefix(currentUser.UserId, trackId), CancellationToken.None);
            }
#pragma warning disable CA1031 // The run is already deleted; a failed cleanup only orphans small blobs (ADR 0023).
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogLateBlobOrphaned(ex, trackId);
            }
        }

        return TrackDeleteOutcome.Deleted;
    }

    private async Task<bool> TryDeleteBlobsAsync(IReadOnlyCollection<Guid> trackIds, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        foreach (var trackId in trackIds)
        {
            try
            {
                await blobs.DeleteByPrefixAsync(BlobPaths.TrackPrefix(userId, trackId), cancellationToken);
            }
#pragma warning disable CA1031 // Any storage failure keeps the rows and is reported as a retryable 503.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogBlobDeleteFailed(ex, trackId);
                return false;
            }
        }

        return true;
    }

    [LoggerMessage(EventId = 4111, Level = LogLevel.Error, Message = "Deleting the blobs of track {TrackId} failed; nothing was removed.")]
    private partial void LogBlobDeleteFailed(Exception exception, Guid trackId);

    [LoggerMessage(EventId = 4112, EventName = "TrackBlobOrphaned", Level = LogLevel.Warning, Message = "TrackBlobOrphaned: deleting the blobs of track {TrackId}, uploaded while its run was being deleted, failed.")]
    private partial void LogLateBlobOrphaned(Exception exception, Guid trackId);
}
