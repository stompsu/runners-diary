using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Infrastructure.Gpx;

public enum TrackIngestOutcome
{
    Created,
    Rejected,
    RunNotFound,
    RestDay,
    Exists,
    LimitReached,
    Busy,
    TooLarge,
    StorageUnavailable,

    /// <summary>The track to replace was deleted meanwhile.</summary>
    TrackNotFound,
}

/// <summary>Outcome of <see cref="TrackIngestionService.IngestAsync"/>; <see cref="Track"/> is set for Created, <see cref="Rejection"/> for Rejected.</summary>
public sealed record TrackIngestResult(TrackIngestOutcome Outcome, GpxTrack? Track = null, GpxRejection? Rejection = null, int Limit = 0, string? TimeZoneId = null);

/// <summary>
/// The synchronous upload pipeline of ADR 0020 §1 (steps 2 and 4 to 10): load the run through the owner filter, conflict and limit
/// checks, bounded copy + SHA-256, processing slot, parse + process, both blobs, one <c>SaveChanges</c>, best-effort removal of a
/// replaced track's blobs. A failure at any point leaves no row and no blob of the new track.
/// </summary>
public sealed partial class TrackIngestionService(
    RunnersDiaryDbContext db,
    ICurrentUser currentUser,
    IBlobStore blobs,
    TrackProcessingSlots slots,
    IOptions<GpxLimitsOptions> options,
    TimeProvider clock,
    ILogger<TrackIngestionService> logger)
{
    private const string GpxContentType = "application/gpx+xml";

    /// <summary>
    /// The cheap checks that need no request body, run before the multipart form is read: the run exists and is the caller's, it is not a
    /// rest day, and the track cap is not already reached for a run that has no track to replace. Null when the upload may proceed.
    /// </summary>
    public async Task<TrackIngestResult?> PreflightAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await db.Runs.AsNoTracking().Include(r => r.Tracks).FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return new TrackIngestResult(TrackIngestOutcome.RunNotFound);
        }

        if (await db.RunTypes.AsNoTracking().AnyAsync(t => t.Id == run.RunTypeId && t.IsRestDay, cancellationToken))
        {
            return new TrackIngestResult(TrackIngestOutcome.RestDay);
        }

        var maxTracks = options.Value.MaxTracksFor(currentUser.IsGuest);
        if (run.Tracks.Count == 0 && await db.GpxTracks.CountAsync(cancellationToken) >= maxTracks)
        {
            return new TrackIngestResult(TrackIngestOutcome.LimitReached, Limit: maxTracks);
        }

        return null;
    }

    public async Task<TrackIngestResult> IngestAsync(
        Guid runId, GpxTrackKind kind, string originalFileName, Stream content, bool replace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var settings = options.Value;
        var userId = currentUser.UserId;
        var isGuest = currentUser.IsGuest;

        // The owner filter hides foreign runs: they are indistinguishable from missing ones.
        var run = await db.Runs.Include(r => r.Tracks).FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return new TrackIngestResult(TrackIngestOutcome.RunNotFound);
        }

        if (await db.RunTypes.AsNoTracking().AnyAsync(t => t.Id == run.RunTypeId && t.IsRestDay, cancellationToken))
        {
            return new TrackIngestResult(TrackIngestOutcome.RestDay);
        }

        var existing = run.Tracks.FirstOrDefault(t => t.Kind == kind);
        if (existing is not null && !replace)
        {
            return new TrackIngestResult(TrackIngestOutcome.Exists);
        }

        var maxTracks = settings.MaxTracksFor(isGuest);
        if (existing is null && await db.GpxTracks.CountAsync(cancellationToken) >= maxTracks)
        {
            return new TrackIngestResult(TrackIngestOutcome.LimitReached, Limit: maxTracks);
        }

        using var lease = await slots.TryAcquireAsync(settings.ProcessingWait, cancellationToken);
        if (lease is null)
        {
            return new TrackIngestResult(TrackIngestOutcome.Busy);
        }

        var maxBytes = settings.MaxFileBytesFor(isGuest);
        var buffer = new MemoryStream(content.CanSeek ? (int)Math.Clamp(content.Length, 0, maxBytes) : 0);
        if (!await CopyBoundedAsync(content, buffer, maxBytes, cancellationToken))
        {
            return new TrackIngestResult(TrackIngestOutcome.TooLarge);
        }

        buffer.Position = 0;
        var sha256 = SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        var read = GpxReader.Read(buffer, settings.ReadLimitsFor(isGuest), clock.GetUtcNow());
        if (read.Rejection is { } rejection)
        {
            LogRejected(rejection.Code);
            return new TrackIngestResult(TrackIngestOutcome.Rejected, Rejection: rejection);
        }

        var processed = TrackProcessor.Process(read.Document!, cancellationToken);
        var geoJson = TrackGeoJsonWriter.Write(processed);

        var trackId = Guid.CreateVersion7();
        var originalBlob = BlobPaths.TrackOriginal(userId, trackId);
        var processedBlob = BlobPaths.TrackProcessed(userId, trackId);
        var track = GpxTrack.CreateProcessed(
            trackId, run.Id, userId, kind, originalFileName, buffer.Length, sha256, originalBlob, processedBlob, processed, clock.GetUtcNow());

        try
        {
            await blobs.UploadAsync(originalBlob, buffer.GetBuffer().AsMemory(0, (int)buffer.Length), GpxContentType, cancellationToken);
            await blobs.UploadAsync(processedBlob, geoJson, TrackGeoJsonWriter.ContentType, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // An aborted request may have written the first blob already; the rollback ignores the cancelled token.
            await TryRollbackAsync(userId, trackId);
            throw;
        }
#pragma warning disable CA1031 // Any storage failure (SDK, network, injected) must roll the new prefix back and become a retryable 503.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogStorageFailed(ex, runId);
            await TryRollbackAsync(userId, trackId);
            return new TrackIngestResult(TrackIngestOutcome.StorageUnavailable);
        }

        TrackIngestOutcome committed;
        try
        {
            committed = await CommitAsync(track, existing?.Id, run.Id, kind, maxTracks, userId, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent upload of the same kind (unique index) or a run deleted meanwhile (composite FK).
            await TryRollbackAsync(userId, trackId);
            db.ChangeTracker.Clear();
            return await db.Runs.AnyAsync(r => r.Id == runId, CancellationToken.None)
                ? new TrackIngestResult(TrackIngestOutcome.Exists)
                : new TrackIngestResult(TrackIngestOutcome.RunNotFound);
        }
        catch
        {
            await TryRollbackAsync(userId, trackId);
            throw;
        }

        if (committed != TrackIngestOutcome.Created)
        {
            await TryRollbackAsync(userId, trackId);
            return new TrackIngestResult(committed, Limit: maxTracks);
        }

        if (existing is not null)
        {
            await TryDeleteReplacedAsync(userId, existing.Id);
        }

        return new TrackIngestResult(TrackIngestOutcome.Created, track, TimeZoneId: run.TimeZoneId);
    }

    /// <summary>
    /// One transaction under the user's track lock: re-count the cap (a concurrent upload may have filled it since the early check),
    /// delete the replaced row, insert the new one. Returns Created, LimitReached, Exists (the kind was replaced by someone else) or
    /// TrackNotFound (the track to replace is gone). The execution strategy re-runs the whole delegate on a transient failure.
    /// </summary>
    private async Task<TrackIngestOutcome> CommitAsync(
        GpxTrack track, Guid? replacedId, Guid runId, GpxTrackKind kind, int maxTracks, Guid userId, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await UserTrackLock.AcquireAsync(db, userId, cancellationToken);
            if (replacedId is { } old)
            {
                var deleted = await db.GpxTracks.Where(t => t.Id == old && t.RunId == runId).ExecuteDeleteAsync(cancellationToken);
                if (deleted == 0)
                {
                    // Deleted (404) or already replaced by a concurrent upload (409) since this request looked.
                    return await db.GpxTracks.AnyAsync(t => t.RunId == runId && t.Kind == kind, cancellationToken)
                        ? TrackIngestOutcome.Exists
                        : TrackIngestOutcome.TrackNotFound;
                }
            }
            else if (await db.GpxTracks.CountAsync(cancellationToken) >= maxTracks)
            {
                return TrackIngestOutcome.LimitReached;
            }

            db.GpxTracks.Add(track);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TrackIngestOutcome.Created;
        });
    }

    private static async Task<bool> CopyBoundedAsync(Stream source, MemoryStream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var chunk = new byte[81_920];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return true;
            }

            if (destination.Length + read > maxBytes)
            {
                return false;
            }

            destination.Write(chunk, 0, read);
        }
    }

    private async Task TryRollbackAsync(Guid userId, Guid trackId)
    {
        try
        {
            // Not tied to the request token: an aborted request must still clean up.
            await blobs.DeleteByPrefixAsync(BlobPaths.TrackPrefix(userId, trackId), CancellationToken.None);
        }
#pragma warning disable CA1031 // Best-effort rollback; the original failure is what the caller reports.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRollbackFailed(ex, BlobPaths.TrackPrefix(userId, trackId));
        }
    }

    private async Task TryDeleteReplacedAsync(Guid userId, Guid oldTrackId)
    {
        var prefix = BlobPaths.TrackPrefix(userId, oldTrackId);
        try
        {
            await blobs.DeleteByPrefixAsync(prefix, CancellationToken.None);
        }
#pragma warning disable CA1031 // The replace is committed; a failed cleanup only orphans small blobs (ADR 0023).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogTrackBlobOrphaned(ex, prefix);
        }
    }

    [LoggerMessage(EventId = 4101, Level = LogLevel.Debug, Message = "GPX upload rejected: {Code}.")]
    private partial void LogRejected(GpxRejectionCode code);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Error, Message = "Storing the blobs of an upload for run {RunId} failed.")]
    private partial void LogStorageFailed(Exception exception, Guid runId);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Warning, Message = "Rolling back blobs under {Prefix} failed; they are orphaned.")]
    private partial void LogRollbackFailed(Exception exception, string prefix);

    [LoggerMessage(EventId = 4104, EventName = "TrackBlobOrphaned", Level = LogLevel.Warning, Message = "TrackBlobOrphaned: deleting the replaced track's blobs under {Prefix} failed.")]
    private partial void LogTrackBlobOrphaned(Exception exception, string prefix);
}
