using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using RunnersDiary.Infrastructure.Ai;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Features.ClientConfig;

/// <summary>Non-secret MSAL values for the SPA (ADR 0015). Client ids, the authority and the scope are visible in every login URL anyway.</summary>
public sealed record EntraClientConfig(string ClientId, string Authority, string ApiScope);

/// <summary>
/// Map settings for the SPA (ADR 0022, 0033): which provider to load, the MapLibre style URL (also the session fallback when Azure Maps
/// cannot get a token), whether place search is wired, and the Azure Maps host (null unless the provider is <c>AzureMaps</c>).
/// </summary>
public sealed record MapsClientConfig(MapProvider Provider, string? MapLibreStyleUrl, bool GeocodingEnabled, string? AzureMapsDomain);

/// <summary>GPX upload limits (anonymous endpoint, so both callers' values): the SPA picks the guest value for guest sessions.</summary>
public sealed record GpxClientConfig(long MaxFileBytesUser, long MaxFileBytesGuest);

/// <summary><c>Entra</c> is null when sign-in is not available (Entra disabled or no SPA client id); guests still work.</summary>
/// <summary>AI summary settings (anonymous endpoint, so no quota or provider details): whether a model is configured and switched on.</summary>
public sealed record AiClientConfig(bool SummaryGeneration);

public sealed record ClientConfigResponse(EntraClientConfig? Entra, MapsClientConfig Maps, GpxClientConfig Gpx, AiClientConfig Ai);

public static class GetClientConfig
{
    public static IEndpointRouteBuilder MapClientConfigEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/client-config", Handle)
            .WithName("getClientConfig")
            .WithTags("ClientConfig")
            .WithSummary("Runtime configuration for the SPA (Entra sign-in values, or null when sign-in is unavailable; map settings).")
            .Produces<ClientConfigResponse>(StatusCodes.Status200OK)
            .AllowAnonymous();

        return endpoints;
    }

    // Computed once at startup (plus whether a geocoder and an AI model are wired): no database access, no per-caller state.
    private static Microsoft.AspNetCore.Http.HttpResults.Ok<ClientConfigResponse> Handle(
        [FromServices] ClientConfigResponse config, [FromServices] IGeocoder geocoder, [FromServices] IOptionsMonitor<AiOptions> ai, HttpContext context)
    {
        context.Response.Headers.CacheControl = "public, max-age=300";
        return TypedResults.Ok(config with
        {
            Maps = config.Maps with { GeocodingEnabled = geocoder.IsAvailable },
            // Read per request so the Ai:Enabled kill switch shows up without a restart (within the 5-minute client cache).
            Ai = new AiClientConfig(ai.CurrentValue.GenerationAvailable),
        });
    }

    /// <summary>Builds the response from the API's own Entra settings; null Entra unless the scheme is enabled and a SPA client id is set.</summary>
    internal static ClientConfigResponse Build(Auth.EntraAuthOptions entra, bool entraEnabled, MapsOptions maps, GpxLimitsOptions gpxLimits)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(gpxLimits);
        // A misconfiguration fails at startup, not in the browser.
        AzureMapsOptions.EnsureValid(maps);

        var styleUrl = string.IsNullOrWhiteSpace(maps.MapLibre.StyleUrl) ? MapsOptions.DefaultStyleUrl : maps.MapLibre.StyleUrl.Trim();
        // The browser loads this URL (and the tiles it names): only absolute https is accepted, a bad value fails at startup.
        if (!Uri.TryCreate(styleUrl, UriKind.Absolute, out var parsedStyle) || parsedStyle.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Maps:MapLibre:StyleUrl must be an absolute https URL.");
        }

        var mapsConfig = new MapsClientConfig(maps.Provider, styleUrl, false, maps.Provider == MapProvider.AzureMaps ? maps.AzureMaps.Domain.Trim().ToLowerInvariant() : null);
        var gpxConfig = new GpxClientConfig(gpxLimits.MaxFileBytes, gpxLimits.GuestMaxFileBytes);
        if (!entraEnabled || string.IsNullOrWhiteSpace(entra.SpaClientId))
        {
            return new ClientConfigResponse(null, mapsConfig, gpxConfig, new AiClientConfig(false));
        }

        var scope = string.IsNullOrWhiteSpace(entra.ApiScope)
            ? $"api://{entra.Audience}/{entra.RequiredScope}"
            : entra.ApiScope;
        return new ClientConfigResponse(new EntraClientConfig(
            entra.SpaClientId.Trim(),
            $"https://{entra.TenantSubdomain}.ciamlogin.com/",
            scope), mapsConfig, gpxConfig, new AiClientConfig(false));
    }
}
