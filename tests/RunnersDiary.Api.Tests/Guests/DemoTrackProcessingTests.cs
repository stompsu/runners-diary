using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Runs;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace RunnersDiary.Api.Tests.Guests;

/// <summary>AC7 (plan §1): a fresh guest's demo tracks are already processed (summary row + GeoJSON blob), without parsing per guest.</summary>
[Collection(ContainersFixtureGroup.Name)]
public sealed class DemoTrackSeedingTests(ContainersApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fresh_guest_has_six_processed_tracks_and_twelve_blobs()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("processed");
        var userId = guest.UserId!.Value;

        var tracks = await factory.TracksAsync(userId);
        var blobs = await factory.BlobNamesAsync(BlobPaths.UserPrefix(userId));

        Assert.Equal(6, tracks.Count);
        Assert.Equal(12, blobs.Count);
        Assert.All(tracks, t =>
        {
            Assert.Equal(GpxTrackStatus.Processed, t.Status);
            Assert.Equal(BlobPaths.TrackProcessed(userId, t.Id), t.ProcessedBlobName);
            Assert.Equal(BlobPaths.TrackOriginal(userId, t.Id), t.RawBlobName);
            Assert.Equal((byte)TrackProcessor.ProcessorVersion, t.ProcessorVersion);
            Assert.NotNull(t.ProcessedAt);
            Assert.True(t.DistanceMeters > 5_000);
            Assert.True(t.PointCount > 600);
            Assert.NotNull(t.StartPoint);
            Assert.NotNull(t.FinishPoint);
            Assert.NotNull(t.ElevationGainMeters);
            Assert.Equal(TrackWarnings.None, t.WarningFlags);
        });
        Assert.Equal(4, tracks.Count(t => t.Kind == GpxTrackKind.RecordedTrack));
        Assert.All(tracks.Where(t => t.Kind == GpxTrackKind.RecordedTrack), t => Assert.NotNull(t.ElapsedSeconds));
        Assert.All(tracks.Where(t => t.Kind == GpxTrackKind.PlannedCourse), t => Assert.Null(t.ElapsedSeconds));
    }

    [Fact]
    public async Task Summaries_match_the_pipeline_and_the_runs_they_belong_to_and_the_geojson_blob_is_the_cached_document()
    {
        factory.SkipIfDockerUnavailable();
        var guest = await factory.GuestSessionAsync("summary");
        var userId = guest.UserId!.Value;
        var dataset = factory.Services.GetRequiredService<DemoDatasetLoader>().Get();
        var tracks = await factory.TracksAsync(userId);
        var runs = await factory.WithDbAsync(db => db.Runs.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).AsNoTracking()
            .Where(r => r.UserId == userId).ToDictionaryAsync(r => r.Id, Ct));
        var container = factory.Services.GetRequiredService<Azure.Storage.Blobs.BlobServiceClient>().GetBlobContainerClient(BlobPaths.ContainerName);

        foreach (var t in tracks)
        {
            var file = dataset.Files.Values.Single(f => f.FileName == t.OriginalFileName);
            var expected = file.Processed.Summary;
            Assert.Equal(expected.DistanceMeters, t.DistanceMeters);
            Assert.Equal(expected.ElevationGainMeters, t.ElevationGainMeters);
            Assert.Equal(expected.ElevationLossMeters, t.ElevationLossMeters);
            Assert.Equal(expected.ElapsedSeconds, t.ElapsedSeconds);
            Assert.Equal(expected.MovingSeconds, t.MovingSeconds);
            Assert.Equal(expected.PointCount, t.PointCount);
            Assert.Equal(file.Content.Length, t.SizeBytes);

            // A completed demo run agrees with its track: distance within 1 %, gain/loss equal to the whole-metre pipeline values.
            var run = runs[t.RunId];
            if (run.Status == RunStatus.Completed)
            {
                Assert.InRange(t.DistanceMeters!.Value / run.DistanceMeters!.Value, 0.99, 1.01);
                Assert.Equal(Math.Round(t.ElevationGainMeters!.Value, MidpointRounding.AwayFromZero), run.ElevationGainMeters);
                Assert.Equal(Math.Round(t.ElevationLossMeters!.Value, MidpointRounding.AwayFromZero), run.ElevationLossMeters);
            }

            var blob = container.GetBlobClient(t.ProcessedBlobName!);
            var content = await blob.DownloadContentAsync(Ct);
            Assert.Equal("application/geo+json", content.Value.Details.ContentType);
            Assert.True(content.Value.Content.ToMemory().Span.SequenceEqual(file.GeoJson.Span));
            using var doc = JsonDocument.Parse(content.Value.Content);
            Assert.Equal("FeatureCollection", doc.RootElement.GetProperty("type").GetString());
        }
    }
}

/// <summary>The demo files are validated by the same pipeline as uploads; a rejected file fails startup instead of every guest creation.</summary>
public sealed class DemoDatasetRejectionTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    [Fact]
    public void A_demo_file_the_pipeline_rejects_fails_the_load_and_host_startup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rd-demo-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(Path.Combine(AppContext.BaseDirectory, "seed", "demo"), directory);
            File.WriteAllText(Path.Combine(directory, "gpx", "gliwice-10k.gpx"), "<?xml version=\"1.0\"?><kml xmlns=\"http://www.opengis.net/kml/2.2\"/>");

            var loader = new DemoDatasetLoader(Options.Create(new GuestOptions { DemoDataPath = directory }), TimeProvider.System);
            var ex = Assert.Throws<InvalidOperationException>(() => loader.Get());
            Assert.Contains("gliwice-10k.gpx", ex.Message, StringComparison.Ordinal);
            Assert.Contains("notGpx", ex.Message, StringComparison.Ordinal);

            using var host = factory.WithWebHostBuilder(b => b.UseSetting("Guests:DemoDataPath", directory));
            Assert.ThrowsAny<Exception>(() => host.CreateClient());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Demo_files_are_processed_once_per_process()
    {
        var loader = new DemoDatasetLoader(
            Options.Create(new GuestOptions { DemoDataPath = Path.Combine(AppContext.BaseDirectory, "seed", "demo") }), TimeProvider.System);

        var first = loader.Get();

        Assert.Equal(6, first.Files.Count);
        Assert.Same(first, loader.Get());
        Assert.All(first.Files.Values, f => Assert.Equal(TrackProcessor.ProcessorVersion, f.Processed.ProcessorVersion));
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
