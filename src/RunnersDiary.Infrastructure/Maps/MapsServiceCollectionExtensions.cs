using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace RunnersDiary.Infrastructure.Maps;

public static class MapsServiceCollectionExtensions
{
    /// <summary>Key of the credential the Azure Maps adapters use; tests register a fake under it first.</summary>
    public const string CredentialKey = "azure-maps";

    /// <summary>
    /// Registers the geocoder, the SAS issuer and the geocode budget for the configured providers. Misconfiguration fails here, at
    /// startup, naming keys only. Defaults (<c>None</c> and <c>MapLibre</c>) register the inert implementations.
    /// </summary>
    public static IServiceCollection AddMaps(this IServiceCollection services, MapsOptions maps)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(maps);
        AzureMapsOptions.EnsureValid(maps);

        services.AddSingleton(maps);
        services.AddSingleton(maps.AzureMaps);
        services.AddSingleton<GeocodeBudget>();
        services.TryAddKeyedSingleton<TokenCredential>(CredentialKey, (_, _) => CreateCredential(maps.AzureMaps));

        // Plain HttpClients, not IHttpClientFactory ones: the default client logging would write the request URL (the search text), and the
        // standard resilience handler would retry a billable call. One attempt under a timeout is the policy (ADR 0033).
        if (maps.Geocoding.Provider == GeocodingProvider.AzureMaps)
        {
            services.AddSingleton<IGeocoder>(sp => new AzureMapsGeocoder(
                CreateHttpClient($"https://{maps.AzureMaps.Domain}/", maps.AzureMaps.GeocodeTimeout, 512 * 1024),
                sp.GetRequiredKeyedService<TokenCredential>(CredentialKey),
                maps.AzureMaps,
                sp.GetRequiredService<ILogger<AzureMapsGeocoder>>()));
        }
        else
        {
            services.TryAddSingleton<IGeocoder, NoGeocoder>();
        }

        if (maps.Provider == MapProvider.AzureMaps)
        {
            services.AddSingleton<IMapsTokenIssuer>(sp => new AzureMapsSasTokenIssuer(
                CreateHttpClient("https://management.azure.com/", TimeSpan.FromSeconds(15), 64 * 1024),
                sp.GetRequiredKeyedService<TokenCredential>(CredentialKey),
                maps.AzureMaps,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<AzureMapsSasTokenIssuer>>()));
        }
        else
        {
            services.TryAddSingleton<IMapsTokenIssuer, DisabledMapsTokenIssuer>();
        }

        return services;
    }

    private static HttpClient CreateHttpClient(string baseAddress, TimeSpan timeout, long maxResponseBytes) =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = timeout,
            MaxResponseContentBufferSize = maxResponseBytes,
        };

    /// <summary>The same credential rules as the Foundry client: the API's user-assigned identity in Azure, developer sign-in locally.</summary>
    internal static TokenCredential CreateCredential(AzureMapsOptions options) =>
        string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true })
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId));
}
