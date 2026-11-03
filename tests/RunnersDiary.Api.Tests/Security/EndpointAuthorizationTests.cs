using Microsoft.AspNetCore.Authorization;
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
    private static readonly HashSet<string> AnonymousAllowlist = new(StringComparer.Ordinal)
    {
        "/alive",
        "/health",
        "/api/health",
    };

    private List<RouteEndpoint> Endpoints()
    {
        _ = factory.CreateClient(); // builds the host
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    }

    [Fact]
    public void AllowlistedEndpoints_ExplicitlyAllowAnonymous()
    {
        var endpoints = Endpoints();

        foreach (var route in AnonymousAllowlist)
        {
            var endpoint = Assert.Single(endpoints, e => "/" + e.RoutePattern.RawText?.TrimStart('/') == route);
            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }

    [Fact]
    public void EveryOtherEndpoint_RequiresAuthorization()
    {
        var offenders = Endpoints()
            .Where(e => !AnonymousAllowlist.Contains("/" + e.RoutePattern.RawText?.TrimStart('/')))
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null || e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(e => e.DisplayName ?? e.RoutePattern.RawText)
            .ToList();

        Assert.Empty(offenders);
    }
}
