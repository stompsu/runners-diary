using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RunnersDiary.Api.Tests.Infrastructure;
using RunnersDiary.Infrastructure.Storage;

namespace RunnersDiary.Api.Tests.Health;

// In Azure the API identity holds Storage Blob Data Contributor on the gpx container only (ADR 0031), so the blob
// readiness check must not touch account-level APIs (Aspire's built-in check did: 403 AuthorizationPermissionMismatch).
[Collection(ContainersFixtureGroup.Name)]
public sealed class BlobContainerHealthCheckTests(ContainersApiFactory factory)
{
    [Fact]
    public void Only_the_container_scoped_blob_check_is_registered()
    {
        factory.SkipIfDockerUnavailable();
        var registrations = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Contains(registrations, r => r.Name == "blobs" && !r.Tags.Contains("live"));
        Assert.DoesNotContain(registrations, r => r.Name.Contains("Blob", StringComparison.OrdinalIgnoreCase) && r.Name != "blobs");
    }

    [Fact]
    public async Task Healthy_when_the_gpx_container_can_be_read()
    {
        factory.SkipIfDockerUnavailable();
        var check = new BlobContainerHealthCheck(factory.Services.GetRequiredService<BlobServiceClient>());

        var result = await check.CheckHealthAsync(Context(check), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Unhealthy_when_the_container_is_missing()
    {
        factory.SkipIfDockerUnavailable();
        var service = factory.Services.GetRequiredService<BlobServiceClient>();
        // Same account, an account the check has no container in: a client whose gpx container does not exist.
        var otherAccount = new BlobServiceClient(new Uri(service.Uri, "../nosuchaccount"));
        var check = new BlobContainerHealthCheck(otherAccount);

        var result = await check.CheckHealthAsync(Context(check), TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    private static HealthCheckContext Context(IHealthCheck check) =>
        new() { Registration = new HealthCheckRegistration("blobs", check, HealthStatus.Unhealthy, tags: null) };
}
