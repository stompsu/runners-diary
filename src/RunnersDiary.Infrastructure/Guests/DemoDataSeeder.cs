using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Time;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Infrastructure.Guests;

/// <summary>
/// Persists a new guest together with its own copy of the demo diary: the dataset expanded for "today" in the guest's zone,
/// plus a private copy of every referenced GPX file under the guest's blob prefix (ADR 0011 §2-3).
/// Seeding runs without a request caller, so it writes through its <b>own</b> DbContext whose caller is explicitly the new guest:
/// the ownership guard then rejects any row that is not the guest's, whatever the surrounding request is.
/// </summary>
public sealed partial class DemoDataSeeder(
    DemoDatasetLoader loader,
    IBlobStore blobs,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<DemoDataSeeder> logger)
{
    private const string GpxContentType = "application/gpx+xml";

    /// <summary>
    /// Uploads the blobs, then inserts the user, runs and track records in one <c>SaveChanges</c> (one transaction).
    /// On failure the uploaded blobs are removed again and the exception propagates; nothing is left behind.
    /// </summary>
    public async Task SeedAsync(User guest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(guest);
        if (guest.Kind != UserKind.Guest)
        {
            throw new InvalidOperationException("Only guest accounts are seeded with demo data.");
        }

        var dataset = loader.Get();
        var now = clock.GetUtcNow();
        var today = UserToday.Get(clock, dataset.Template.TimeZoneId);

        var runs = new List<Run>();
        var tracks = new List<GpxTrack>();
        var uploads = new List<(string BlobName, DemoFile File)>();

        foreach (var demo in DemoScheduleExpander.Expand(dataset.Template, today))
        {
            var run = Run.Create(guest.Id, demo.Values, now);
            runs.Add(run);

            if (demo.Gpx is { } gpx)
            {
                var file = dataset.Files[gpx.File];
                var trackId = Guid.CreateVersion7();
                var blobName = BlobPaths.TrackOriginal(guest.Id, trackId);
                uploads.Add((blobName, file));
                tracks.Add(GpxTrack.CreatePending(
                    trackId, run.Id, guest.Id, gpx.Kind, file.FileName, file.Content.Length, file.Sha256.ToArray(), blobName, now));
            }
        }

        try
        {
            await Task.WhenAll(uploads.Select(u => blobs.UploadAsync(u.BlobName, u.File.Content, GpxContentType, cancellationToken)));

            await using var scope = scopes.CreateAsyncScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RunnersDiaryDbContext>>();
            await using var db = new RunnersDiaryDbContext(options, new FixedCurrentUser(guest.Id, isGuest: true));
            db.Users.Add(guest);
            db.Runs.AddRange(runs);
            db.GpxTracks.AddRange(tracks);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await RollBackBlobsAsync(guest.Id);
            throw;
        }

        LogSeeded(guest.Id, runs.Count, tracks.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded guest {UserId} with {RunCount} runs and {TrackCount} tracks.")]
    private partial void LogSeeded(Guid userId, int runCount, int trackCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove the blobs of guest {UserId} after a failed seeding; guest cleanup will not find a user row for them.")]
    private partial void LogRollbackFailed(Exception exception, Guid userId);

    private async Task RollBackBlobsAsync(Guid userId)
    {
        try
        {
            await blobs.DeleteByPrefixAsync(BlobPaths.UserPrefix(userId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogRollbackFailed(ex, userId);
        }
    }
}
