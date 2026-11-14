using RunnersDiary.Domain.Gpx;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Races.RaceTestData;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class RaceProposalBuilderTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);
    private static readonly DateOnly Wide = new(2027, 12, 31);
    private static readonly GeoPoint Gliwice = new(50.2945, 18.6714);
    private static readonly DateTime Seen = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<Guid, RaceProposalState> NoStates = new Dictionary<Guid, RaceProposalState>();
    private static readonly IReadOnlyDictionary<Guid, LinkedRun> NoRuns = new Dictionary<Guid, LinkedRun>();

    private static IReadOnlyList<RaceProposal> Build(
        RaceInterestProfile profile,
        IReadOnlyList<RaceSnapshot> races,
        DateOnly? from = null,
        DateOnly? to = null,
        IReadOnlyDictionary<Guid, RaceProposalState>? states = null,
        IReadOnlyDictionary<Guid, LinkedRun>? runs = null,
        DateTime? seen = null,
        bool includeDismissed = false) =>
        RaceProposalBuilder.Build(profile, Today, from ?? Today, to ?? Wide, races, states ?? NoStates, runs ?? NoRuns, seen ?? Seen, includeDismissed);

    private static string[] Names(IReadOnlyList<RaceProposal> proposals) => [.. proposals.Select(p => p.Race.Name)];

    private static RaceSnapshot Race(string name, DateOnly date, RaceKind kind = RaceKind.Road, params double[] distances) =>
        Snapshot(name, date, kind, distances.Length == 0 ? [10_000] : distances);

    private static Dictionary<Guid, RaceProposalState> StateOf(RaceSnapshot race, RaceProposalMark mark, DateOnly? markedDate = null) =>
        new Dictionary<Guid, RaceProposalState>
        {
            [race.Id] = RaceProposalState.Create(Guid.NewGuid(), race.Id, mark, markedDate ?? race.Date, Now),
        };

    private static Dictionary<Guid, LinkedRun> RunOf(RaceSnapshot race, DateOnly? runDate = null) =>
        new Dictionary<Guid, LinkedRun> { [race.Id] = new LinkedRun(Guid.NewGuid(), runDate ?? race.Date) };

    // ---- window ----

    [Fact]
    public void Window_is_clamped_to_today_and_today_plus_horizon()
    {
        var profile = Profile(v => v with { HorizonDays = 30 });
        var races = new[]
        {
            Race("yesterday", Today.AddDays(-1)),
            Race("today", Today),
            Race("last day of horizon", Today.AddDays(30)),
            Race("after horizon", Today.AddDays(31)),
        };

        var result = Build(profile, races, from: new DateOnly(2026, 11, 1));

        Assert.Equal(["today", "last day of horizon"], Names(result));
    }

    [Fact]
    public void A_start_after_today_clamps_the_window_start()
    {
        var races = new[] { Race("early", new DateOnly(2026, 11, 20)), Race("inside", new DateOnly(2026, 12, 1)), Race("late", new DateOnly(2026, 12, 10)) };

        var result = Build(Profile(), races, from: new DateOnly(2026, 12, 1), to: new DateOnly(2026, 12, 5));

        Assert.Equal(["inside"], Names(result));
    }

    [Fact]
    public void A_requested_end_before_the_horizon_wins()
    {
        var races = new[] { Race("a", new DateOnly(2026, 11, 20)), Race("b", new DateOnly(2026, 11, 21)) };

        Assert.Equal(["a"], Names(Build(Profile(), races, to: new DateOnly(2026, 11, 20))));
    }

    [Fact]
    public void An_empty_window_yields_nothing()
    {
        var races = new[] { Race("a", Today) };

        Assert.Empty(Build(Profile(), races, from: new DateOnly(2026, 10, 1), to: new DateOnly(2026, 10, 31)));
    }

    // ---- kinds ----

    [Fact]
    public void Only_selected_kinds_match()
    {
        var profile = Profile(v => v with { Kinds = [RaceKind.Road, RaceKind.Ocr] });
        var races = new[]
        {
            Race("road", Today.AddDays(5), RaceKind.Road),
            Race("trail", Today.AddDays(5), RaceKind.Trail),
            Race("ocr", Today.AddDays(5), RaceKind.Ocr),
            Race("mountain", Today.AddDays(5), RaceKind.Mountain),
        };

        Assert.Equal(["ocr", "road"], Names(Build(profile, races)));
    }

    [Fact]
    public void Ultra_interest_also_matches_any_distance_above_a_marathon()
    {
        var profile = Profile(v => v with { Kinds = [RaceKind.Ultra] });
        var races = new[]
        {
            Race("kind ultra", Today.AddDays(5), RaceKind.Ultra, 50_000),
            Race("road 50k", Today.AddDays(6), RaceKind.Road, 10_000, 50_000),
            Race("trail marathon", Today.AddDays(7), RaceKind.Trail, 42_195),
            Race("trail 42196", Today.AddDays(8), RaceKind.Trail, 42_196),
            Race("road 10k", Today.AddDays(9), RaceKind.Road, 10_000),
        };

        Assert.Equal(["kind ultra", "road 50k", "trail 42196"], Names(Build(profile, races)));
    }

    [Fact]
    public void The_derived_ultra_rule_does_not_apply_without_an_ultra_interest()
    {
        var profile = Profile(v => v with { Kinds = [RaceKind.Road] });

        Assert.Empty(Build(profile, [Race("trail 50k", Today.AddDays(5), RaceKind.Trail, 50_000)]));
    }

    // ---- location ----

    [Fact]
    public void No_region_and_no_centre_means_everywhere()
    {
        var races = new[] { Race("silesia", Today.AddDays(5)), Snapshot("no region", Today.AddDays(6), regionCode: null) };

        Assert.Equal(["silesia", "no region"], Names(Build(Profile(), races)));
    }

    [Fact]
    public void Region_and_radius_are_a_union()
    {
        var profile = Profile(v => v with { RegionCodes = ["PL-24"], Center = Gliwice, RadiusMeters = 50_000 });
        var nearOtherRegion = new GeoPoint(50.45, 18.85); // Opole voivodeship side, about 25 km from Gliwice centre.
        var far = new GeoPoint(54.352, 18.646);
        Assert.True(Haversine.Distance(Gliwice.Latitude, Gliwice.Longitude, nearOtherRegion.Latitude, nearOtherRegion.Longitude) < 50_000);
        var races = new[]
        {
            Snapshot("region only", Today.AddDays(1), regionCode: "PL-24", point: far),
            Snapshot("radius only", Today.AddDays(2), regionCode: "PL-16", point: nearOtherRegion),
            Snapshot("radius without region code", Today.AddDays(3), regionCode: null, point: nearOtherRegion),
            Snapshot("neither", Today.AddDays(4), regionCode: "PL-22", point: far),
            Snapshot("neither without point", Today.AddDays(5), regionCode: "PL-22", point: null),
            Snapshot("region without point", Today.AddDays(6), regionCode: "PL-24", point: null),
        };

        Assert.Equal(["region only", "radius only", "radius without region code", "region without point"], Names(Build(profile, races)));
    }

    [Fact]
    public void Radius_alone_excludes_races_without_a_point_even_in_the_region()
    {
        var profile = Profile(v => v with { Center = Gliwice, RadiusMeters = 50_000 });

        Assert.Empty(Build(profile, [Snapshot("no point", Today.AddDays(1), regionCode: "PL-24", point: null)]));
    }

    [Fact]
    public void Haversine_edge_at_the_radius()
    {
        var point = new GeoPoint(50.4745, 18.6714);
        var distance = Haversine.Distance(Gliwice.Latitude, Gliwice.Longitude, point.Latitude, point.Longitude);
        Assert.InRange(distance, 19_900, 20_100);
        Assert.NotEqual(Math.Floor(distance), distance);
        var race = Snapshot("edge", Today.AddDays(1), regionCode: null, point: point);

        var atCeiling = Profile(v => v with { Center = Gliwice, RadiusMeters = (int)Math.Ceiling(distance) });
        var belowFloor = Profile(v => v with { Center = Gliwice, RadiusMeters = (int)Math.Floor(distance) });

        Assert.Single(Build(atCeiling, [race]));
        Assert.Empty(Build(belowFloor, [race]));
    }

    [Fact]
    public void Radius_is_inclusive_when_the_distance_equals_it_exactly()
    {
        // The same point as the centre is exactly 0 m away; the minimum radius still includes it.
        var profile = Profile(v => v with { Center = Gliwice, RadiusMeters = 5_000 });

        Assert.Single(Build(profile, [Snapshot("here", Today.AddDays(1), regionCode: null, point: Gliwice)]));
    }

    // ---- distance ----

    [Fact]
    public void Any_distance_inside_the_range_matches()
    {
        var profile = Profile(v => v with { MinDistanceMeters = 10_000, MaxDistanceMeters = 25_000 });
        var races = new[]
        {
            Race("has 21k", Today.AddDays(1), RaceKind.Road, 5_000, 21_100),
            Race("exactly min", Today.AddDays(2), RaceKind.Road, 10_000),
            Race("exactly max", Today.AddDays(3), RaceKind.Road, 25_000),
            Race("nothing inside", Today.AddDays(4), RaceKind.Road, 5_000, 42_195),
            Race("too short", Today.AddDays(5), RaceKind.Road, 9_999),
        };

        Assert.Equal(["has 21k", "exactly min", "exactly max"], Names(Build(profile, races)));
    }

    [Fact]
    public void Open_ended_bounds_work()
    {
        var races = new[] { Race("5k", Today.AddDays(1), RaceKind.Road, 5_000), Race("marathon", Today.AddDays(2), RaceKind.Road, 42_195) };

        Assert.Equal(["marathon"], Names(Build(Profile(v => v with { MinDistanceMeters = 20_000 }), races)));
        Assert.Equal(["5k"], Names(Build(Profile(v => v with { MaxDistanceMeters = 20_000 }), races)));
    }

    [Fact]
    public void A_race_without_distances_matches_only_when_both_bounds_are_open()
    {
        var race = Snapshot("no distances", Today.AddDays(1), distances: []);

        Assert.Single(Build(Profile(), [race]));
        Assert.Empty(Build(Profile(v => v with { MinDistanceMeters = 5_000 }), [race]));
        Assert.Empty(Build(Profile(v => v with { MaxDistanceMeters = 50_000 }), [race]));
    }

    // ---- keywords ----

    [Fact]
    public void Every_keyword_term_must_match_with_diacritics_folded()
    {
        var races = new[]
        {
            Race("Łódź Maraton Fabryczny", Today.AddDays(1)),
            Race("Łódź Park Run", Today.AddDays(2)),
            Snapshot("Maraton Dookoła Zalewu", Today.AddDays(3), locationName: "Łódź"),
            Race("Maraton Warszawski", Today.AddDays(4)),
        };

        Assert.Equal(["Łódź Maraton Fabryczny", "Maraton Dookoła Zalewu"], Names(Build(Profile(v => v with { Keywords = "lodz maraton" }), races)));
        Assert.Equal(["Łódź Maraton Fabryczny", "Łódź Park Run", "Maraton Dookoła Zalewu"], Names(Build(Profile(v => v with { Keywords = "ŁÓDŹ" }), races)));
    }

    [Fact]
    public void Keywords_match_as_substrings_of_the_name_or_the_place()
    {
        var races = new[] { Race("Gliwicki Bieg Nocny", Today.AddDays(1)) };

        Assert.Single(Build(Profile(v => v with { Keywords = "gliwic noc" }), races));
        Assert.Empty(Build(Profile(v => v with { Keywords = "gliwic dzien" }), races));
    }

    // ---- state ----

    [Fact]
    public void Dismissed_races_are_hidden_unless_included()
    {
        var race = Race("dismissed", Today.AddDays(1));
        var states = StateOf(race, RaceProposalMark.Dismissed);

        Assert.Empty(Build(Profile(), [race], states: states));
        var shown = Assert.Single(Build(Profile(), [race], states: states, includeDismissed: true));
        Assert.Equal(ProposalState.Dismissed, shown.State);
    }

    [Fact]
    public void Interested_and_plain_states_are_reported()
    {
        var interested = Race("interested", Today.AddDays(1));
        var plain = Race("plain", Today.AddDays(2));

        var result = Build(Profile(), [interested, plain], states: StateOf(interested, RaceProposalMark.Interested));

        Assert.Equal(ProposalState.Interested, result[0].State);
        Assert.Equal(ProposalState.None, result[1].State);
        Assert.Null(result[1].LinkedRunId);
    }

    [Fact]
    public void Signed_up_races_are_always_shown_even_outside_filters_horizon_and_dismissal()
    {
        var profile = Profile(v => v with { Kinds = [RaceKind.Road], HorizonDays = 30, MinDistanceMeters = 5_000, MaxDistanceMeters = 10_000 });
        var race = Race("far trail 100k", Today.AddDays(200), RaceKind.Trail, 100_000);
        var runs = RunOf(race);

        var result = Build(profile, [race], runs: runs, states: StateOf(race, RaceProposalMark.Dismissed));

        var proposal = Assert.Single(result);
        Assert.Equal(ProposalState.SignedUp, proposal.State);
        Assert.Equal(runs[race.Id].RunId, proposal.LinkedRunId);
    }

    [Fact]
    public void Signed_up_past_race_inside_the_requested_range_is_shown_but_outside_it_is_not()
    {
        var race = Race("done", Today.AddDays(-10));

        Assert.Single(Build(Profile(), [race], from: Today.AddDays(-30), to: Today.AddDays(30), runs: RunOf(race)));
        Assert.Empty(Build(Profile(), [race], from: Today, to: Today.AddDays(30), runs: RunOf(race)));
        Assert.Empty(Build(Profile(), [race], from: Today.AddDays(-30), to: Today.AddDays(30)));
    }

    [Fact]
    public void Removed_races_are_hidden_unless_signed_up_and_duplicates_never_show()
    {
        var removed = Snapshot("removed", Today.AddDays(1)) with { RemovedUtc = Seen };
        var signed = Snapshot("removed but signed up", Today.AddDays(2)) with { RemovedUtc = Seen };
        var duplicate = Snapshot("duplicate", Today.AddDays(3)) with { DuplicateOfRaceId = Guid.NewGuid() };

        var result = Build(Profile(), [removed, signed, duplicate], runs: RunOf(signed));

        Assert.Equal(["removed but signed up"], Names(result));
    }

    // ---- ordering ----

    [Fact]
    public void Orders_by_date_then_signed_up_interested_proximity_name_and_id()
    {
        var profile = Profile(v => v with { Center = Gliwice, RadiusMeters = 100_000 });
        var day = Today.AddDays(10);
        var near = new GeoPoint(50.30, 18.68);
        var mid = new GeoPoint(50.45, 18.70);
        var edge = new GeoPoint(50.95, 18.70);
        var idLow = new Guid("00000000-0000-0000-0000-000000000001");
        var idHigh = new Guid("00000000-0000-0000-0000-000000000002");

        var later = Snapshot("A later day", day.AddDays(1), regionCode: null, point: near);
        var signed = Snapshot("Z signed up", day, regionCode: null, point: edge);
        var interested = Snapshot("Y interested", day, regionCode: null, point: edge);
        var nearest = Snapshot("M nearest", day, regionCode: null, point: near);
        var farther = Snapshot("B farther", day, regionCode: null, point: mid, id: new Guid("00000000-0000-0000-0000-000000000003"));
        var tieB = Snapshot("b tie", day, regionCode: null, point: mid, id: idHigh);
        var tieA = Snapshot("B Farther", day, regionCode: null, point: mid, id: idLow);

        var states = StateOf(interested, RaceProposalMark.Interested);
        var result = Build(profile, [later, farther, tieB, signed, nearest, interested, tieA], states: states, runs: RunOf(signed));

        Assert.Equal(["Z signed up", "Y interested", "M nearest", "B Farther", "B farther", "b tie", "A later day"], Names(result));
    }

    [Fact]
    public void Without_a_centre_proximity_ties_and_the_name_orders_races_of_a_day()
    {
        var day = Today.AddDays(3);

        var result = Build(Profile(), [Race("beta", day), Race("Alfa", day), Race("gamma", day)]);

        Assert.Equal(["Alfa", "beta", "gamma"], Names(result));
    }

    [Fact]
    public void With_a_centre_races_without_a_point_sort_after_those_with_one()
    {
        var profile = Profile(v => v with { Center = Gliwice, RadiusMeters = 50_000, RegionCodes = ["PL-24"] });
        var day = Today.AddDays(3);

        var result = Build(profile, [Snapshot("a no point", day, point: null), Snapshot("z with point", day, point: new GeoPoint(50.29, 18.67))]);

        Assert.Equal(["z with point", "a no point"], Names(result));
    }

    // ---- isNew ----

    [Fact]
    public void Is_new_compares_import_time_to_the_seen_marker()
    {
        var older = Snapshot("older", Today.AddDays(1), importedUtc: Seen.AddDays(-1));
        var same = Snapshot("same", Today.AddDays(2), importedUtc: Seen);
        var newer = Snapshot("newer", Today.AddDays(3), importedUtc: Seen.AddSeconds(1));

        var result = Build(Profile(), [older, same, newer]);

        Assert.Equal([false, false, true], result.Select(p => p.IsNew).ToArray());
    }

    [Fact]
    public void Everything_is_new_when_the_list_was_never_opened()
    {
        var race = Snapshot("a", Today.AddDays(1), importedUtc: Seen.AddYears(-1));

        var result = RaceProposalBuilder.Build(Profile(), Today, Today, Wide, [race], NoStates, NoRuns, seenUtc: null, includeDismissed: false);

        Assert.True(Assert.Single(result).IsNew);
    }

    // ---- dateChanged ----

    [Fact]
    public void Interested_is_date_changed_when_the_marked_date_differs()
    {
        var race = Race("moved", Today.AddDays(10));
        var same = Race("same", Today.AddDays(11));

        var result = Build(Profile(), [race, same], states: new Dictionary<Guid, RaceProposalState>(StateOf(race, RaceProposalMark.Interested, race.Date.AddDays(-7)))
        {
            [same.Id] = RaceProposalState.Create(Guid.NewGuid(), same.Id, RaceProposalMark.Interested, same.Date, Now),
        });

        Assert.True(result[0].DateChanged);
        Assert.False(result[1].DateChanged);
    }

    [Fact]
    public void Dismissed_and_unmarked_races_are_never_date_changed()
    {
        var dismissed = Race("dismissed", Today.AddDays(10));
        var plain = Race("plain", Today.AddDays(11));

        var result = Build(Profile(), [dismissed, plain], states: StateOf(dismissed, RaceProposalMark.Dismissed, dismissed.Date.AddDays(-7)), includeDismissed: true);

        Assert.All(result, p => Assert.False(p.DateChanged));
    }

    [Fact]
    public void Signed_up_is_date_changed_when_the_run_date_differs_in_either_direction()
    {
        var race = Race("signed", Today.AddDays(10));

        Assert.True(Assert.Single(Build(Profile(), [race], runs: RunOf(race, race.Date.AddDays(-7)))).DateChanged);
        Assert.True(Assert.Single(Build(Profile(), [race], runs: RunOf(race, race.Date.AddDays(7)))).DateChanged);
        Assert.False(Assert.Single(Build(Profile(), [race], runs: RunOf(race))).DateChanged);
    }

    [Fact]
    public void For_a_signed_up_race_the_run_decides_not_the_interested_mark()
    {
        var race = Race("signed", Today.AddDays(10));

        var result = Build(Profile(), [race], states: StateOf(race, RaceProposalMark.Interested, race.Date.AddDays(-7)), runs: RunOf(race));

        Assert.False(Assert.Single(result).DateChanged);
    }
}
