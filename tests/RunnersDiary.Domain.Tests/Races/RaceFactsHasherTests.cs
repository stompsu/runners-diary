using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Races.RaceTestData;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class RaceFactsHasherTests
{
    [Fact]
    public void Hash_is_32_bytes_and_stable()
    {
        var a = RaceFactsHasher.Hash(Facts());
        var b = RaceFactsHasher.Hash(Facts());

        Assert.Equal(RaceFactsHasher.HashLength, a.Length);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Canonical_json_has_a_fixed_property_order_and_sorted_distances()
    {
        var facts = Facts(f => f with { DistancesMeters = [21_097.5, 10_000], StartTime = new TimeOnly(9, 30), PriceAmount = 60.00m });

        const string expected =
            "{\"source\":100,\"externalId\":\"demo-1\",\"name\":\"Demo Gliwice Night 10K\",\"date\":\"2027-03-14\",\"startTime\":\"09:30:00\","
            + "\"locationName\":\"Gliwice\",\"point\":{\"lat\":50.2945,\"lon\":18.6714},\"countryCode\":\"PL\",\"regionCode\":\"PL-24\",\"kind\":0,\"surface\":0,"
            + "\"distancesMeters\":[10000,21097.5],\"elevationGainMeters\":50,\"signupUrl\":\"https://example.org/demo-signup\",\"eventUrl\":null,\"sourceUrl\":null,"
            + "\"priceAmount\":\"60\",\"priceCurrency\":\"PLN\",\"signupDeadline\":\"2027-03-01\",\"organizerName\":\"Klub Biegacza Gliwice\"}";
        Assert.Equal(expected, RaceFactsHasher.CanonicalJsonString(facts));
    }

    [Fact]
    public void Distances_are_order_insensitive_and_duplicates_collapse()
    {
        var a = Facts(f => f with { DistancesMeters = [10_000, 21_100] });
        var b = Facts(f => f with { DistancesMeters = [21_100, 10_000, 10_000] });

        Assert.Equal(RaceFactsHasher.Hash(a), RaceFactsHasher.Hash(b));
        Assert.NotEqual(RaceFactsHasher.Hash(a), RaceFactsHasher.Hash(Facts(f => f with { DistancesMeters = [10_000] })));
    }

    [Fact]
    public void Coordinates_are_rounded_to_six_decimals()
    {
        var baseline = Facts(f => f with { Point = new GeoPoint(50.294500, 18.671400) });
        var below = Facts(f => f with { Point = new GeoPoint(50.2945004, 18.6714004) });
        var above = Facts(f => f with { Point = new GeoPoint(50.2945006, 18.671400) });

        Assert.Equal(RaceFactsHasher.Hash(baseline), RaceFactsHasher.Hash(below));
        Assert.NotEqual(RaceFactsHasher.Hash(baseline), RaceFactsHasher.Hash(above));
    }

    [Fact]
    public void Any_stored_url_change_changes_the_hash()
    {
        var baseline = RaceFactsHasher.Hash(Facts());

        Assert.NotEqual(baseline, RaceFactsHasher.Hash(Facts(f => f with { SignupUrl = "https://example.org/other" })));
        Assert.NotEqual(baseline, RaceFactsHasher.Hash(Facts(f => f with { EventUrl = "https://example.org/event" })));
        Assert.NotEqual(baseline, RaceFactsHasher.Hash(Facts(f => f with { SourceUrl = "https://example.org/source" })));
    }

    [Fact]
    public void Changes_to_other_facts_change_the_hash()
    {
        var baseline = RaceFactsHasher.Hash(Facts());
        Func<RaceFacts, RaceFacts>[] changes =
        [
            f => f with { Name = "Other" },
            f => f with { Date = f.Date.AddDays(1) },
            f => f with { StartTime = null },
            f => f with { Kind = RaceKind.Trail },
            f => f with { Point = null },
            f => f with { PriceAmount = 61m },
            f => f with { OrganizerName = null },
            f => f with { ElevationGainMeters = 51 },
        ];

        foreach (var change in changes)
        {
            Assert.NotEqual(baseline, RaceFactsHasher.Hash(Facts(change)));
        }
    }

    [Fact]
    public void Raw_and_normalised_facts_hash_alike()
    {
        var raw = Facts(f => f with { Name = "  Demo   Gliwice Night 10K ", PriceCurrency = "pln", PriceAmount = 60.0m });

        Assert.Equal(RaceFactsHasher.Hash(Facts()), RaceFactsHasher.Hash(raw));
    }
}
