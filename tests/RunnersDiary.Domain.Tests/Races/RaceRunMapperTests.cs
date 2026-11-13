using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.RunTypes;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Races.RaceTestData;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class RaceRunMapperTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);
    private static readonly DateOnly RaceDay = new(2027, 3, 14);
    private const string ProfileZone = "America/New_York";

    private static RunValues Map(RaceSnapshot race, RaceSignupChoice? choice = null, string zone = ProfileZone)
    {
        var values = RaceRunMapper.ToPlannedRun(race, choice ?? RaceSignupChoice.None, zone, Today);
        Assert.Empty(RunRules.Validate(values));
        return values;
    }

    private static RaceSnapshot Race(RaceKind kind = RaceKind.Road, IReadOnlyList<double>? distances = null, Surface? surface = null) =>
        Snapshot("Demo Gliwice Night 10K", RaceDay, kind, distances ?? [10_000], surface: surface);

    private static IReadOnlyList<DomainError> Errors(RaceSnapshot race, RaceSignupChoice? choice = null, string zone = ProfileZone, DateOnly? today = null)
    {
        Assert.False(RaceRunMapper.TryToPlannedRun(race, choice ?? RaceSignupChoice.None, zone, today ?? Today, out var values, out var errors));
        Assert.Null(values);
        return errors;
    }

    [Fact]
    public void Maps_a_complete_road_race()
    {
        var race = Snapshot(
            "Demo Gliwice Night 10K",
            RaceDay,
            RaceKind.Road,
            [10_000],
            point: new GeoPoint(50.2945, 18.6714),
            locationName: "Gliwice",
            startTime: new TimeOnly(10, 0),
            elevation: 50);

        var v = Map(race, new RaceSignupChoice(TargetTimeSeconds: 2_700));

        Assert.Equal(RunStatus.Planned, v.Status);
        Assert.Equal(RaceDay, v.Date);
        Assert.Equal(new TimeOnly(10, 0), v.StartTime);
        Assert.Equal("Europe/Warsaw", v.TimeZoneId);
        Assert.Equal(1, v.RunTypeId);
        Assert.Equal(10_000, v.DistanceMeters);
        Assert.Equal(Surface.Road, v.Surface);
        Assert.Equal(50, v.ElevationGainMeters);
        Assert.Equal(new RunLocation(new GeoPoint(50.2945, 18.6714), "Gliwice"), v.Location);
        Assert.Equal(new RaceDetails("Demo Gliwice Night 10K", RaceDistanceCategory.TenK, 10_000, TargetTimeSeconds: 2_700), v.Race);
        Assert.Null(v.Title);
        Assert.Null(v.Notes);
        Assert.Null(v.DurationSeconds);
        Assert.Null(v.Rating);
    }

    // ---- run type by kind (ADR 0036 §5) ----

    [Theory]
    [InlineData(RaceKind.Road, 1)]
    [InlineData(RaceKind.Other, 1)]
    [InlineData(RaceKind.Trail, 2)]
    [InlineData(RaceKind.Mountain, 2)]
    [InlineData(RaceKind.Ocr, 2)]
    [InlineData(RaceKind.CrossCountry, 5)]
    [InlineData(RaceKind.Ultra, 3)]
    public void Run_type_follows_the_kind(RaceKind kind, short expected) => Assert.Equal(expected, Map(Race(kind)).RunTypeId);

    [Fact]
    public void A_chosen_distance_above_a_marathon_makes_it_an_ultra_whatever_the_kind()
    {
        Assert.Equal(3, Map(Race(RaceKind.Road, [10_000, 50_000]), new RaceSignupChoice(DistanceMeters: 50_000)).RunTypeId);
        Assert.Equal(3, Map(Race(RaceKind.Trail, [42_196])).RunTypeId);
        Assert.Equal(2, Map(Race(RaceKind.Trail, [42_195])).RunTypeId);
        Assert.Equal(1, Map(Race(RaceKind.Road, [10_000, 50_000]), new RaceSignupChoice(DistanceMeters: 10_000)).RunTypeId);
    }

    [Fact]
    public void An_explicit_race_type_wins_and_a_non_race_type_is_rejected()
    {
        Assert.Equal(4, Map(Race(RaceKind.Road), new RaceSignupChoice(RunTypeId: 4)).RunTypeId);
        Assert.Contains(Errors(Race(), new RaceSignupChoice(RunTypeId: 20)), e => e.Path == "runTypeId");
        Assert.Contains(Errors(Race(), new RaceSignupChoice(RunTypeId: 41)), e => e.Path == "runTypeId");
        Assert.Contains(Errors(Race(), new RaceSignupChoice(RunTypeId: 999)), e => e.Path == "runTypeId");
        Assert.All(RunTypeCatalog.All.Where(t => t.Category == RunTypeCategory.Race), t => Assert.Equal(t.Id, Map(Race(), new RaceSignupChoice(RunTypeId: t.Id)).RunTypeId));
    }

    // ---- surface ----

    [Theory]
    [InlineData(RaceKind.Road, Surface.Mixed, Surface.Road)]
    [InlineData(RaceKind.Trail, null, Surface.Trail)]
    [InlineData(RaceKind.Mountain, Surface.Road, Surface.Trail)]
    [InlineData(RaceKind.CrossCountry, null, Surface.Trail)]
    [InlineData(RaceKind.Ocr, null, Surface.Trail)]
    [InlineData(RaceKind.Ultra, Surface.Mixed, Surface.Mixed)]
    [InlineData(RaceKind.Ultra, null, null)]
    [InlineData(RaceKind.Other, Surface.Track, Surface.Track)]
    public void Surface_follows_the_kind(RaceKind kind, Surface? raceSurface, Surface? expected) =>
        Assert.Equal(expected, Map(Race(kind, surface: raceSurface)).Surface);

    // ---- distance choice ----

    [Fact]
    public void A_single_distance_is_used_without_a_choice_and_may_be_confirmed()
    {
        Assert.Equal(21_097.5, Map(Race(distances: [21_097.5])).DistanceMeters);
        Assert.Equal(21_097.5, Map(Race(distances: [21_097.5]), new RaceSignupChoice(DistanceMeters: 21_097.5)).DistanceMeters);
    }

    [Fact]
    public void Several_distances_need_a_choice_that_is_one_of_them()
    {
        var race = Race(distances: [5_000, 10_000, 21_097.5]);

        Assert.Contains(Errors(race), e => e.Path == "distanceMeters");
        Assert.Contains(Errors(race, new RaceSignupChoice(DistanceMeters: 15_000)), e => e.Path == "distanceMeters");

        var v = Map(race, new RaceSignupChoice(DistanceMeters: 21_097.5));
        Assert.Equal(21_097.5, v.DistanceMeters);
        Assert.Equal(RaceDistanceCategory.HalfMarathon, v.Race!.DistanceCategory);
        Assert.Equal(21_097.5, v.Race.OfficialDistanceMeters);
    }

    [Fact]
    public void A_choice_snaps_to_the_offered_distance_within_half_a_metre()
    {
        var v = Map(Race(distances: [5_000, 10_000]), new RaceSignupChoice(DistanceMeters: 10_000.3));

        Assert.Equal(10_000, v.DistanceMeters);
    }

    [Fact]
    public void A_race_without_distances_maps_to_no_distance_and_other_category()
    {
        var v = Map(Race(distances: []));

        Assert.Null(v.DistanceMeters);
        Assert.Equal(RaceDistanceCategory.Other, v.Race!.DistanceCategory);
        Assert.Null(v.Race.OfficialDistanceMeters);
        Assert.Contains(Errors(Race(distances: []), new RaceSignupChoice(DistanceMeters: 10_000)), e => e.Path == "distanceMeters");
    }

    // ---- category edges ----

    [Theory]
    [InlineData(5_000, RaceDistanceCategory.FiveK)]
    [InlineData(4_950, RaceDistanceCategory.FiveK)]
    [InlineData(5_050, RaceDistanceCategory.FiveK)]
    [InlineData(4_949, RaceDistanceCategory.Other)]
    [InlineData(5_051, RaceDistanceCategory.Other)]
    [InlineData(9_900, RaceDistanceCategory.TenK)]
    [InlineData(10_000, RaceDistanceCategory.TenK)]
    [InlineData(10_100, RaceDistanceCategory.TenK)]
    [InlineData(9_899, RaceDistanceCategory.Other)]
    [InlineData(10_101, RaceDistanceCategory.Other)]
    [InlineData(20_887, RaceDistanceCategory.HalfMarathon)]
    [InlineData(21_097.5, RaceDistanceCategory.HalfMarathon)]
    [InlineData(21_308, RaceDistanceCategory.HalfMarathon)]
    [InlineData(20_886, RaceDistanceCategory.Other)]
    [InlineData(21_309, RaceDistanceCategory.Other)]
    [InlineData(41_774, RaceDistanceCategory.Marathon)]
    [InlineData(42_195, RaceDistanceCategory.Marathon)]
    [InlineData(42_616, RaceDistanceCategory.Marathon)]
    [InlineData(41_773, RaceDistanceCategory.Other)]
    [InlineData(42_617, RaceDistanceCategory.Other)]
    [InlineData(1_000, RaceDistanceCategory.Other)]
    [InlineData(100_000, RaceDistanceCategory.Other)]
    public void Distance_category_edges_are_one_percent_inclusive(double meters, RaceDistanceCategory expected)
    {
        Assert.Equal(expected, RaceRunMapper.CategoryFor(meters));
        Assert.Equal(expected, Map(Race(distances: [meters])).Race!.DistanceCategory);
    }

    [Fact]
    public void No_distance_is_category_other() => Assert.Equal(RaceDistanceCategory.Other, RaceRunMapper.CategoryFor(null));

    // ---- dates and time zone ----

    [Fact]
    public void A_race_dated_before_today_is_rejected_but_today_is_fine()
    {
        var past = Snapshot("past", Today.AddDays(-1));
        var todays = Snapshot("today", Today);

        Assert.Contains(Errors(past), e => e.Path == "raceId");
        Assert.Equal(Today, Map(todays).Date);
        Assert.Contains(Errors(Race(), today: RaceDay.AddDays(1)), e => e.Path == "raceId");
    }

    [Fact]
    public void ToPlannedRun_throws_a_domain_error_with_paths()
    {
        var ex = Assert.Throws<DomainRuleException>(() => RaceRunMapper.ToPlannedRun(Snapshot("past", Today.AddDays(-1)), RaceSignupChoice.None, ProfileZone, Today));

        Assert.Contains(ex.Errors, e => e.Path == "raceId");
    }

    [Fact]
    public void Time_zone_comes_from_the_race_and_falls_back_to_the_profile()
    {
        Assert.Equal("Europe/Warsaw", Map(Snapshot("a", RaceDay, timeZoneId: "Europe/Warsaw")).TimeZoneId);
        Assert.Equal("Europe/Berlin", Map(Snapshot("a", RaceDay, timeZoneId: "Europe/Berlin"), zone: "Europe/Warsaw").TimeZoneId);
        Assert.Equal(ProfileZone, Map(Snapshot("a", RaceDay, timeZoneId: null)).TimeZoneId);
        Assert.Contains(Errors(Snapshot("a", RaceDay, timeZoneId: null), zone: "Not/AZone"), e => e.Path == "timeZoneId");
    }

    [Fact]
    public void Start_time_prefers_the_choice_then_the_race()
    {
        var race = Snapshot("a", RaceDay, startTime: new TimeOnly(10, 0));

        Assert.Equal(new TimeOnly(9, 30), Map(race, new RaceSignupChoice(StartTime: new TimeOnly(9, 30))).StartTime);
        Assert.Equal(new TimeOnly(10, 0), Map(race).StartTime);
        Assert.Null(Map(Snapshot("a", RaceDay)).StartTime);
        Assert.Equal(new TimeOnly(8, 0), Map(Snapshot("a", RaceDay), new RaceSignupChoice(StartTime: new TimeOnly(8, 0))).StartTime);
    }

    // ---- name, location, target ----

    [Fact]
    public void Race_name_is_cut_to_150_characters_without_splitting_a_surrogate_pair()
    {
        var plain = Map(Snapshot(new string('a', 200), RaceDay));
        var emoji = Map(Snapshot(new string('a', 149) + "\U0001F3C3" + "tail", RaceDay));

        Assert.Equal(150, plain.Race!.Name.Length);
        Assert.Equal(149, emoji.Race!.Name.Length);
        Assert.Equal(new string('a', 149), emoji.Race.Name);
    }

    [Fact]
    public void Location_needs_a_point()
    {
        Assert.Null(Map(Snapshot("a", RaceDay, point: null, locationName: "Gliwice")).Location);
        Assert.Equal("Gliwice", Map(Snapshot("a", RaceDay, point: new GeoPoint(50, 18), locationName: "Gliwice")).Location!.Name);
        Assert.Null(Map(Snapshot("a", RaceDay, point: new GeoPoint(50, 18), locationName: null)).Location!.Name);
    }

    [Fact]
    public void Invalid_target_time_is_reported_by_the_run_rules()
    {
        var errors = Errors(Race(), new RaceSignupChoice(TargetTimeSeconds: 0));

        Assert.Contains(errors, e => e.Path == "race.targetTimeSeconds");
        Assert.Contains(Errors(Race(), new RaceSignupChoice(TargetTimeSeconds: 604_801)), e => e.Path == "race.targetTimeSeconds");
    }

    [Fact]
    public void The_mapped_values_create_a_run_that_can_be_linked_to_the_race()
    {
        var race = Race(RaceKind.Trail, [10_000, 21_097.5]);
        var values = Map(race, new RaceSignupChoice(DistanceMeters: 21_097.5));

        var run = Run.Create(Guid.NewGuid(), values, Now);
        run.LinkToCatalogRace(race.Id);

        Assert.Equal(RunStatus.Planned, run.Status);
        Assert.Equal(race.Id, run.CatalogRaceId);
        Assert.Equal(21_097.5, run.DistanceMeters);
        Assert.Equal(2, run.RunTypeId);
    }
}
