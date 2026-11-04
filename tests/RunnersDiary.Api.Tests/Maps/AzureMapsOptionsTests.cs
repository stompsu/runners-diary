using RunnersDiary.Infrastructure.Maps;

namespace RunnersDiary.Api.Tests.Maps;

public sealed class AzureMapsOptionsTests
{
    private const string ResourceId = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-runnersdiary-prod/providers/Microsoft.Maps/accounts/map-rd-prod";

    private static MapsOptions Display() => new()
    {
        Provider = MapProvider.AzureMaps,
        Geocoding = { Provider = GeocodingProvider.AzureMaps },
        AzureMaps =
        {
            AccountClientId = "abc-123",
            AccountResourceId = ResourceId,
            SasPrincipalId = "11111111-2222-3333-4444-555555555555",
            SasRegions = ["westeurope", "northeurope"],
        },
    };

    [Fact]
    public void Defaults_need_nothing()
    {
        Assert.Empty(AzureMapsOptions.Validate(new MapsOptions()));
        Assert.Equal("eu.atlas.microsoft.com", new AzureMapsOptions().Domain);
    }

    [Fact]
    public void A_complete_display_and_geocoding_setup_is_valid()
    {
        Assert.Empty(AzureMapsOptions.Validate(Display()));
    }

    [Fact]
    public void Geocoding_alone_needs_only_the_client_id_and_a_valid_domain()
    {
        var maps = new MapsOptions { Geocoding = { Provider = GeocodingProvider.AzureMaps }, AzureMaps = { AccountClientId = "abc" } };

        Assert.Empty(AzureMapsOptions.Validate(maps));

        maps.AzureMaps.AccountClientId = null;
        Assert.Contains(AzureMapsOptions.Validate(maps), m => m.Contains("AccountClientId", StringComparison.Ordinal));
    }

    [Fact]
    public void Display_requires_the_sas_settings()
    {
        var maps = Display();
        maps.AzureMaps.AccountResourceId = null;
        maps.AzureMaps.SasPrincipalId = "not-a-guid";

        var failures = AzureMapsOptions.Validate(maps);

        Assert.Contains(failures, m => m.Contains("AccountResourceId", StringComparison.Ordinal));
        Assert.Contains(failures, m => m.Contains("SasPrincipalId", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("atlas.microsoft.com.evil.example")]
    [InlineData("notatlas.microsoft.com")]
    [InlineData("eu.atlas.microsoft.com/path")]
    [InlineData("eu.atlas.microsoft.com:444")]
    [InlineData("user@eu.atlas.microsoft.com")]
    [InlineData("")]
    public void Only_azure_maps_hosts_may_receive_the_token(string domain)
    {
        var maps = Display();
        maps.AzureMaps.Domain = domain;

        Assert.Contains(AzureMapsOptions.Validate(maps), m => m.Contains("Domain", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("atlas.microsoft.com")]
    [InlineData("eu.atlas.microsoft.com")]
    [InlineData("us.atlas.microsoft.com")]
    public void Azure_maps_geography_hosts_are_accepted(string domain)
    {
        var maps = Display();
        maps.AzureMaps.Domain = domain;

        Assert.Empty(AzureMapsOptions.Validate(maps));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void The_rate_cap_must_be_within_the_azure_range(int rate)
    {
        var maps = Display();
        maps.AzureMaps.SasMaxRatePerSecond = rate;

        Assert.Contains(AzureMapsOptions.Validate(maps), m => m.Contains("SasMaxRatePerSecond", StringComparison.Ordinal));
    }

    [Fact]
    public void The_lifetime_is_bounded_and_longer_than_the_refresh_window()
    {
        var maps = Display();
        maps.AzureMaps.SasLifetime = TimeSpan.FromHours(25);
        Assert.NotEmpty(AzureMapsOptions.Validate(maps));

        maps.AzureMaps.SasLifetime = TimeSpan.FromMinutes(20);
        Assert.NotEmpty(AzureMapsOptions.Validate(maps));
    }

    [Fact]
    public void The_daily_geocode_limit_must_be_positive_and_a_bad_region_is_rejected()
    {
        var maps = Display();
        maps.Geocoding.GlobalDailyLimit = 0;
        maps.AzureMaps.SasRegions = ["West Europe!"];

        var failures = AzureMapsOptions.Validate(maps);

        Assert.Contains(failures, m => m.Contains("GlobalDailyLimit", StringComparison.Ordinal));
        Assert.Contains(failures, m => m.Contains("SasRegions", StringComparison.Ordinal));
    }

    [Fact]
    public void Failure_messages_name_keys_and_never_echo_values()
    {
        var maps = Display();
        maps.AzureMaps.AccountResourceId = "super-secret-looking-value";

        var ex = Assert.Throws<InvalidOperationException>(() => AzureMapsOptions.EnsureValid(maps));

        Assert.DoesNotContain("super-secret-looking-value", ex.Message, StringComparison.Ordinal);
    }
}
