using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RunnersDiary.Domain.Demo;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Time;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Infrastructure.Guests;

/// <summary>
/// Persists a new guest together with its own copy of the demo diary: the dataset expanded for "today" in the guest's zone,
/// plus a private copy of every referenced GPX file and its pre-processed GeoJSON under the guest's blob prefix, with <c>Processed</c>
/// track rows (ADR 0011 §2-3, ADR 0021 §8). Nothing is parsed per guest.
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
    /// Uploads the blobs, then inserts the user, runs and track rows in one <c>SaveChanges</c> (one transaction).
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
        var uploads = new List<(string BlobName, ReadOnlyMemory<byte> Content, string ContentType)>();

        foreach (var demo in DemoScheduleExpander.Expand(dataset.Template, today))
        {
            var run = Run.Create(guest.Id, demo.Values, now);
            runs.Add(run);

            if (demo.Gpx is { } gpx)
            {
                var file = dataset.Files[gpx.File];
                var trackId = Guid.CreateVersion7();
                var originalBlob = BlobPaths.TrackOriginal(guest.Id, trackId);
                var processedBlob = BlobPaths.TrackProcessed(guest.Id, trackId);
                uploads.Add((originalBlob, file.Content, GpxContentType));
                uploads.Add((processedBlob, file.GeoJson, TrackGeoJsonWriter.ContentType));
                tracks.Add(GpxTrack.CreateProcessed(
                    trackId, run.Id, guest.Id, gpx.Kind, file.FileName, file.Content.Length, file.Sha256.ToArray(), originalBlob,
                    processedBlob, file.Processed, now));
            }
        }

        try
        {
            await Task.WhenAll(uploads.Select(u => blobs.UploadAsync(u.BlobName, u.Content, u.ContentType, cancellationToken)));

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
