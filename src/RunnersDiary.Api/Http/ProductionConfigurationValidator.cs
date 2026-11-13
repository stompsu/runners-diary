using System.Net;
using RunnersDiary.Infrastructure;
using RunnersDiary.Infrastructure.RaceSources;

namespace RunnersDiary.Api.Http;

/// <summary>
/// Fail-fast checks for settings that only exist in a deployed environment (M7 plan §5.2), so a missing or malformed value stops the
/// host at startup instead of surfacing as a misleading runtime behaviour (every client sharing one rate-limit partition, a
/// blob call against nothing). Production only: Development and tests keep their relaxed defaults. Messages name keys, never values.
/// Settings that already validate themselves elsewhere (Entra identifiers, <c>Auth:Guest:SigningKey</c>, <c>Ai:*</c>,
/// <c>ForwardedHeaders:Enabled</c> being explicit) are not repeated here.
/// </summary>
public static class ProductionConfigurationValidator
{
    /// <summary>The API process: connection strings plus a coherent forwarded-headers trust configuration.</summary>
    public static void ValidateApi(IConfiguration configuration, IHostEnvironment environment) =>
        Validate(configuration, environment, includeWebSettings: true, requireBlobs: true);

    /// <summary>The job host: only what Infrastructure needs.</summary>
    public static void ValidateJob(IConfiguration configuration, IHostEnvironment environment) =>
        Validate(configuration, environment, includeWebSettings: false, requireBlobs: true);

    /// <summary>The job host of <paramref name="job"/>: the race import (ADR 0038) needs no Blob connection string, every other job keeps the full set.</summary>
    public static void ValidateJob(IConfiguration configuration, IHostEnvironment environment, string job) =>
        Validate(configuration, environment, includeWebSettings: false, requireBlobs: !string.Equals(job, Jobs.JobMode.RaceImportJob, StringComparison.Ordinal));

    private static void Validate(IConfiguration configuration, IHostEnvironment environment, bool includeWebSettings, bool requireBlobs)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsProduction())
        {
            return;
        }

        var failures = new List<string>();
        Require(configuration, $"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}", failures);
        if (requireBlobs)
        {
            Require(configuration, $"ConnectionStrings:{DependencyInjection.BlobsConnectionName}", failures);
        }

        // The synthetic demo catalogue (M8) is Development/Testing data and must never be seeded into production.
        if (configuration.GetValue<bool>(DependencyInjection.DemoCatalogueSeedOnStartupKey))
        {
            failures.Add($"{DependencyInjection.DemoCatalogueSeedOnStartupKey} must not be true in Production.");
        }

        ValidateRaceImport(configuration, failures);

        if (includeWebSettings)
        {
            ValidateForwardedHeaders(configuration, failures);
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException("Invalid production configuration: " + string.Join(" ", failures));
        }
    }

    /// <summary>The synthetic Demo source must never run in Production, and the polite-crawling floor (ADR 0035 §4) is enforced before the job starts.</summary>
    private static void ValidateRaceImport(IConfiguration configuration, List<string> failures)
    {
        if (configuration.GetValue<bool>("RaceImport:Sources:Demo:Enabled"))
        {
            failures.Add("RaceImport:Sources:Demo:Enabled must not be true in Production.");
        }

        var interval = configuration["RaceImport:MinInterval"];
        if (!string.IsNullOrWhiteSpace(interval)
            && !(TimeSpan.TryParse(interval, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= RaceImportOptions.MinimumInterval))
        {
            failures.Add("RaceImport:MinInterval must be a time span of at least one second.");
        }
    }

    private static void Require(IConfiguration configuration, string key, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            failures.Add($"{key} is required.");
        }
    }

    /// <summary>
    /// Behind the web tier the proxy chain is web (nginx) then the Container Apps ingress (ADR 0029 section 3): trusting forwarded
    /// headers needs a sane hop limit and parseable trusted networks (an unset list keeps the documented loopback-only default, so
    /// deployments behind the web tier list their networks explicitly; a malformed entry would otherwise fail on first use).
    /// </summary>
    private static void ValidateForwardedHeaders(IConfiguration configuration, List<string> failures)
    {
        if (!bool.TryParse(configuration[ForwardedHeadersSetup.EnabledKey], out var enabled) || !enabled)
        {
            return;
        }

        var limit = configuration["ForwardedHeaders:ForwardLimit"];
        if (!string.IsNullOrWhiteSpace(limit) && !(int.TryParse(limit, out var parsed) && parsed >= 1))
        {
            failures.Add("ForwardedHeaders:ForwardLimit must be a positive integer.");
        }

        var networks = configuration.GetSection("ForwardedHeaders:KnownIPNetworks").Get<string[]>() ?? [];
        foreach (var cidr in networks)
        {
            if (!IPNetwork.TryParse(cidr, out _))
            {
                failures.Add("ForwardedHeaders:KnownIPNetworks contains an entry that is not a CIDR network.");
                break;
            }
        }
    }
}
