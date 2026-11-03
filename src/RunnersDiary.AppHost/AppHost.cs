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

// HS256 key for guest tokens (ADR 0009 §5): generated once, persisted to the AppHost user-secrets, never in the repo.
// Letters and digits only (Aspire's special characters would be fine for UTF-8 but awkward in env files); 44 chars >= 32 bytes.
var guestKey = builder.AddParameter(
    "guest-token-signing-key",
    new GenerateParameterDefault { MinLength = 44, Special = false },
    secret: true,
    persist: true);

var api = builder.AddProject<Projects.RunnersDiary_Api>("api")
    .WithReference(db)
    .WithReference(blobs)
    // Local dev only: the API applies EF migrations at startup. Azure never sets this (ADR 0013 §7).
    .WithEnvironment("Database__MigrateOnStartup", "true")
    // Local dev only: create the private "gpx" container in Azurite. Azure never sets this (Bicep provisions it in M7).
    .WithEnvironment("Storage__CreateContainerOnStartup", "true")
    .WithEnvironment("Auth__Guest__SigningKey", guestKey)
    // The peer address is the real client locally; Azure (M7) must set true with the trusted proxy networks and hop count.
    .WithEnvironment("ForwardedHeaders__Enabled", "false")
    .WaitFor(db)
    .WaitFor(blobs)
    .WithHttpHealthCheck("/health");

// Entra External ID (docs/plans/M2-domain-runs-api.md section 10). The ids are non-secret but never committed: they come from the
// AppHost user-secrets ("Entra:*"). Without a tenant the API starts with Entra disabled (guests still work) and no prompt appears.
var entraTenantId = builder.Configuration["Entra:TenantId"];
var entraSubdomain = builder.Configuration["Entra:TenantSubdomain"];
var entraApiClientId = builder.Configuration["Entra:ApiClientId"];
if (!string.IsNullOrWhiteSpace(entraTenantId) && !string.IsNullOrWhiteSpace(entraSubdomain) && !string.IsNullOrWhiteSpace(entraApiClientId))
{
    api.WithEnvironment("Auth__Entra__Enabled", "true")
        .WithEnvironment("Auth__Entra__TenantId", entraTenantId)
        .WithEnvironment("Auth__Entra__TenantSubdomain", entraSubdomain)
        .WithEnvironment("Auth__Entra__Audience", entraApiClientId);
    if (builder.Configuration["Entra:Issuer"] is { Length: > 0 } issuer)
    {
        api.WithEnvironment("Auth__Entra__ValidIssuers__0", issuer);
    }
}
else
{
    api.WithEnvironment("Auth__Entra__Enabled", "false");
}

builder.AddViteApp("web", "../../web")
    .WithReference(api)
    // Stable browser origin so the Entra SPA redirect URI (http://localhost:5173/) is the same standalone and under Aspire.
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WaitFor(api);

await builder.Build().RunAsync();
