using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Races;
using RunnersDiary.Domain.Runs;
using static RunnersDiary.Domain.Tests.Races.RaceTestData;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class RaceInterestRulesTests
{
    private static RaceInterestValues Valid(Func<RaceInterestValues, RaceInterestValues>? change = null)
    {
        var v = RaceInterestValues.Defaults;
        return change is null ? v : change(v);
    }

    private static void AssertError(RaceInterestValues values, string path) =>
        Assert.Contains(RaceInterestRules.Validate(values), e => e.Path == path);

    private static void AssertOk(RaceInterestValues values) => Assert.Empty(RaceInterestRules.Validate(values));

    private static readonly GeoPoint Gliwice = new(50.2945, 18.6714);

    [Fact]
    public void Defaults_are_valid_road_and_trail_5k_to_marathon_120_days()
    {
        var d = RaceInterestValues.Defaults;

        AssertOk(d);
        Assert.Equal([RaceKind.Road, RaceKind.Trail], d.Kinds);
        Assert.Equal(5_000, d.MinDistanceMeters);
        Assert.Equal(42_195, d.MaxDistanceMeters);
        Assert.Equal(120, d.HorizonDays);
        Assert.True(d.ShowInCalendar);
    }

    [Fact]
    public void Kinds_need_at_least_one_known_kind()
    {
        AssertError(Valid(v => v with { Kinds = [] }), "kinds");
        AssertError(Valid(v => v with { Kinds = [(RaceKind)99] }), "kinds");
        AssertOk(Valid(v => v with { Kinds = [RaceKind.Ultra] }));
    }

    [Fact]
    public void Regions_are_known_codes_and_at_most_sixteen()
    {
        var all = PolishRegions.All.Select(r => r.Code).ToList();
        AssertOk(Valid(v => v with { RegionCodes = all }));
        AssertError(Valid(v => v with { RegionCodes = [.. all, "PL-24"] }), "regionCodes");
        AssertError(Valid(v => v with { RegionCodes = ["PL-99"] }), "regionCodes");
        AssertError(Valid(v => v with { RegionCodes = ["Silesia"] }), "regionCodes");
        AssertOk(Valid(v => v with { RegionCodes = ["pl-24"] }));
    }

    [Fact]
    public void Centre_and_radius_come_together()
    {
        AssertError(Valid(v => v with { RadiusMeters = 50_000 }), "center");
        AssertError(Valid(v => v with { Center = Gliwice }), "radiusMeters");
        AssertOk(Valid(v => v with { Center = Gliwice, RadiusMeters = 50_000 }));
    }

    [Theory]
    [InlineData(4_000, false)]
    [InlineData(4_999, false)]
    [InlineData(5_000, true)]
    [InlineData(300_000, true)]
    [InlineData(300_001, false)]
    public void Radius_is_between_5_and_300_km(int radius, bool valid)
    {
        var errors = RaceInterestRules.Validate(Valid(v => v with { Center = Gliwice, RadiusMeters = radius }));

        Assert.Equal(valid, errors.Count == 0);
        if (!valid)
        {
            Assert.Contains(errors, e => e.Path == "radiusMeters");
        }
    }

    [Fact]
    public void Distance_range_is_1_to_250_km_and_min_not_above_max()
    {
        AssertOk(Valid(v => v with { MinDistanceMeters = 1_000, MaxDistanceMeters = 250_000 }));
        AssertOk(Valid(v => v with { MinDistanceMeters = 10_000, MaxDistanceMeters = 10_000 }));
        AssertOk(Valid(v => v with { MinDistanceMeters = null, MaxDistanceMeters = null }));
        AssertError(Valid(v => v with { MinDistanceMeters = 999 }), "minDistanceMeters");
        AssertError(Valid(v => v with { MaxDistanceMeters = 250_001 }), "maxDistanceMeters");
        AssertError(Valid(v => v with { MinDistanceMeters = 20_000, MaxDistanceMeters = 10_000 }), "maxDistanceMeters");
        AssertError(Valid(v => v with { MinDistanceMeters = double.NaN }), "minDistanceMeters");
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    [InlineData(0, false)]
    public void Horizon_is_7_to_365_days(int days, bool valid)
    {
        var errors = RaceInterestRules.Validate(Valid(v => v with { HorizonDays = days }));

        Assert.Equal(valid, errors.Count == 0);
    }

    [Fact]
    public void Keywords_are_at_most_five_terms_of_two_to_thirty_characters_within_100()
    {
        AssertOk(Valid(v => v with { Keywords = null }));
        AssertOk(Valid(v => v with { Keywords = "   " }));
        AssertOk(Valid(v => v with { Keywords = "gliwice night  10k   noc  nocny" }));
        AssertError(Valid(v => v with { Keywords = "a b c d e f" }), "keywords");
        AssertError(Valid(v => v with { Keywords = "gliwice x" }), "keywords");
        AssertOk(Valid(v => v with { Keywords = "ab " + new string('c', 30) }));
        AssertError(Valid(v => v with { Keywords = new string('c', 31) }), "keywords");
        var longTerms = string.Join(' ', Enumerable.Repeat(new string('c', 20), 5));
        Assert.Equal(104, longTerms.Length);
        AssertError(Valid(v => v with { Keywords = longTerms }), "keywords");
        var under100 = string.Join(' ', Enumerable.Repeat(new string('c', 19), 5));
        Assert.Equal(99, under100.Length);
        AssertOk(Valid(v => v with { Keywords = under100 }));
    }

    [Fact]
    public void Centre_name_is_limited_to_200()
    {
        AssertError(Valid(v => v with { Center = Gliwice, RadiusMeters = 10_000, CenterName = new string('x', 201) }), "centerName");
        AssertOk(Valid(v => v with { Center = Gliwice, RadiusMeters = 10_000, CenterName = new string('x', 200) }));
    }

    [Fact]
    public void EnsureValid_throws_with_all_errors()
    {
        var ex = Assert.Throws<DomainRuleException>(() => RaceInterestRules.EnsureValid(Valid(v => v with { Kinds = [], HorizonDays = 3 })));

        Assert.Contains(ex.Errors, e => e.Path == "kinds");
        Assert.Contains(ex.Errors, e => e.Path == "horizonDays");
    }

    [Fact]
    public void Kind_bit_set_uses_one_shifted_by_the_kind_value()
    {
        Assert.Equal((RaceKinds)1, RaceKind.Road.ToSet());
        Assert.Equal((RaceKinds)2, RaceKind.Trail.ToSet());
        Assert.Equal((RaceKinds)4, RaceKind.Mountain.ToSet());
        Assert.Equal((RaceKinds)8, RaceKind.Ultra.ToSet());
        Assert.Equal((RaceKinds)16, RaceKind.CrossCountry.ToSet());
        Assert.Equal((RaceKinds)32, RaceKind.Ocr.ToSet());
        Assert.Equal((RaceKinds)64, RaceKind.Other.ToSet());
        Assert.Equal((RaceKinds)127, RaceKindsExtensions.All);
        Assert.Equal((RaceKinds)10, new[] { RaceKind.Trail, RaceKind.Ultra, RaceKind.Trail }.ToSet());
        Assert.Equal([RaceKind.Trail, RaceKind.Ultra], ((RaceKinds)10).ToKinds());
        Assert.False(RaceKinds.None.Contains(RaceKind.Road));
        Assert.False(RaceKindsExtensions.All.Contains((RaceKind)99));
    }

    [Fact]
    public void Profile_create_normalises_and_round_trips()
    {
        var userId = Guid.NewGuid();
        var values = Valid(v => v with
        {
            Kinds = [RaceKind.Trail, RaceKind.Road, RaceKind.Trail],
            RegionCodes = ["pl-24", "PL-02", "PL-24"],
            Center = Gliwice,
            CenterName = "  Gliwice  ",
            RadiusMeters = 60_000,
            Keywords = "  gliwice   night ",
            ShowInCalendar = false,
        });

        var profile = RaceInterestProfile.Create(userId, values, Now);

        Assert.Equal(userId, profile.UserId);
        Assert.Equal((RaceKinds)3, profile.Kinds);
        Assert.Equal(["PL-02", "PL-24"], profile.RegionCodes);
        Assert.Equal("Gliwice", profile.CenterName);
        Assert.Equal("gliwice night", profile.Keywords);
        Assert.False(profile.ShowInCalendar);
        Assert.Null(profile.ProposalsSeenUtc);
        Assert.Equal(new DateTime(2026, 11, 13, 6, 0, 0, DateTimeKind.Utc), profile.UpdatedAt);

        var back = profile.ToValues();
        Assert.Equal([RaceKind.Road, RaceKind.Trail], back.Kinds);
        Assert.Equal(["PL-02", "PL-24"], back.RegionCodes);
        Assert.Equal(60_000, back.RadiusMeters);
    }

    [Fact]
    public void Profile_update_is_a_full_replace_and_keeps_the_seen_marker()
    {
        var profile = RaceInterestProfile.Create(Guid.NewGuid(), Valid(v => v with { Center = Gliwice, RadiusMeters = 60_000, CenterName = "Gliwice" }), Now);
        profile.MarkProposalsSeen(Now.AddHours(1));

        profile.Update(Valid(v => v with { RegionCodes = ["PL-12"], HorizonDays = 30 }), Now.AddDays(1));

        Assert.Null(profile.Center);
        Assert.Null(profile.CenterName);
        Assert.Null(profile.RadiusMeters);
        Assert.Equal(["PL-12"], profile.RegionCodes);
        Assert.Equal(30, profile.HorizonDays);
        Assert.Equal(new DateTime(2026, 11, 13, 7, 0, 0, DateTimeKind.Utc), profile.ProposalsSeenUtc);
    }

    [Fact]
    public void Profile_rejects_invalid_values_and_a_missing_owner()
    {
        Assert.Throws<DomainRuleException>(() => RaceInterestProfile.Create(Guid.NewGuid(), Valid(v => v with { Kinds = [] }), Now));
        Assert.Throws<DomainRuleException>(() => RaceInterestProfile.Create(Guid.Empty, Valid(), Now));
        Assert.Contains(typeof(IUserOwned), typeof(RaceInterestProfile).GetInterfaces());
    }

    [Fact]
    public void Proposal_state_records_mark_and_date_and_is_user_owned()
    {
        var userId = Guid.NewGuid();
        var raceId = Guid.NewGuid();

        var row = RaceProposalState.Create(userId, raceId, RaceProposalMark.Interested, new DateOnly(2027, 3, 14), Now);
        row.Set(RaceProposalMark.Dismissed, new DateOnly(2027, 3, 21), Now.AddMinutes(5));

        Assert.Equal(userId, row.UserId);
        Assert.Equal(raceId, row.RaceId);
        Assert.Equal(RaceProposalMark.Dismissed, row.State);
        Assert.Equal(new DateOnly(2027, 3, 21), row.RaceDateWhenMarked);
        Assert.Equal(new DateTime(2026, 11, 13, 6, 5, 0, DateTimeKind.Utc), row.UpdatedAt);
        Assert.Contains(typeof(IUserOwned), typeof(RaceProposalState).GetInterfaces());
        Assert.Equal(1, (byte)RaceProposalMark.Interested);
        Assert.Equal(2, (byte)RaceProposalMark.Dismissed);
        Assert.Throws<DomainRuleException>(() => RaceProposalState.Create(Guid.Empty, raceId, RaceProposalMark.Interested, new DateOnly(2027, 3, 14), Now));
        Assert.Throws<DomainRuleException>(() => row.Set((RaceProposalMark)0, new DateOnly(2027, 3, 14), Now));
    }
}
