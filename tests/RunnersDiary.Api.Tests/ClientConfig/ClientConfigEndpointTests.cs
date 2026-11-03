using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using RunnersDiary.Api.Features.ClientConfig;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.ClientConfig;

/// <summary>GET /api/client-config (ADR 0015): anonymous, cacheable, no database; never needs Docker.</summary>
public sealed class ClientConfigEndpointTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    private static readonly Uri Url = new("/api/client-config", UriKind.Relative);

    private static WebApplicationFactory<Program> WithEntra(OfflineApiFactory f, string? spaClientId, string? apiScope = null) =>
        f.WithWebHostBuilder(b =>
        {
            TestHost.ConfigureAuth(b, entraEnabled: true);
            if (spaClientId is not null)
            {
                b.UseSetting("Auth:Entra:SpaClientId", spaClientId);
            }

            if (apiScope is not null)
            {
                b.UseSetting("Auth:Entra:ApiScope", apiScope);
            }
        });

    [Fact]
    public async Task Entra_disabled_returns_null_entra_anonymously_with_cache_header()
    {
        var response = await factory.CreateClient().GetAsync(Url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
        Assert.Contains("\"entra\":null", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Entra_enabled_with_spa_client_id_returns_derived_values()
    {
        using var entra = WithEntra(factory, "spa-client-id");

        var response = await entra.CreateClient().GetAsync(Url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<ClientConfigResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body?.Entra);
        Assert.Equal("spa-client-id", body.Entra.ClientId);
        Assert.Equal($"https://{TestTokens.TenantSubdomain}.ciamlogin.com/", body.Entra.Authority);
        Assert.Equal($"api://{TestTokens.ApiAudience}/access_as_user", body.Entra.ApiScope);
    }

    [Fact]
    public async Task Api_scope_override_is_returned()
    {
        using var entra = WithEntra(factory, "spa-client-id", "api://custom/scope");

        var body = await entra.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, TestContext.Current.CancellationToken);

        Assert.Equal("api://custom/scope", body?.Entra?.ApiScope);
    }

    [Fact]
    public async Task Entra_enabled_without_spa_client_id_returns_null_entra()
    {
        using var entra = WithEntra(factory, spaClientId: null);

        var response = await entra.CreateClient().GetAsync(Url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<ClientConfigResponse>(TestContext.Current.CancellationToken))?.Entra);
    }
}
