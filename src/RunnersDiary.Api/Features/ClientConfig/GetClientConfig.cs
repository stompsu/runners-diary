using Microsoft.AspNetCore.Mvc;
using RunnersDiary.Infrastructure.Gpx;
using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Features.ClientConfig;

/// <summary>Non-secret MSAL values for the SPA (ADR 0015). Client ids, the authority and the scope are visible in every login URL anyway.</summary>
public sealed record EntraClientConfig(string ClientId, string Authority, string ApiScope);

/// <summary>Map settings for the SPA (ADR 0022): which provider to load, the MapLibre style URL, and whether place search is wired.</summary>
public sealed record MapsClientConfig(MapProvider Provider, string? MapLibreStyleUrl, bool GeocodingEnabled);

/// <summary>GPX upload limits (anonymous endpoint, so both callers' values): the SPA picks the guest value for guest sessions.</summary>
public sealed record GpxClientConfig(long MaxFileBytesUser, long MaxFileBytesGuest);

/// <summary><c>Entra</c> is null when sign-in is not available (Entra disabled or no SPA client id); guests still work.</summary>
public sealed record ClientConfigResponse(EntraClientConfig? Entra, MapsClientConfig Maps, GpxClientConfig Gpx);

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

    // Computed once at startup (plus whether a geocoder is wired): no database access, no per-caller state.
    private static Microsoft.AspNetCore.Http.HttpResults.Ok<ClientConfigResponse> Handle(
        [FromServices] ClientConfigResponse config, [FromServices] IGeocoder geocoder, HttpContext context)
    {
        context.Response.Headers.CacheControl = "public, max-age=300";
        return TypedResults.Ok(config with { Maps = config.Maps with { GeocodingEnabled = geocoder.IsAvailable } });
    }

    /// <summary>Builds the response from the API's own Entra settings; null Entra unless the scheme is enabled and a SPA client id is set.</summary>
    internal static ClientConfigResponse Build(Auth.EntraAuthOptions entra, bool entraEnabled, MapsOptions maps, GpxLimitsOptions gpxLimits)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(gpxLimits);
        // The Azure Maps provider is deferred to M7 (plan section 9 Q1): a misconfiguration fails at startup, not in the browser.
        if (maps.Provider != MapProvider.MapLibre)
        {
            throw new InvalidOperationException($"Maps:Provider '{maps.Provider}' is not available yet; use MapLibre.");
        }

        var styleUrl = string.IsNullOrWhiteSpace(maps.MapLibre.StyleUrl) ? MapsOptions.DefaultStyleUrl : maps.MapLibre.StyleUrl.Trim();
        // The browser loads this URL (and the tiles it names): only absolute https is accepted, a bad value fails at startup.
        if (!Uri.TryCreate(styleUrl, UriKind.Absolute, out var parsedStyle) || parsedStyle.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Maps:MapLibre:StyleUrl must be an absolute https URL.");
        }

        var mapsConfig = new MapsClientConfig(maps.Provider, styleUrl, false);
        var gpxConfig = new GpxClientConfig(gpxLimits.MaxFileBytes, gpxLimits.GuestMaxFileBytes);
        if (!entraEnabled || string.IsNullOrWhiteSpace(entra.SpaClientId))
        {
            return new ClientConfigResponse(null, mapsConfig, gpxConfig);
        }

        var scope = string.IsNullOrWhiteSpace(entra.ApiScope)
            ? $"api://{entra.Audience}/{entra.RequiredScope}"
            : entra.ApiScope;
        return new ClientConfigResponse(new EntraClientConfig(
            entra.SpaClientId.Trim(),
            $"https://{entra.TenantSubdomain}.ciamlogin.com/",
            scope), mapsConfig, gpxConfig);
    }
}
