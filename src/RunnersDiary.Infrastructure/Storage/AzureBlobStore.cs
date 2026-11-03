using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace RunnersDiary.Infrastructure.Storage;

/// <summary>Blob store over the Aspire-provided <see cref="BlobServiceClient"/> (Azurite locally, a storage account in Azure).</summary>
public sealed class AzureBlobStore(BlobServiceClient service) : IBlobStore
{
    private BlobContainerClient Container => service.GetBlobContainerClient(BlobPaths.ContainerName);

    public async Task UploadAsync(string blobName, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        var blob = Container.GetBlobClient(blobName);
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        await blob.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    public async Task<int> DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        var deleted = 0;
        try
        {
            await foreach (var item in Container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
            {
                if ((await Container.DeleteBlobIfExistsAsync(item.Name, cancellationToken: cancellationToken)).Value)
                {
                    deleted++;
                }
            }
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
        {
            // Nothing was ever uploaded (the container does not exist yet): nothing to delete.
        }

        return deleted;
    }

    public async Task<bool> AnyAsync(string prefix, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in Container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
            {
                return true;
            }
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.ContainerNotFound)
        {
            // See DeleteByPrefixAsync.
        }

        return false;
    }
}
