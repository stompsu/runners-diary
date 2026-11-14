using RunnersDiary.Domain.Common;
using RunnersDiary.Domain.Races;
using static RunnersDiary.Domain.Tests.Races.RaceTestData;

namespace RunnersDiary.Domain.Tests.Races;

public sealed class RaceTests
{
    [Fact]
    public void Create_stores_normalised_facts_and_bookkeeping()
    {
        var race = Race.Create(Facts(f => f with { Name = "  Demo  Gliwice Night 10K ", DistancesMeters = [21_100, 10_000] }), Now);

        Assert.NotEqual(Guid.Empty, race.Id);
        Assert.Equal(7, race.Id.Version);
        Assert.Equal("Demo Gliwice Night 10K", race.Name);
        Assert.Equal([10_000, 21_100], race.DistancesMeters);
        Assert.Equal("Europe/Warsaw", race.TimeZoneId);
        Assert.Equal(RaceFactsHasher.Hash(Facts(f => f with { DistancesMeters = [10_000, 21_100] })), race.FactsHash);
        var expectedNow = new DateTime(2026, 11, 13, 6, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expectedNow, race.ImportedUtc);
        Assert.Equal(expectedNow, race.LastSeenUtc);
        Assert.Equal(expectedNow, race.UpdatedUtc);
        Assert.Equal(0, race.MissedScans);
        Assert.Null(race.RemovedUtc);
        Assert.Null(race.PreviousDate);
        Assert.Null(race.DuplicateOfRaceId);
    }

    [Fact]
    public void Create_leaves_the_zone_empty_for_an_unknown_country()
    {
        var race = Race.Create(Facts(f => f with { CountryCode = "DE", RegionCode = null }), Now);

        Assert.Null(race.TimeZoneId);
    }

    [Fact]
    public void Create_rejects_invalid_facts()
    {
        var ex = Assert.Throws<DomainRuleException>(() => Race.Create(Facts(f => f with { Name = "" }), Now));

        Assert.Contains(ex.Errors, e => e.Path == "name");
    }

    [Fact]
    public void ApplyFacts_updates_facts_and_hash_without_a_date_change()
    {
        var race = Race.Create(Facts(), Now);
        var later = Now.AddDays(7);

        race.ApplyFacts(Facts(f => f with { PriceAmount = 80m }), later);

        Assert.Equal(80m, race.PriceAmount);
        Assert.Null(race.PreviousDate);
        Assert.Null(race.DateChangedUtc);
        Assert.Equal(new DateTime(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc), race.UpdatedUtc);
        Assert.Equal(race.UpdatedUtc, race.LastSeenUtc);
        Assert.Equal(Now.UtcDateTime.Date, race.ImportedUtc.Date);
        Assert.Equal(RaceFactsHasher.Hash(Facts(f => f with { PriceAmount = 80m })), race.FactsHash);
    }

    [Fact]
    public void ApplyFacts_with_a_new_date_sets_previous_date()
    {
        var race = Race.Create(Facts(), Now);

        race.ApplyFacts(Facts(f => f with { Date = new DateOnly(2027, 3, 21) }), Now.AddDays(7));

        Assert.Equal(new DateOnly(2027, 3, 21), race.Date);
        Assert.Equal(new DateOnly(2027, 3, 14), race.PreviousDate);
        Assert.Equal(new DateTime(2026, 11, 20, 6, 0, 0, DateTimeKind.Utc), race.DateChangedUtc);
    }

    [Fact]
    public void ApplyFacts_clears_missed_state_and_restores_a_removed_race()
    {
        var race = Race.Create(Facts(), Now);
        race.MarkMissed(Now, 1);
        Assert.True(race.IsRemoved);

        race.ApplyFacts(Facts(f => f with { Name = "Renamed" }), Now.AddDays(1));

        Assert.False(race.IsRemoved);
        Assert.Equal(0, race.MissedScans);
    }

    [Fact]
    public void ApplyFacts_rejects_facts_of_another_race()
    {
        var race = Race.Create(Facts(), Now);

        Assert.Throws<DomainRuleException>(() => race.ApplyFacts(Facts(f => f with { ExternalId = "other" }), Now));
        Assert.Throws<DomainRuleException>(() => race.ApplyFacts(Facts(f => f with { Source = RaceSource.RunningLife, SourceUrl = "https://example.org" }), Now));
    }

