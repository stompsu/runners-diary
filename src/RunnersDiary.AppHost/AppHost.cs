var builder = DistributedApplication.CreateBuilder(args);

// Resource names are contracts: they become connection string names in the API (docs/plans/M1-skeleton.md §3.1).
var sql = builder.AddSqlServer("sql")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var db = sql.AddDatabase("runnersdiary");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator
        // Aspire 13.6.4 defaults to Azurite 3.35.0, which rejects the Storage SDK's API version 2026-06-06.
        // Keep in sync with tests/RunnersDiary.Api.Tests/Infrastructure/ContainersApiFactory.cs; Dependabot does not bump
        // these tags. Remove both pins once the Aspire default image is >= 3.37.0.
        .WithImageTag("3.37.0")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent));
var blobs = storage.AddBlobs("blobs");

var api = builder.AddProject<Projects.RunnersDiary_Api>("api")
    .WithReference(db)
    .WithReference(blobs)
    .WaitFor(db)
    .WaitFor(blobs)
    .WithHttpHealthCheck("/health");

builder.AddViteApp("web", "../../web")
    .WithReference(api)
    .WaitFor(api);

await builder.Build().RunAsync();
