using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc.Testing;
using DotNet.Testcontainers.Builders;
using Testcontainers.Azurite;
using Testcontainers.MsSql;

namespace RunnersDiary.Api.Tests.Infrastructure;

/// <summary>Hosts the API against real SQL Server and Azurite containers (requires Docker).</summary>
public sealed class ContainersApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private MsSqlContainer? _sql;
    private AzuriteContainer? _azurite;

    private string? _skipReason;

    public async ValueTask InitializeAsync()
    {
        try
        {
            // Built here, not in field initializers: Build() itself can throw when Docker is missing.
            // Same images as the AppHost resources (src/RunnersDiary.AppHost/AppHost.cs), so tests and local dev run the same engines.
            _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
                .WithInMemoryPersistence()
                .Build();
            await Task.WhenAll(_sql.StartAsync(), _azurite.StartAsync());
        }
        catch (Exception ex) when (!IsCi && ex is DockerUnavailableException or AggregateException { InnerException: DockerUnavailableException })
        {
            // Locally, missing Docker skips these tests with a clear message. In CI it must fail.
            _skipReason = $"Docker is not available, so container-backed tests are skipped: {ex.Message}";
        }
    }

    /// <summary>Connection string of the SQL container (server level; callers pick their own database name).</summary>
    public string SqlConnectionString =>
        (_sql ?? throw new InvalidOperationException("Containers are not running; call SkipIfDockerUnavailable() first.")).GetConnectionString();

    private static bool IsCi => string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Call at the start of each test that needs the containers.</summary>
    public void SkipIfDockerUnavailable() => Assert.SkipWhen(_skipReason is not null, _skipReason ?? string.Empty);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        if (_sql is null || _azurite is null)
        {
            throw new InvalidOperationException("Containers are not running; call SkipIfDockerUnavailable() first.");
        }

        // A dedicated database; the host applies the real migrations at startup (what the AppHost does locally).
        var apiDatabase = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "runnersdiary_api_tests" };
        builder.UseSetting("ConnectionStrings:runnersdiary", apiDatabase.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        TestHost.ConfigureAuth(builder, entraEnabled: true);
        builder.UseSetting("ConnectionStrings:blobs", _azurite.GetConnectionString());
        // The "gpx" container does not exist in a fresh Azurite; the host creates it (as in the AppHost). Azure never sets this.
        builder.UseSetting("Storage:CreateContainerOnStartup", "true");
        // Functional tests create many guests from one (absent) client IP and own the expiry timeline: no background cleanup, no
        // creation limit. Dedicated tests switch these back on through WithWebHostBuilder.
        builder.UseSetting("Guests:Cleanup:Enabled", "false");
        // Race proposals (M8): the feature is on. This host runs as Production, whose configuration validator rejects the demo catalogue
        // seeding switch, so tests that need the synthetic catalogue seed it explicitly (RaceTestSupport.EnsureDemoCatalogueAsync).
        builder.UseSetting("Races:Enabled", "true");
        builder.UseSetting("RateLimiting:GuestCreate:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:GuestCreate:GlobalPermitLimit", "100000");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_sql is not null)
        {
            await _sql.DisposeAsync();
        }

        if (_azurite is not null)
        {
            await _azurite.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ContainersFixtureGroup : ICollectionFixture<ContainersApiFactory>
{
    public const string Name = "Containers";
}