    [Fact]
    public void Missed_scans_count_up_and_remove_at_the_threshold()
    {
        var race = Race.Create(Facts(), Now);

        Assert.False(race.MarkMissed(Now.AddDays(7), 2));
        Assert.Equal(1, race.MissedScans);
        Assert.Null(race.RemovedUtc);

        Assert.True(race.MarkMissed(Now.AddDays(14), 2));
        Assert.Equal(2, race.MissedScans);
        Assert.Equal(new DateTime(2026, 11, 27, 6, 0, 0, DateTimeKind.Utc), race.RemovedUtc);
    }

    [Fact]
    public void Removed_at_stays_at_the_first_removal_and_mark_seen_restores()
    {
        var race = Race.Create(Facts(), Now);
        race.Remove(Now.AddDays(1));
        race.Remove(Now.AddDays(9));
        Assert.Equal(new DateTime(2026, 11, 14, 6, 0, 0, DateTimeKind.Utc), race.RemovedUtc);

        race.MarkSeen(Now.AddDays(10));

        Assert.Null(race.RemovedUtc);
        Assert.Equal(0, race.MissedScans);
        Assert.Equal(new DateTime(2026, 11, 23, 6, 0, 0, DateTimeKind.Utc), race.LastSeenUtc);
    }

    [Fact]
    public void Restore_clears_the_removal_only()
    {
        var race = Race.Create(Facts(), Now);
        race.MarkMissed(Now, 1);

        race.Restore();

        Assert.Null(race.RemovedUtc);
        Assert.Equal(1, race.MissedScans);
    }

    [Fact]
    public void ChangeDate_is_a_no_op_for_the_same_date()
    {
        var race = Race.Create(Facts(), Now);

        race.ChangeDate(race.Date, Now.AddDays(1));

        Assert.Null(race.PreviousDate);
        Assert.Null(race.DateChangedUtc);
    }

    [Fact]
    public void A_second_date_change_keeps_only_the_latest_previous_date()
    {
        var race = Race.Create(Facts(), Now);

        race.ChangeDate(new DateOnly(2027, 3, 21), Now.AddDays(1));
        race.ChangeDate(new DateOnly(2027, 3, 28), Now.AddDays(2));

        Assert.Equal(new DateOnly(2027, 3, 21), race.PreviousDate);
        Assert.Equal(new DateOnly(2027, 3, 28), race.Date);
    }

    [Fact]
    public void Duplicate_mark_cannot_point_to_itself()
    {
        var race = Race.Create(Facts(), Now);
        var other = Guid.NewGuid();

        race.SetDuplicateOf(other);
        Assert.Equal(other, race.DuplicateOfRaceId);
        race.SetDuplicateOf(null);
        Assert.Null(race.DuplicateOfRaceId);
        Assert.Throws<DomainRuleException>(() => race.SetDuplicateOf(race.Id));
    }

    [Fact]
    public void Snapshot_and_index_entry_mirror_the_race()
    {
        var race = Race.Create(Facts(), Now);

        var snapshot = race.ToSnapshot();
        var entry = race.ToIndexEntry();

        Assert.Equal(race.Id, snapshot.Id);
        Assert.Equal(race.Name, snapshot.Name);
        Assert.Equal([10_000], snapshot.DistancesMeters);
        Assert.Equal("Europe/Warsaw", snapshot.TimeZoneId);
        Assert.Equal(race.ImportedUtc, snapshot.ImportedUtc);
        Assert.Equal(race.Id, entry.Id);
        Assert.Equal("demo-1", entry.ExternalId);
        Assert.True(entry.FactsHash.Span.SequenceEqual(race.FactsHash));
    }

    [Fact]
    public void RecordDetailFetch_stores_validators()
    {
        var race = Race.Create(Facts(), Now);

        race.RecordDetailFetch("\"abc\"", "Wed, 11 Nov 2026 10:00:00 GMT", Now);

        Assert.Equal("\"abc\"", race.DetailETag);
        Assert.Equal("Wed, 11 Nov 2026 10:00:00 GMT", race.DetailLastModified);
        Assert.Equal(new DateTime(2026, 11, 13, 6, 0, 0, DateTimeKind.Utc), race.DetailFetchedUtc);
    }

    [Fact]
    public void Race_is_not_user_owned() =>
        Assert.DoesNotContain(typeof(IUserOwned), typeof(Race).GetInterfaces());
}
