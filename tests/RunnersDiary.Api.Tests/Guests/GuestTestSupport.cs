using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Domain.Tracks;
using RunnersDiary.Domain.Users;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Guests;

/// <summary>Direct database and blob reads for guest tests (bypassing the owner filter on purpose: tests observe, never mutate through it).</summary>
internal static class GuestTestSupport
{
    public static async Task<T> WithDbAsync<T>(this WebApplicationFactory<Program> factory, Func<RunnersDiaryDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<RunnersDiaryDbContext>());
    }

    public static Task<bool> UserExistsAsync(this WebApplicationFactory<Program> factory, Guid userId) =>
        factory.WithDbAsync(db => db.Users.AsNoTracking().AnyAsync(u => u.Id == userId, TestContext.Current.CancellationToken));

    public static Task<int> RunCountAsync(this WebApplicationFactory<Program> factory, Guid userId) =>
        factory.WithDbAsync(db => db.Runs.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).CountAsync(r => r.UserId == userId, TestContext.Current.CancellationToken));

    public static Task<List<GpxTrack>> TracksAsync(this WebApplicationFactory<Program> factory, Guid userId) =>
        factory.WithDbAsync(db => db.GpxTracks.IgnoreQueryFilters([RunnersDiaryDbContext.OwnerFilter]).AsNoTracking()
            .Where(t => t.UserId == userId).ToListAsync(TestContext.Current.CancellationToken));

    public static Task<int> ActiveGuestCountAsync(this WebApplicationFactory<Program> factory) =>
        factory.WithDbAsync(db =>
        {
            var now = DateTime.UtcNow;
            return db.Users.AsNoTracking().CountAsync(u => u.Kind == UserKind.Guest && u.ExpiresAt > now, TestContext.Current.CancellationToken);
        });

    public static async Task<List<string>> BlobNamesAsync(this WebApplicationFactory<Program> factory, string prefix)
    {
        var container = factory.Services.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(BlobPaths.ContainerName);
        var names = new List<string>();
        await foreach (var blob in container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, TestContext.Current.CancellationToken))
        {
            names.Add(blob.Name);
        }

        return names;
    }

    /// <summary>Moves a guest's expiry (what the passage of time does) so cleanup tests need no 24 h wait.</summary>
    public static Task SetGuestExpiryAsync(this WebApplicationFactory<Program> factory, Guid userId, DateTime expiresAtUtc) =>
        factory.WithDbAsync(db => db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExpiresAt, expiresAtUtc), TestContext.Current.CancellationToken));

    /// <summary>The ids of a JSON array of runs.</summary>
    public static IEnumerable<Guid> Ids(this System.Text.Json.JsonElement items) =>
        items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid());
}

/// <summary>Wraps a blob store so tests can make uploads or deletes fail and see what was written.</summary>
internal sealed class FaultyBlobStore(IBlobStore inner) : IBlobStore
{
    private int _uploads;

    public int? FailUploadsAfter { get; set; }

    public bool FailDeletes { get; set; }

    public List<string> Uploaded { get; } = [];

    public async Task UploadAsync(string blobName, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        if (FailUploadsAfter is { } limit && Interlocked.Increment(ref _uploads) > limit)
        {
            throw new IOException("Injected upload failure.");
        }

        await inner.UploadAsync(blobName, content, contentType, cancellationToken);
        lock (Uploaded)
        {
            Uploaded.Add(blobName);
        }
    }

    public Task<int> DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken) =>
        FailDeletes ? throw new IOException("Injected delete failure.") : inner.DeleteByPrefixAsync(prefix, cancellationToken);

    public Task<bool> AnyAsync(string prefix, CancellationToken cancellationToken) => inner.AnyAsync(prefix, cancellationToken);
}
