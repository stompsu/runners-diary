using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RunnersDiary.Infrastructure.Storage;

/// <summary>
/// Readiness check for blob storage that needs only what the app itself needs: read access to the <c>gpx</c> container.
/// Aspire's built-in check calls the account-level service properties, which the API identity's container-scoped
/// Storage Blob Data Contributor role does not allow (403 AuthorizationPermissionMismatch in Azure, ADR 0031).
/// </summary>
internal sealed class BlobContainerHealthCheck(BlobServiceClient service) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = service.GetBlobContainerClient(BlobPaths.ContainerName);
            // One page of at most one blob: a data-plane read inside the container, cheap whatever the container holds.
            await foreach (var _ in container.GetBlobsAsync(cancellationToken: cancellationToken).AsPages(pageSizeHint: 1))
            {
                break;
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
