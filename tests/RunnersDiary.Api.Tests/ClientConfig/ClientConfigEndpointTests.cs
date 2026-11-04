using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RunnersDiary.Api.Features.ClientConfig;
using RunnersDiary.Api.Tests.Infrastructure;

namespace RunnersDiary.Api.Tests.ClientConfig;

/// <summary>GET /api/client-config (ADR 0015): anonymous, cacheable, no database; never needs Docker.</summary>
public sealed class ClientConfigEndpointTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    private static readonly Uri Url = new("/api/client-config", UriKind.Relative);

    // The API writes enums by name (JsonSetup); the default test options would not read them back.
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

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
        var body = await response.Content.ReadFromJsonAsync<ClientConfigResponse>(Json, TestContext.Current.CancellationToken);
        Assert.NotNull(body?.Entra);
        Assert.Equal("spa-client-id", body.Entra.ClientId);
        Assert.Equal($"https://{TestTokens.TenantSubdomain}.ciamlogin.com/", body.Entra.Authority);
        Assert.Equal($"api://{TestTokens.ApiAudience}/access_as_user", body.Entra.ApiScope);
    }

    [Fact]
    public async Task Api_scope_override_is_returned()
    {
        using var entra = WithEntra(factory, "spa-client-id", "api://custom/scope");

        var body = await entra.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, Json, TestContext.Current.CancellationToken);

        Assert.Equal("api://custom/scope", body?.Entra?.ApiScope);
    }

    [Fact]
    public async Task Entra_enabled_without_spa_client_id_returns_null_entra()
    {
        using var entra = WithEntra(factory, spaClientId: null);

        var response = await entra.CreateClient().GetAsync(Url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<ClientConfigResponse>(Json, TestContext.Current.CancellationToken))?.Entra);
    }

    [Fact]
    public async Task Maps_defaults_to_maplibre_with_the_openfreemap_style_and_no_geocoding()
    {
        var response = await factory.CreateClient().GetAsync(Url, TestContext.Current.CancellationToken);

        var maps = (await response.Content.ReadFromJsonAsync<ClientConfigResponse>(Json, TestContext.Current.CancellationToken))!.Maps;
        Assert.Equal(RunnersDiary.Infrastructure.Maps.MapProvider.MapLibre, maps.Provider);
        Assert.Equal("https://tiles.openfreemap.org/styles/liberty", maps.MapLibreStyleUrl);
        Assert.False(maps.GeocodingEnabled);
        Assert.Contains("\"maps\":{\"provider\":\"MapLibre\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maps_style_url_is_configurable_and_a_wired_geocoder_enables_search()
    {
        using var configured = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Maps:MapLibre:StyleUrl", "https://tiles.example.test/style.json");
            b.ConfigureServices(services => services.AddSingleton<RunnersDiary.Infrastructure.Maps.IGeocoder, FakeGeocoder>());
        });

        var maps = (await configured.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, Json, TestContext.Current.CancellationToken))!.Maps;

        Assert.Equal("https://tiles.example.test/style.json", maps.MapLibreStyleUrl);
        Assert.True(maps.GeocodingEnabled);
    }

    [Fact]
    public void An_unavailable_map_or_geocoding_provider_fails_at_startup()
    {
        using var azureMaps = factory.WithWebHostBuilder(b => b.UseSetting("Maps:Provider", "AzureMaps"));
        using var azureGeocoding = factory.WithWebHostBuilder(b => b.UseSetting("Maps:Geocoding:Provider", "AzureMaps"));

        Assert.Throws<InvalidOperationException>(() => azureMaps.CreateClient());
        Assert.Throws<InvalidOperationException>(() => azureGeocoding.CreateClient());
    }

    [Fact]
    public async Task Gpx_limits_are_exposed_for_both_callers_from_configuration()
    {
        var defaults = (await factory.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, Json, TestContext.Current.CancellationToken))!.Gpx;
        using var configured = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("GpxLimits:MaxFileBytes", "1000");
            b.UseSetting("GpxLimits:GuestMaxFileBytes", "100");
        });

        var custom = (await configured.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, Json, TestContext.Current.CancellationToken))!.Gpx;
        var raw = await factory.CreateClient().GetStringAsync(Url, TestContext.Current.CancellationToken);

        Assert.Equal(new GpxClientConfig(20L * 1024 * 1024, 5L * 1024 * 1024), defaults);
        Assert.Equal(new GpxClientConfig(1000, 100), custom);
        Assert.Contains("\"gpx\":{\"maxFileBytesUser\":20971520,\"maxFileBytesGuest\":5242880}", raw, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://tiles.example.test/style.json")]
    [InlineData("/styles/local.json")]
    [InlineData("javascript:alert(1)")]
    public void A_maplibre_style_url_that_is_not_absolute_https_fails_at_startup(string url)
    {
        using var bad = factory.WithWebHostBuilder(b => b.UseSetting("Maps:MapLibre:StyleUrl", url));

        var ex = Assert.Throws<InvalidOperationException>(() => bad.CreateClient());
        Assert.Contains("StyleUrl", ex.Message, StringComparison.Ordinal);
    }
}
