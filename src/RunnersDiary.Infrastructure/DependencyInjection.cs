using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Insights;
using RunnersDiary.Infrastructure.Maps;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.Runs;
using RunnersDiary.Infrastructure.Storage;
using RunnersDiary.Infrastructure.Users;

namespace RunnersDiary.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Connection string names; they match the Aspire resource names in the AppHost.</summary>
    public const string DatabaseConnectionName = "runnersdiary";
    public const string BlobsConnectionName = "blobs";

    /// <summary>Configuration key that makes the host apply migrations at startup (set only by the AppHost, never in Azure).</summary>
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    /// <summary>Configuration key that makes the host create the private <c>gpx</c> container at startup (AppHost and tests only; Bicep provisions it in Azure).</summary>
    public const string CreateContainerOnStartupKey = "Storage:CreateContainerOnStartup";

    /// <summary>
    /// Registers the SQL DbContext and the Blob client from Aspire-provided connection strings.
    /// The DbContext is registered without pooling (it takes the scoped <see cref="ICurrentUser"/>, ADR 0010) and then
    /// enriched by Aspire with health checks, retries and telemetry.
    /// </summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Fail-closed default; the API replaces it with the HTTP-backed implementation.
        builder.Services.TryAddScoped<ICurrentUser>(_ => AnonymousCurrentUser.Instance);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<UserProvisioner>();

        var connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{DatabaseConnectionName}' is missing.");
        builder.Services.AddDbContext<RunnersDiaryDbContext>(options => options
            .UseSqlServer(connectionString, sql => sql.UseNetTopologySuite())
            .AddInterceptors(OwnershipGuardInterceptor.Instance));
        builder.EnrichSqlServerDbContext<RunnersDiaryDbContext>();

        if (builder.Configuration.GetValue<bool>(MigrateOnStartupKey))
        {
            builder.Services.AddHostedService<DatabaseMigrator>();
        }

        builder.AddAzureBlobServiceClient(BlobsConnectionName);
        builder.Services.AddSingleton<IBlobStore, AzureBlobStore>();
        if (builder.Configuration.GetValue<bool>(CreateContainerOnStartupKey))
        {
            builder.Services.AddHostedService<BlobContainerInitializer>();
        }

        builder.Services.Configure<GuestOptions>(builder.Configuration.GetSection(GuestOptions.SectionName));
        builder.Services.Configure<RunLimitsOptions>(builder.Configuration.GetSection(RunLimitsOptions.SectionName));
        builder.Services.Configure<GpxLimitsOptions>(builder.Configuration.GetSection(GpxLimitsOptions.SectionName));
        builder.Services.AddSingleton<TrackProcessingSlots>();
        builder.Services.AddScoped<TrackIngestionService>();
        builder.Services.AddScoped<TrackDeletionService>();

        // Azure Maps adapters are deferred to M7 (plan section 9 Q1): until then only "None" is accepted, so a wrong setting fails at startup.
        var maps = builder.Configuration.GetSection(MapsOptions.SectionName).Get<MapsOptions>() ?? new MapsOptions();
        if (maps.Geocoding.Provider != GeocodingProvider.None)
        {
            throw new InvalidOperationException($"Maps:Geocoding:Provider '{maps.Geocoding.Provider}' is not available yet; use None.");
        }

        builder.Services.TryAddSingleton<IGeocoder, NoGeocoder>();
        builder.Services.AddSingleton<DemoDatasetLoader>();
        builder.Services.AddHostedService<DemoDatasetStartupCheck>();
        builder.Services.AddSingleton<DemoDataSeeder>();
        builder.Services.AddSingleton<GuestCleanup>();
        builder.Services.AddSingleton<GuestAccountService>();
        builder.Services.AddScoped<IInsightsReportService, InsightsReportService>();
        if (builder.Configuration.GetValue("Guests:Cleanup:Enabled", true))
        {
            builder.Services.AddHostedService<GuestCleanupService>();
        }

        return builder;
    }
}
