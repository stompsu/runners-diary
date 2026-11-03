using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.Security;

/// <summary>
/// Calls EVERY mapped endpoint without a token (AC3). New endpoints are covered automatically; the allowlist is the only way out.
/// Runs offline: authentication fails before any database access.
/// </summary>
public sealed class UnauthenticatedSweepTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    public static TheoryData<string, string> NonAllowlistedEndpoints()
    {
        using var factory = new OfflineApiFactory();
        _ = factory.CreateClient();
        var data = new TheoryData<string, string>();
        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var route = "/" + endpoint.RoutePattern.RawText?.TrimStart('/');
            if (EndpointAuthorizationTests.AnonymousAllowlist.Contains(route))
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            foreach (var method in methods)
            {
                data.Add(method, Fill(endpoint.RoutePattern));
            }
        }

        return data;
    }

    private static string Fill(RoutePattern pattern)
    {
        var path = pattern.RawText ?? string.Empty;
        foreach (var parameter in pattern.Parameters)
        {
            var value = parameter.Name switch
            {
                "year" => "2026",
                "month" => "11",
                _ => Guid.NewGuid().ToString(),
            };
            path = System.Text.RegularExpressions.Regex.Replace(path, "\\{" + parameter.Name + "[^}]*\\}", value);
        }

        return "/" + path.TrimStart('/');
    }

    [Fact]
    public void Sweep_CoversTheKnownApiEndpoints()
    {
        var data = NonAllowlistedEndpoints().Select(d => d.Data).ToList();
        Assert.True(data.Count >= 9, $"Expected at least 9 protected endpoint/method pairs, found {data.Count}.");
    }

    [Theory]
    [MemberData(nameof(NonAllowlistedEndpoints))]
    public async Task Endpoint_WithoutToken_Returns401Problem(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(new { });
        }

        var response = await factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task GarbageBearerToken_Returns401()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer not-a-jwt");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
