using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Security;

/// <summary>
/// Endpoint inventory guard (ADR 0007 §4): every endpoint must either require authorization or be on the
/// anonymous allowlist. Adding an anonymous endpoint means changing this list on purpose, in review.
/// </summary>
public sealed class EndpointAuthorizationTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    /// <summary>The only endpoints that may answer without a token. <c>/api/guest-sessions</c> carries the <c>guest-create</c> rate limit policy (asserted below).</summary>
    internal static readonly HashSet<string> AnonymousAllowlist = new(StringComparer.Ordinal)
    {
        "/alive",
        "/health",
        "/api/health",
        "/api/client-config",
        "/api/guest-sessions",
    };

    private static readonly string[] ExistingToday = ["/alive", "/health", "/api/health", "/api/client-config"];

    private static string Route(RouteEndpoint e) => "/" + e.RoutePattern.RawText?.TrimStart('/');

    private List<RouteEndpoint> Endpoints()
    {
        _ = factory.CreateClient(); // builds the host
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    }

    [Fact]
    public void AllowlistedEndpoints_ThatExist_ExplicitlyAllowAnonymous()
    {
        var endpoints = Endpoints();

        foreach (var route in AnonymousAllowlist)
        {
            foreach (var endpoint in endpoints.Where(e => Route(e) == route))
            {
                Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            }
        }

        // The three non-guest entries must exist today.
        foreach (var route in ExistingToday)
        {
            Assert.Contains(endpoints, e => Route(e) == route);
        }
    }

    [Fact]
    public void EveryOtherEndpoint_RequiresAuthorization()
    {
        var offenders = Endpoints()
            .Where(e => !AnonymousAllowlist.Contains(Route(e)))
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null || e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(e => e.DisplayName ?? e.RoutePattern.RawText)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApiEndpoints_ExceptAnonymousOnes_PassTheGeneralRateLimiter()
    {
        var offenders = Endpoints()
            .Where(e => Route(e).StartsWith("/api/", StringComparison.Ordinal) && !AnonymousAllowlist.Contains(Route(e)))
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName != "api")
            .Select(e => e.DisplayName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void GuestSessions_exists_allows_anonymous_and_carries_the_guest_create_limiter()
    {
        var endpoint = Assert.Single(Endpoints(), e => Route(e) == "/api/guest-sessions");

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IHttpMethodMetadata>().SelectMany(m => m.HttpMethods), m => m == "POST");
        Assert.Equal("guest-create", endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public void InDevelopment_only_the_openapi_document_is_added_to_the_anonymous_endpoints()
    {
        using var development = factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));
        _ = development.CreateClient();
        var endpoints = development.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var allowed = new HashSet<string>(AnonymousAllowlist, StringComparer.Ordinal) { "/openapi/{documentName}.json" };

        var anonymous = endpoints
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Route)
            .Order()
            .ToList();
        var unprotected = endpoints
            .Where(e => !allowed.Contains(Route(e)))
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null || e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(e => e.DisplayName)
            .ToList();

        Assert.Contains("/openapi/{documentName}.json", anonymous);
        Assert.Empty(unprotected);
    }

    [Fact]
    public async Task FallbackPolicy_RequiresAnAuthenticatedUser()
    {
        _ = factory.CreateClient();
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();
        var defaultPolicy = await provider.GetDefaultPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(defaultPolicy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }
}
