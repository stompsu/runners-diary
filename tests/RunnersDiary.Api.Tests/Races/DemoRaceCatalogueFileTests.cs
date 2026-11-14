using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RunnersDiary.Domain.Races;
using RunnersDiary.Infrastructure.Races;

namespace RunnersDiary.Api.Tests.Races;

/// <summary>The synthetic demo catalogue file and its startup check (no containers needed).</summary>
public sealed class DemoRaceCatalogueFileTests
{
    private static readonly DateOnly Start = new(2026, 11, 13);

    private static DemoRaceCatalogue Catalogue() => new(
        Options.Create(new RacesOptions()), new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        TimeProvider.System, NullLogger<DemoRaceCatalogue>.Instance);

    [Fact]
    public void The_startup_check_passes_for_every_day_of_two_years_including_a_leap_year()
    {
        var catalogue = Catalogue();

        for (var day = Start; day < Start.AddDays(2 * 366); day = day.AddDays(1))
        {
            var facts = catalogue.Validate(day);
            Assert.Equal(40, facts.Count);
            Assert.True(facts.Count(f => f.Date > day && f.RegionCode == "PL-24") >= DemoRaceCatalogue.MinFutureSilesianRaces, day.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Dates_are_resolved_relative_to_the_current_iso_week_in_the_catalogue_zone()
    {
        var facts = Catalogue().Expand(Start); // Friday 2026-11-13: week 0 starts Monday 2026-11-09

        var rybnik = facts.Single(f => f.ExternalId == "demo-16"); // week 0, Sunday
        Assert.Equal(new DateOnly(2026, 11, 15), rybnik.Date);
        var earliest = facts.Min(f => f.Date);
        Assert.Equal(new DateOnly(2026, 10, 31), earliest); // week -2, Saturday
    }

    [Fact]
    public void The_catalogue_is_synthetic_and_covers_the_cases_the_plan_requires()
    {
        var facts = Catalogue().Expand(Start);

        Assert.All(facts, f =>
        {
            Assert.Equal(RaceSource.Demo, f.Source);
            Assert.Null(f.SourceUrl);
            Assert.Equal("https://example.org/demo-signup", f.SignupUrl);
            Assert.StartsWith("Demo ", f.Name, StringComparison.Ordinal);
            Assert.NotNull(f.Point);
        });
        Assert.Equal(RaceKinds_All(), facts.Select(f => f.Kind).Distinct().Order().ToList());
        Assert.True(facts.Count(f => f.RegionCode == "PL-24") >= 12);
        Assert.Contains(facts, f => f.DistancesMeters.Count == 3);
        Assert.Contains(facts, f => f.Kind == RaceKind.Ultra && f.DistancesMeters.Max() > 42_195);
        Assert.Contains(facts, f => f.Kind == RaceKind.Ocr);
        foreach (var city in new[] { "Gliwice", "Katowice", "Bielsko-Biała", "Rybnik" })
        {
            Assert.Contains(facts, f => f.RegionCode == "PL-24" && f.LocationName == city);
        }

        Assert.Contains(facts, f => f.LocationName == "Gdańsk");
        Assert.Contains(facts, f => f.LocationName == "Wrocław");
        Assert.Contains(facts, f => f.LocationName == "Zakopane");
    }

    private static List<RaceKind> RaceKinds_All() => [.. Enum.GetValues<RaceKind>().Order()];
}
