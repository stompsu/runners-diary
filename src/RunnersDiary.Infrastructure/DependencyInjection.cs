using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RunnersDiary.Infrastructure.Ai;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Guests;
using RunnersDiary.Infrastructure.Identity;
using RunnersDiary.Infrastructure.Insights;
using RunnersDiary.Infrastructure.Maps;
using RunnersDiary.Infrastructure.Persistence;
using RunnersDiary.Infrastructure.RaceSources;
using RunnersDiary.Infrastructure.RaceSources.Demo;
using RunnersDiary.Infrastructure.RaceSources.RunningLife;
using RunnersDiary.Infrastructure.Races;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    /// <summary>Configuration key that makes the host upsert the synthetic demo race catalogue at startup (AppHost and tests only; the Production validator rejects it).</summary>
    public const string DemoCatalogueSeedOnStartupKey = "Races:DemoCatalogue:SeedOnStartup";

    /// <summary>Configuration key that makes the host create the private <c>gpx</c> container at startup (AppHost and tests only; Bicep provisions it in Azure).</summary>
    public const string CreateContainerOnStartupKey = "Storage:CreateContainerOnStartup";

    /// <summary>
    /// Registers the SQL DbContext and the Blob client from Aspire-provided connection strings.
    /// The DbContext is registered without pooling (it takes the scoped <see cref="ICurrentUser"/>, ADR 0010) and then
    /// enriched by Aspire with health checks, retries and telemetry.
    /// </summary>
    /// <param name="jobMode">
    /// A one-shot job host (<c>--job guest-cleanup</c>, ADR 0032 §2): no startup work and no background services, only the services a
    /// job resolves explicitly.
    /// </param>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder, bool jobMode = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Fail-closed default; the API replaces it with the HTTP-backed implementation.
        builder.Services.TryAddScoped<ICurrentUser>(_ => AnonymousCurrentUser.Instance);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<UserProvisioner>();

        var connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{DatabaseConnectionName}' is missing.");
        // Counts opened connections for the guest-cleanup idle-skip (ADR 0032 §2).
        builder.Services.AddSingleton<SqlActivityInterceptor>();
        builder.Services.AddDbContext<RunnersDiaryDbContext>((services, options) => options
            .UseSqlServer(connectionString, sql => sql.UseNetTopologySuite())
            .AddInterceptors(OwnershipGuardInterceptor.Instance, services.GetRequiredService<SqlActivityInterceptor>()));
        builder.EnrichSqlServerDbContext<RunnersDiaryDbContext>();

        if (!jobMode && builder.Configuration.GetValue<bool>(MigrateOnStartupKey))
        {
            builder.Services.AddHostedService<DatabaseMigrator>();
        }

        // Aspire's own check reads account-level properties, which the container-scoped role (ADR 0031) does not allow.
        builder.AddAzureBlobServiceClient(BlobsConnectionName, settings => settings.DisableHealthChecks = true);
        builder.Services.AddHealthChecks().AddCheck<BlobContainerHealthCheck>("blobs");
        builder.Services.AddSingleton<IBlobStore, AzureBlobStore>();
        if (!jobMode && builder.Configuration.GetValue<bool>(CreateContainerOnStartupKey))
        {
            builder.Services.AddHostedService<BlobContainerInitializer>();
        }

        builder.Services.Configure<GuestOptions>(builder.Configuration.GetSection(GuestOptions.SectionName));
        builder.Services.Configure<RunLimitsOptions>(builder.Configuration.GetSection(RunLimitsOptions.SectionName));
        builder.Services.Configure<GpxLimitsOptions>(builder.Configuration.GetSection(GpxLimitsOptions.SectionName));
        builder.Services.AddSingleton<TrackProcessingSlots>();
        builder.Services.AddScoped<TrackIngestionService>();
        builder.Services.AddScoped<TrackDeletionService>();

        // Geocoder, SAS issuer and geocode budget for the configured providers (M7, ADR 0033); an invalid setup fails here.
        builder.Services.AddMaps(builder.Configuration.GetSection(MapsOptions.SectionName).Get<MapsOptions>() ?? new MapsOptions());
        builder.Services.AddSingleton<DemoDatasetLoader>();
        if (!jobMode)
        {
            builder.Services.AddHostedService<DemoDatasetStartupCheck>();
        }

        builder.Services.AddSingleton<DemoDataSeeder>();

        // Race catalogue (M8): options, the synthetic demo catalogue and, in Development/Testing only, its idempotent seeding after the migrator.
        // The Production configuration validator rejects SeedOnStartup, so this hosted service never runs in Azure.
        builder.Services.Configure<RacesOptions>(builder.Configuration.GetSection(RacesOptions.SectionName));
        builder.Services.AddSingleton<DemoRaceCatalogue>();
        builder.Services.AddScoped<RaceProposalService>();
        builder.Services.AddScoped<RaceSignupService>();
        if (!jobMode && builder.Configuration.GetValue<bool>(DemoCatalogueSeedOnStartupKey))
        {
            builder.Services.AddHostedService<DemoRaceCatalogueSeeder>();
        }

        builder.Services.AddSingleton<GuestCleanup>();
        builder.Services.AddSingleton<IGuestCleanupPass>(services => services.GetRequiredService<GuestCleanup>());
        builder.Services.AddSingleton<GuestAccountService>();
        builder.Services.AddScoped<IInsightsReportService, InsightsReportService>();
        builder.Services.AddScoped<IAiGenerationStore, EfAiGenerationStore>();
        builder.Services.AddScoped<IAiSummaryStore, EfAiSummaryStore>();
        builder.AddTrainingInsights(runStartupChecks: !jobMode);
        if (!jobMode && builder.Configuration.GetValue("Guests:Cleanup:Enabled", true))
        {
            builder.Services.AddHostedService<GuestCleanupService>();
        }

        return builder;
    }

    /// <summary>
    /// The <c>--job race-import</c> profile (ADR 0038 §1): the DbContext (no pooling, anonymous user), the race catalogue services and the sources, nothing
    /// else. There is no Blob, Maps, AI, authentication or guest service, no hosted service and no startup work, and the connection string of the
    /// database is the only one required. A disabled import never opens it (the service checks the switches first).
    /// </summary>
    public static IHostApplicationBuilder AddRaceImportJob(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddScoped<ICurrentUser>(_ => AnonymousCurrentUser.Instance);
        builder.Services.TryAddSingleton(TimeProvider.System);

        var connectionString = builder.Configuration.GetConnectionString(DatabaseConnectionName)
            ?? throw new InvalidOperationException($"Connection string '{DatabaseConnectionName}' is missing.");
        builder.Services.AddSingleton<SqlActivityInterceptor>();
        builder.Services.AddDbContext<RunnersDiaryDbContext>((services, options) => options
            .UseSqlServer(connectionString, sql => sql.UseNetTopologySuite())
            .AddInterceptors(OwnershipGuardInterceptor.Instance, services.GetRequiredService<SqlActivityInterceptor>()));
        builder.EnrichSqlServerDbContext<RunnersDiaryDbContext>();

        builder.Services.Configure<RacesOptions>(builder.Configuration.GetSection(RacesOptions.SectionName));
        builder.Services.AddSingleton<DemoRaceCatalogue>();

        builder.Services.AddOptions<RaceImportOptions>().Bind(builder.Configuration.GetSection(RaceImportOptions.SectionName)).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<RaceImportOptions>, RaceImportOptionsValidator>();
        builder.Services.AddSingleton<IRaceSource>(sp => new RunningLifeSource(
            CreateRaceSourceHttpClient(), sp.GetRequiredService<IOptions<RaceImportOptions>>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<RunningLifeSource>>()));
        builder.Services.AddSingleton<IRaceSource, DemoRaceSource>();
        builder.Services.AddSingleton<RaceImportService>();
        return builder;
    }

    /// <summary>
    /// A plain client, not an <c>IHttpClientFactory</c> one: the standard resilience handler of the service defaults would retry (the polite client never retries,
    /// ADR 0035 §4) and the default client logging would write URLs. Redirects are followed by hand, only to the same host; decompression is on, the cap is
    /// applied to the decompressed bytes and the timeout by the polite client.
    /// </summary>
    private static HttpClient CreateRaceSourceHttpClient() =>
        new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Brotli,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
}
