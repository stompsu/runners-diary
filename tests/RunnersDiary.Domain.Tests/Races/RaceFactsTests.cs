using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Races.RaceTestData;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class RaceFactsTests
{
    private static void AssertError(RaceFacts facts, string path) =>
        Assert.Contains(facts.Normalized().Validate(), e => e.Path == path);

    [Fact]
    public void Baseline_is_valid_and_normalisation_is_idempotent()
    {
        var facts = Facts();

        Assert.Empty(facts.Normalized().Validate());
        Assert.Equal(facts.Normalized().DistancesMeters, facts.Normalized().Normalized().DistancesMeters);
    }

    [Fact]
    public void Name_is_trimmed_collapsed_and_control_characters_are_removed()
    {
        var facts = Facts(f => f with { Name = "  Bieg\tGliwice\u0007 \n Night  " }).Normalized();
        var inside = Facts(f => f with { Name = "Gli\u0007wice" }).Normalized();

        Assert.Equal("Bieg Gliwice Night", facts.Name);
        Assert.Equal("Gliwice", inside.Name);
    }

    [Fact]
    public void Name_length_is_one_to_two_hundred_after_cleaning()
    {
        Assert.Empty(Facts(f => f with { Name = new string('a', 200) }).Normalized().Validate());
        AssertError(Facts(f => f with { Name = new string('a', 201) }), "name");
        AssertError(Facts(f => f with { Name = " \t\u0001 " }), "name");
        Assert.Empty(Facts(f => f with { Name = "  " + new string('a', 200) + "  " }).Normalized().Validate());
    }

    [Fact]
    public void Date_must_be_between_1950_and_2100()
    {
        Assert.Empty(Facts(f => f with { Date = new DateOnly(1950, 1, 1) }).Validate());
        Assert.Empty(Facts(f => f with { Date = new DateOnly(2100, 12, 31) }).Validate());
        AssertError(Facts(f => f with { Date = new DateOnly(1949, 12, 31) }), "date");
        AssertError(Facts(f => f with { Date = new DateOnly(2101, 1, 1) }), "date");
    }

    [Fact]
    public void Distances_are_deduplicated_sorted_and_rounded_to_a_tenth_of_a_metre()
    {
        var facts = Facts(f => f with { DistancesMeters = [21_100, 10_000, 10_000.04, 5_000] }).Normalized();

        Assert.Equal([5_000, 10_000, 21_100], facts.DistancesMeters);
    }

    [Fact]
    public void Distance_bounds_and_count_are_validated()
    {
        Assert.Empty(Facts(f => f with { DistancesMeters = [1_000, 250_000] }).Normalized().Validate());
        Assert.Empty(Facts(f => f with { DistancesMeters = [] }).Normalized().Validate());
        AssertError(Facts(f => f with { DistancesMeters = [999] }), "distancesMeters");
        AssertError(Facts(f => f with { DistancesMeters = [250_001] }), "distancesMeters");
        AssertError(Facts(f => f with { DistancesMeters = [double.NaN] }), "distancesMeters");

        var twelve = Enumerable.Range(1, 12).Select(i => i * 1_000d).ToList();
        Assert.Empty(Facts(f => f with { DistancesMeters = twelve }).Normalized().Validate());
        twelve.Add(13_000);
        AssertError(Facts(f => f with { DistancesMeters = twelve }), "distancesMeters");
    }

    [Theory]
    [InlineData("https://example.org/a", true)]
    [InlineData("http://example.org/a", true)]
    [InlineData("ftp://example.org/a", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative/path", false)]
    [InlineData("example.org", false)]
    public void Urls_must_be_absolute_http_or_https(string url, bool valid)
    {
        foreach (var make in new Func<RaceFacts, string?, RaceFacts>[]
        {
            (f, u) => f with { SignupUrl = u },
            (f, u) => f with { EventUrl = u },
            (f, u) => f with { SourceUrl = u },
        })
        {
            var errors = make(Facts(), url).Normalized().Validate();
            Assert.Equal(valid, errors.Count == 0);
        }
    }

    [Fact]
    public void Url_length_limit_is_2048_and_blank_becomes_null()
    {
        var ok = "https://example.org/" + new string('a', 2048 - 20);
        Assert.Equal(2048, ok.Length);
        Assert.Empty(Facts(f => f with { SignupUrl = ok }).Normalized().Validate());
        AssertError(Facts(f => f with { SignupUrl = ok + "a" }), "signupUrl");
        Assert.Null(Facts(f => f with { EventUrl = "   " }).Normalized().EventUrl);
    }

    [Fact]
    public void Real_sources_require_a_source_url()
    {
        var real = Facts(f => f with { Source = RaceSource.RunningLife, SourceUrl = null });

        AssertError(real, "sourceUrl");
        Assert.Empty((real with { SourceUrl = "https://example.org/race" }).Normalized().Validate());
    }

    [Theory]
    [InlineData("jan.kowalski@example.org")]
    [InlineData("+48 600 100 200")]
    [InlineData("600-100-200")]
    [InlineData("(32) 555 12 34")]
    public void Organiser_that_looks_like_email_or_phone_becomes_null(string organiser) =>
        Assert.Null(Facts(f => f with { OrganizerName = organiser }).Normalized().OrganizerName);

    [Theory]
    [InlineData("Klub Biegacza Gliwice")]
    [InlineData("KS Maraton 2027")]
    [InlineData("Fundacja 1000 kroków")]
    public void Organisation_names_are_kept(string organiser) =>
        Assert.Equal(organiser, Facts(f => f with { OrganizerName = organiser }).Normalized().OrganizerName);

    [Fact]
    public void Organiser_length_is_limited_to_150()
    {
        AssertError(Facts(f => f with { OrganizerName = new string('k', 151) }), "organizerName");
        Assert.Empty(Facts(f => f with { OrganizerName = new string('k', 150) }).Normalized().Validate());
    }

    [Fact]
    public void Price_is_both_or_neither_with_a_three_letter_currency()
    {
        AssertError(Facts(f => f with { PriceCurrency = null }), "priceAmount");
        AssertError(Facts(f => f with { PriceAmount = null }), "priceAmount");
        Assert.Empty(Facts(f => f with { PriceAmount = null, PriceCurrency = null }).Normalized().Validate());
        Assert.Equal("PLN", Facts(f => f with { PriceCurrency = " pln " }).Normalized().PriceCurrency);
        AssertError(Facts(f => f with { PriceCurrency = "PL" }), "priceCurrency");
        AssertError(Facts(f => f with { PriceCurrency = "PLN1" }), "priceCurrency");
        AssertError(Facts(f => f with { PriceCurrency = "P1N" }), "priceCurrency");
        AssertError(Facts(f => f with { PriceAmount = -1m }), "priceAmount");
        Assert.Empty(Facts(f => f with { PriceAmount = 0m }).Normalized().Validate());
    }

    [Fact]
    public void Country_and_region_codes_are_normalised_and_checked()
    {
        var facts = Facts(f => f with { CountryCode = "pl", RegionCode = "pl-24" }).Normalized();

        Assert.Equal("PL", facts.CountryCode);
        Assert.Equal("PL-24", facts.RegionCode);
        AssertError(Facts(f => f with { CountryCode = "POL" }), "countryCode");
        AssertError(Facts(f => f with { RegionCode = "Silesia" }), "regionCode");
    }

    [Fact]
    public void Identity_surface_kind_and_elevation_are_validated()
    {
        AssertError(Facts(f => f with { ExternalId = "  " }), "externalId");
        AssertError(Facts(f => f with { ExternalId = new string('x', 101) }), "externalId");
        AssertError(Facts(f => f with { Kind = (RaceKind)99 }), "kind");
        AssertError(Facts(f => f with { Surface = (Surface)99 }), "surface");
        AssertError(Facts(f => f with { Source = (RaceSource)7 }), "source");
        AssertError(Facts(f => f with { ElevationGainMeters = -1 }), "elevationGainMeters");
        AssertError(Facts(f => f with { ElevationGainMeters = 100_001 }), "elevationGainMeters");
        Assert.Empty(Facts(f => f with { ElevationGainMeters = 0 }).Normalized().Validate());
    }

    [Fact]
    public void Start_time_loses_sub_second_digits()
    {
        var facts = Facts(f => f with { StartTime = new TimeOnly(9, 30, 15, 750) }).Normalized();

        Assert.Equal(new TimeOnly(9, 30, 15), facts.StartTime);
    }

    [Fact]
    public void TryNormalize_reports_errors()
    {
        Assert.False(RaceFacts.TryNormalize(Facts(f => f with { Name = "" }), out _, out var errors));
        Assert.Contains(errors, e => e.Path == "name");
        Assert.True(RaceFacts.TryNormalize(Facts(), out var facts, out var none));
        Assert.Empty(none);
        Assert.Equal("Demo Gliwice Night 10K", facts.Name);
    }
}
