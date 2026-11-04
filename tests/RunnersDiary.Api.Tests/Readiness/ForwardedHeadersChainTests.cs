using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Http;

namespace RunnersDiary.Api.Tests.Readiness;

/// <summary>
/// ADR 0029 section 3 / M7 plan section 5.2: behind nginx (which keeps one X-Forwarded-For value) and the Container Apps ingress
/// (which appends the peer) the API sees two entries and must trust exactly two hops, from any address (the networks are the
/// environment's private ranges, unknown in advance).
/// </summary>
public sealed class ForwardedHeadersChainTests
{
    private static async Task<string> ClientIpAsync(string peer, string forwardedFor)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:ForwardLimit"] = "2",
            ["ForwardedHeaders:KnownIPNetworks:0"] = "0.0.0.0/0",
            ["ForwardedHeaders:KnownIPNetworks:1"] = "::/0",
        });
        Assert.True(builder.Services.AddConfiguredForwardedHeaders(builder.Configuration, relaxed: false));

        await using var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            return next(context);
        });
        app.UseForwardedHeaders();
        app.MapGet("/ip", (HttpContext context) => context.Connection.RemoteIpAddress?.ToString());
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ip");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_client_is_the_second_entry_from_the_right_and_a_spoofed_leading_entry_is_ignored()
    {
        // client 203.0.113.9 -> nginx (10.0.0.4, normalises to one value) -> ingress (10.0.0.5 = peer); a client-sent "1.1.1.1" is in front.
        Assert.Equal("203.0.113.9", await ClientIpAsync("10.0.0.5", "1.1.1.1, 203.0.113.9, 10.0.0.4"));
    }

    [Fact]
    public async Task The_normal_two_entry_chain_resolves_to_the_client()
    {
        Assert.Equal("203.0.113.9", await ClientIpAsync("10.0.0.5", "203.0.113.9, 10.0.0.4"));
    }

    [Fact]
    public async Task Ipv6_clients_and_peers_are_trusted_too()
    {
        Assert.Equal("2001:db8::9", await ClientIpAsync("fd00::5", "2001:db8::9, fd00::4"));
    }
}
