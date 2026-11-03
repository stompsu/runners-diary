using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;

namespace RunnersDiary.Infrastructure.Storage;

/// <summary>
/// Creates the private <c>gpx</c> container at startup. Registered only when <c>Storage:CreateContainerOnStartup=true</c>
/// (the AppHost and the test factories); in Azure the container is provisioned by Bicep (M7) and the app has no create rights.
/// </summary>
internal sealed class BlobContainerInitializer(BlobServiceClient service) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) =>
        await service.GetBlobContainerClient(BlobPaths.ContainerName).CreateIfNotExistsAsync(cancellationToken: cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
