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
    public async Task Races_are_off_by_default_and_the_sources_are_listed()
    {
        var body = await factory.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, Json, TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.False(body.Races.Enabled);
        var source = Assert.Single(body.Races.Sources);
        Assert.Equal(new RaceSourceClientConfig("running-life", "running.life", "https://running.life/"), source);
    }

    [Fact]
    public async Task Races_enabled_follows_the_Races_Enabled_setting()
    {
        using var on = factory.WithWebHostBuilder(b => b.UseSetting("Races:Enabled", "true"));

        var body = await on.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, Json, TestContext.Current.CancellationToken);

        Assert.True(body?.Races.Enabled);
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

    private const string AccountResourceId = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg/providers/Microsoft.Maps/accounts/map-rd-prod";

    private static void AzureMapsSettings(Microsoft.AspNetCore.Hosting.IWebHostBuilder b, bool display, bool geocoding)
    {
        if (display)
        {
            b.UseSetting("Maps:Provider", "AzureMaps");
            b.UseSetting("Maps:AzureMaps:AccountResourceId", AccountResourceId);
            b.UseSetting("Maps:AzureMaps:SasPrincipalId", "11111111-2222-3333-4444-555555555555");
        }

        if (geocoding)
        {
            b.UseSetting("Maps:Geocoding:Provider", "AzureMaps");
        }

        b.UseSetting("Maps:AzureMaps:AccountClientId", "account-unique-id");
        b.UseSetting("Maps:AzureMaps:Domain", "eu.atlas.microsoft.com");
    }

    [Fact]
    public void An_incomplete_azure_maps_setup_fails_at_startup_naming_the_keys()
    {
        using var azureMaps = factory.WithWebHostBuilder(b => b.UseSetting("Maps:Provider", "AzureMaps"));
        using var azureGeocoding = factory.WithWebHostBuilder(b => b.UseSetting("Maps:Geocoding:Provider", "AzureMaps"));
        using var badDomain = factory.WithWebHostBuilder(b =>
        {
            AzureMapsSettings(b, display: true, geocoding: false);
            b.UseSetting("Maps:AzureMaps:Domain", "evil.example.com");
        });

        var display = Assert.Throws<InvalidOperationException>(() => azureMaps.CreateClient());
        var geocode = Assert.Throws<InvalidOperationException>(() => azureGeocoding.CreateClient());
        var domain = Assert.Throws<InvalidOperationException>(() => badDomain.CreateClient());

        Assert.Contains("Maps:AzureMaps:AccountResourceId", display.Message, StringComparison.Ordinal);
        Assert.Contains("Maps:AzureMaps:AccountClientId", geocode.Message, StringComparison.Ordinal);
        Assert.Contains("Maps:AzureMaps:Domain", domain.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Azure_maps_display_hands_the_domain_to_the_browser_and_keeps_the_maplibre_style_as_the_fallback()
    {
        using var configured = factory.WithWebHostBuilder(b => AzureMapsSettings(b, display: true, geocoding: false));

        var raw = await configured.CreateClient().GetStringAsync(Url, TestContext.Current.CancellationToken);
        var maps = (await configured.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, Json, TestContext.Current.CancellationToken))!.Maps;

        Assert.Equal(RunnersDiary.Infrastructure.Maps.MapProvider.AzureMaps, maps.Provider);
        Assert.Equal("eu.atlas.microsoft.com", maps.AzureMapsDomain);
        Assert.Equal("https://tiles.openfreemap.org/styles/liberty", maps.MapLibreStyleUrl);
        Assert.False(maps.GeocodingEnabled);
        // Only non-secret values reach the browser: no account ids, principal ids or resource ids.
        Assert.DoesNotContain("account-unique-id", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("11111111", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("subscriptions", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Azure_maps_geocoding_enables_search_without_changing_the_maplibre_display()
    {
        using var configured = factory.WithWebHostBuilder(b => AzureMapsSettings(b, display: false, geocoding: true));

        var raw = await configured.CreateClient().GetStringAsync(Url, TestContext.Current.CancellationToken);
        var maps = (await configured.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, Json, TestContext.Current.CancellationToken))!.Maps;

        Assert.Equal(RunnersDiary.Infrastructure.Maps.MapProvider.MapLibre, maps.Provider);
        Assert.Null(maps.AzureMapsDomain);
        Assert.True(maps.GeocodingEnabled);
        Assert.Contains("\"azureMapsDomain\":null", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_default_maplibre_config_has_no_azure_maps_domain()
    {
        var maps = (await factory.CreateClient().GetFromJsonAsync<ClientConfigResponse>(Url, Json, TestContext.Current.CancellationToken))!.Maps;

        Assert.Null(maps.AzureMapsDomain);
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

    [Fact]
    public async Task Ai_summary_generation_is_off_by_default_and_on_only_with_a_provider_and_the_kill_switch_on()
    {
        static void Foundry(Microsoft.AspNetCore.Hosting.IWebHostBuilder b)
        {
            b.UseSetting("Ai:Provider", "Foundry");
            b.UseSetting("Ai:Foundry:Endpoint", "https://example.invalid/");
            b.UseSetting("Ai:Foundry:Deployment", "gpt-test");
        }

        using var foundry = factory.WithWebHostBuilder(Foundry);
        using var switchedOff = factory.WithWebHostBuilder(b =>
        {
            Foundry(b);
            b.UseSetting("Ai:Enabled", "false");
        });

        var none = await factory.CreateClient().GetStringAsync(Url, TestContext.Current.CancellationToken);
        var on = await foundry.CreateClient().GetStringAsync(Url, TestContext.Current.CancellationToken);
        var off = await switchedOff.CreateClient().GetStringAsync(Url, TestContext.Current.CancellationToken);

        Assert.Contains("\"ai\":{\"summaryGeneration\":false}", none, StringComparison.Ordinal);
        Assert.Contains("\"ai\":{\"summaryGeneration\":true}", on, StringComparison.Ordinal);
        Assert.Contains("\"ai\":{\"summaryGeneration\":false}", off, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", on, StringComparison.Ordinal); // no endpoint or deployment ever reaches the browser
        Assert.DoesNotContain("gpt-test", on, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fake_ai_provider_is_refused_outside_development_and_testing_and_a_bad_foundry_setup_fails_at_startup()
    {
        using var fake = factory.WithWebHostBuilder(b => b.UseSetting("Ai:Provider", "Fake"));
        using var noEndpoint = factory.WithWebHostBuilder(b => b.UseSetting("Ai:Provider", "Foundry"));

        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => fake.CreateClient());
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => noEndpoint.CreateClient());
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
