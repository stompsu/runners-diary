using Microsoft.AspNetCore.Mvc;

namespace RunnersDiary.Api.Features.ClientConfig;

/// <summary>Non-secret MSAL values for the SPA (ADR 0015). Client ids, the authority and the scope are visible in every login URL anyway.</summary>
public sealed record EntraClientConfig(string ClientId, string Authority, string ApiScope);

/// <summary><c>Entra</c> is null when sign-in is not available (Entra disabled or no SPA client id); guests still work.</summary>
public sealed record ClientConfigResponse(EntraClientConfig? Entra);

public static class GetClientConfig
{
    public static IEndpointRouteBuilder MapClientConfigEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/client-config", Handle)
            .WithName("getClientConfig")
            .WithTags("ClientConfig")
            .WithSummary("Runtime configuration for the SPA (Entra sign-in values, or null when sign-in is unavailable).")
            .Produces<ClientConfigResponse>(StatusCodes.Status200OK)
            .AllowAnonymous();

        return endpoints;
    }

    // Static, computed once at startup: no database access, no per-caller state.
    private static Microsoft.AspNetCore.Http.HttpResults.Ok<ClientConfigResponse> Handle([FromServices] ClientConfigResponse config, HttpContext context)
    {
        context.Response.Headers.CacheControl = "public, max-age=300";
        return TypedResults.Ok(config);
    }

    /// <summary>Builds the response from the API's own Entra settings; null Entra unless the scheme is enabled and a SPA client id is set.</summary>
    internal static ClientConfigResponse Build(Auth.EntraAuthOptions entra, bool entraEnabled)
    {
        if (!entraEnabled || string.IsNullOrWhiteSpace(entra.SpaClientId))
        {
            return new ClientConfigResponse(null);
        }

        var scope = string.IsNullOrWhiteSpace(entra.ApiScope)
            ? $"api://{entra.Audience}/{entra.RequiredScope}"
            : entra.ApiScope;
        return new ClientConfigResponse(new EntraClientConfig(
            entra.SpaClientId.Trim(),
            $"https://{entra.TenantSubdomain}.ciamlogin.com/",
            scope));
    }
}
