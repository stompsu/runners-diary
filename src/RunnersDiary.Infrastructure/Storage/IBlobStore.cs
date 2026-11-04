namespace RunnersDiary.Infrastructure.Storage;

/// <summary>The blob operations the API needs. Names are relative to the <see cref="BlobPaths.ContainerName"/> container.</summary>
public interface IBlobStore
{
    Task UploadAsync(string blobName, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    /// <summary>Opens a blob for streaming; null when it does not exist (or the container does not). The caller disposes the stream.</summary>
    Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>Deletes every blob whose name starts with <paramref name="prefix"/>; returns how many were removed. A missing container counts as empty.</summary>
    Task<int> DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>True when at least one blob starts with <paramref name="prefix"/>.</summary>
    Task<bool> AnyAsync(string prefix, CancellationToken cancellationToken);
}
